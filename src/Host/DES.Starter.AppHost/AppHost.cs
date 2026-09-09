using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// Per-app prefix from the AppHost assembly name (DES.Starter.AppHost -> fsh-starter); namespaces Docker volumes + resource names so multiple DES apps don't clash.
#pragma warning disable CA1308 // resource + volume names are conventionally lowercase
var appPrefix = builder.Environment.ApplicationName
    .Replace(".AppHost", string.Empty, StringComparison.OrdinalIgnoreCase)
    .Replace('.', '-')
    .ToLowerInvariant();
#pragma warning restore CA1308

// Infrastructure mode switches: Container (Aspire-managed Docker), External (your own instance), or Disabled/Local.
var infra = builder.Configuration.GetSection("Infrastructure");
var postgresMode = infra.GetValue("Postgres:Mode", "Container");
var redisMode = infra.GetValue("Redis:Mode", "Container");
var storageMode = infra.GetValue("Storage:Mode", "Minio");

var useContainerPostgres = string.Equals(postgresMode, "Container", StringComparison.OrdinalIgnoreCase);
var useContainerRedis = string.Equals(redisMode, "Container", StringComparison.OrdinalIgnoreCase);
var useExternalRedis = string.Equals(redisMode, "External", StringComparison.OrdinalIgnoreCase);
var useMinio = string.Equals(storageMode, "Minio", StringComparison.OrdinalIgnoreCase);
var useExternalS3 = string.Equals(storageMode, "ExternalS3", StringComparison.OrdinalIgnoreCase);
var useLocalStorage = string.Equals(storageMode, "Local", StringComparison.OrdinalIgnoreCase);

if (!useContainerPostgres && !string.Equals(postgresMode, "External", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Infrastructure:Postgres:Mode must be 'Container' or 'External'. Got: '{postgresMode}'.");
}

// Copy externally-supplied connection strings into the standard ConnectionStrings section so Aspire's AddConnectionString picks them up.
var postgresExternalConnectionString = infra["Postgres:ConnectionString"];
if (!useContainerPostgres && !string.IsNullOrEmpty(postgresExternalConnectionString))
{
    builder.Configuration["ConnectionStrings:postgres"] = postgresExternalConnectionString;
}

var redisExternalConnectionString = infra["Redis:ConnectionString"];
if (useExternalRedis && !string.IsNullOrEmpty(redisExternalConnectionString))
{
    builder.Configuration["ConnectionStrings:redis"] = redisExternalConnectionString;
}

if (!useContainerRedis && !useExternalRedis && !string.Equals(redisMode, "None", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Infrastructure:Redis:Mode must be 'Container', 'External', or 'None'. Got: '{redisMode}'.");
}

if (!useMinio && !useExternalS3 && !useLocalStorage)
{
    throw new InvalidOperationException($"Infrastructure:Storage:Mode must be 'Minio', 'ExternalS3', or 'Local'. Got: '{storageMode}'.");
}

// Postgres
IResourceBuilder<IResourceWithConnectionString> postgres;
if (useContainerPostgres)
{
    var postgresServer = builder.AddPostgres("postgres")
        .WithDataVolume($"{appPrefix}-postgres-data")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithPgAdmin(pa => pa
            .WithHostPort(5050)
            .WithLifetime(ContainerLifetime.Persistent));

    postgres = postgresServer.AddDatabase("fsh-db");
}
else
{
    postgres = builder.AddConnectionString("postgres");
}

// Warm pooled-connection floor for the long-running API — Npgsql's default Minimum Pool Size of 0 lets the pool drain to cold, so /health/ready's ~10 concurrent DbContext checks cold-open a cohort at once and intermittently stall the probe; a floor keeps connections warm for reuse.
var apiPgConnection = ReferenceExpression.Create(
    $"{postgres.Resource.ConnectionStringExpression};Minimum Pool Size=5");

// Redis
ReferenceExpression? redisConnectionString = null;
IResourceBuilder<IResource>? redisResource = null;

if (useContainerRedis)
{
    // Valkey (BSD-3 Redis fork) as a plain container: Aspire 13.4.0 AddRedis() forces TLS-by-default in run mode and never materializes the container, so we drop to plain RESP over TCP. Name stays "redis" so config keys don't churn.
    var redisContainer = builder.AddContainer("redis", "valkey/valkey", "9.1.0")
        .WithEndpoint(targetPort: 6379, scheme: "tcp", name: "tcp")
        .WithVolume($"{appPrefix}-redis-data", "/data")
        .WithLifetime(ContainerLifetime.Persistent);

    redisResource = redisContainer;

    var redisEndpoint = redisContainer.GetEndpoint("tcp");
    redisConnectionString = ReferenceExpression.Create(
        $"{redisEndpoint.Property(EndpointProperty.HostAndPort)}");

    // RedisInsight cache browser (dev-only) sidecar; RI_REDIS_* pre-registers the Valkey connection via the container-network alias "redis".
    builder.AddContainer("redis-insight", "redis/redisinsight", "latest")
        .WithHttpEndpoint(port: 5540, targetPort: 5540, name: "http")
        .WithEnvironment("RI_REDIS_HOST0", "redis")
        .WithEnvironment("RI_REDIS_PORT0", "6379")
        .WithEnvironment("RI_REDIS_ALIAS0", "fsh-cache")
        .WithEnvironment("RI_ACCEPT_TERMS_AND_CONDITIONS", "true")
        .WithLifetime(ContainerLifetime.Persistent)
        .WaitFor(redisContainer);
}
else if (useExternalRedis)
{
    var redisConnection = builder.AddConnectionString("redis");
    redisResource = redisConnection;
    redisConnectionString = redisConnection.Resource.ConnectionStringExpression;
}

// Object storage (MinIO, S3-compatible). CORS via MINIO_API_CORS_ALLOW_ORIGIN so browser presigned PUTs from the admin (:5173)/dashboard (:5174) dev origins work without proxying through the API.
const string MinioBucket = "fsh-uploads";
const string AdminOrigin = "http://localhost:5173";
const string DashboardOrigin = "http://localhost:5174";

var minioUser = builder.AddParameter("minio-user", "minioadmin");
var minioPassword = builder.AddParameter("minio-password", "minioadmin", secret: true);

IResourceBuilder<ContainerResource>? minio = null;
IResourceBuilder<ContainerResource>? minioInit = null;
EndpointReference? minioApiEndpoint = null;

if (useMinio)
{
    minio = builder.AddContainer("minio", "minio/minio")
        .WithArgs("server", "/data", "--console-address", ":9001")
        .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
        .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
        .WithEnvironment("MINIO_ROOT_USER", minioUser)
        .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
        .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", $"{AdminOrigin},{DashboardOrigin}")
        .WithVolume($"{appPrefix}-minio-data", "/data")
        .WithLifetime(ContainerLifetime.Persistent);

    // Init container: bucket bootstrap (create + public-read). Script normalized to LF so /bin/sh in minio/mc doesn't choke on Windows CRLF.
    var minioInitScript = ($$"""
        until mc alias set local http://minio:9000 "$MC_USER" "$MC_PASS"; do
          echo "waiting for minio...";
          sleep 2;
        done;
        mc mb --ignore-existing local/{{MinioBucket}};
        mc anonymous set download local/{{MinioBucket}};
        """).ReplaceLineEndings("\n");

    minioInit = builder.AddContainer("minio-init", "minio/mc")
        .WithEntrypoint("/bin/sh")
        .WithArgs("-c", minioInitScript)
        .WithEnvironment("MC_USER", minioUser)
        .WithEnvironment("MC_PASS", minioPassword)
        .WaitFor(minio);

    minioApiEndpoint = minio.GetEndpoint("api");
}

// DB migrator: applies pending migrations + seeds the root admin (admin@root.com), then exits; the API waits for its completion so it never starts against an unmigrated DB. Seed password is a dev-only default.
var migrator = builder.AddProject<Projects.DES_Starter_DbMigrator>($"{appPrefix}-db-migrator")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("DatabaseOptions__Provider", "POSTGRESQL")
    .WithEnvironment("DatabaseOptions__ConnectionString", postgres.Resource.ConnectionStringExpression)
    .WithEnvironment("DatabaseOptions__MigrationsAssembly", "DES.Starter.Migrations.PostgreSQL")
    .WithEnvironment("Seed__DefaultAdminPassword", "123Pa$$word!")
    .WithArgs("apply", "--seed");

// Demo seeder (dev-only): provisions the acme/globex tenants + demo-login users via seed-demo. DOTNET_ENVIRONMENT=Development is required (console host ignores ASPNETCORE_ENVIRONMENT) or seed-demo refuses to run.
var demoSeeder = builder.AddProject<Projects.DES_Starter_DbMigrator>($"{appPrefix}-demo-seeder")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitForCompletion(migrator)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("DatabaseOptions__Provider", "POSTGRESQL")
    .WithEnvironment("DatabaseOptions__ConnectionString", postgres.Resource.ConnectionStringExpression)
    .WithEnvironment("DatabaseOptions__MigrationsAssembly", "DES.Starter.Migrations.PostgreSQL")
    .WithEnvironment("Seed__DemoPassword", "Password123!")
    .WithArgs("seed-demo");

// API Service
var api = builder.AddProject<Projects.DES_Starter_Api>($"{appPrefix}-api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WaitForCompletion(migrator)
    .WaitForCompletion(demoSeeder)
    .WithExternalHttpEndpoints()
    .WithEnvironment("DatabaseOptions__Provider", "POSTGRESQL")
    .WithEnvironment("DatabaseOptions__ConnectionString", apiPgConnection)
    .WithEnvironment("DatabaseOptions__MigrationsAssembly", "DES.Starter.Migrations.PostgreSQL")
    // Hangfire dashboard (/jobs) creds — [Required], Password [MinLength(12)], ValidateOnStart; API won't boot without them. Dev-only, mirrors appsettings.Development.json.
    .WithEnvironment("HangfireOptions__UserName", "admin")
    .WithEnvironment("HangfireOptions__Password", "Password123!")
    // SMTP via Ethereal (https://ethereal.email) — fake catch-all inbox for local dev (nothing delivered); mirrors appsettings.Development.json. Safe to commit: throwaway test creds.
    .WithEnvironment("MailOptions__UseSendGrid", "false")
    .WithEnvironment("MailOptions__From", "nicole.lueilwitz0@ethereal.email")
    .WithEnvironment("MailOptions__DisplayName", "Mukesh Murugan")
    .WithEnvironment("MailOptions__Smtp__Host", "smtp.ethereal.email")
    .WithEnvironment("MailOptions__Smtp__Port", "587")
    .WithEnvironment("MailOptions__Smtp__UserName", "nicole.lueilwitz0@ethereal.email")
    .WithEnvironment("MailOptions__Smtp__Password", "x4VJz2r9x2NDss9KpC");

if (redisResource is not null)
{
    api = api
        .WaitFor(redisResource)
        .WithEnvironment("CachingOptions__Redis", redisConnectionString!)
        .WithEnvironment("CachingOptions__EnableSsl", "false");
}

if (minio is not null && minioInit is not null && minioApiEndpoint is not null)
{
    api = api
        .WaitForCompletion(minioInit)
        .WithEnvironment("Storage__Provider", "s3")
        .WithEnvironment("Storage__S3__Bucket", MinioBucket)
        .WithEnvironment("Storage__S3__Region", "us-east-1")
        .WithEnvironment("Storage__S3__ServiceUrl", minioApiEndpoint)
        .WithEnvironment("Storage__S3__AccessKey", minioUser)
        .WithEnvironment("Storage__S3__SecretKey", minioPassword)
        .WithEnvironment("Storage__S3__ForcePathStyle", "true")
        .WithEnvironment("Storage__S3__PublicBaseUrl", ReferenceExpression.Create($"{minioApiEndpoint}/{MinioBucket}"));
}
else if (useExternalS3)
{
    var s3 = infra.GetSection("Storage:S3");
    api = api
        .WithEnvironment("Storage__Provider", "s3")
        .WithEnvironment("Storage__S3__Bucket", s3.GetValue("Bucket", "fsh-uploads") ?? "fsh-uploads")
        .WithEnvironment("Storage__S3__Region", s3.GetValue("Region", "us-east-1") ?? "us-east-1")
        .WithEnvironment("Storage__S3__ServiceUrl", s3["ServiceUrl"] ?? string.Empty)
        .WithEnvironment("Storage__S3__AccessKey", s3["AccessKey"] ?? string.Empty)
        .WithEnvironment("Storage__S3__SecretKey", s3["SecretKey"] ?? string.Empty)
        .WithEnvironment("Storage__S3__ForcePathStyle", s3.GetValue("ForcePathStyle", true).ToString())
        .WithEnvironment("Storage__S3__PublicBaseUrl", s3["PublicBaseUrl"] ?? string.Empty);
}
else
{
    api = api.WithEnvironment("Storage__Provider", "local");
}

//#if (frontend)
// Admin console (React + Vite). Target the API's HTTPS endpoint directly — UseHttpsRedirection's 307 to https is cross-origin and strips the Authorization header.
builder.AddJavaScriptApp($"{appPrefix}-admin", "../../../clients/admin", "dev")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5173, targetPort: 5173, isProxied: false)
    .WithExternalHttpEndpoints()
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("https"));

// Tenant-facing dashboard (React + Vite, with SSE live feed)
builder.AddJavaScriptApp($"{appPrefix}-dashboard", "../../../clients/dashboard", "dev")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 5174, targetPort: 5174, isProxied: false)
    .WithExternalHttpEndpoints()
    .WithEnvironment("VITE_API_BASE_URL", api.GetEndpoint("https"));
//#else
// React apps excluded: discard the unused api handle to keep the no-frontend scaffold warning-clean (S1481 under TreatWarningsAsErrors).
_ = api;
//#endif

await builder.Build().RunAsync();
