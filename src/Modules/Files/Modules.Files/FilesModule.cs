using Asp.Versioning;
using FluentValidation;
using DES.Framework.Persistence;
using DES.Framework.Shared.Constants;
using DES.Framework.Web.Modules;
using DES.Modules.Files.Authorization;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.Authorization;
using DES.Modules.Files.Data;
using DES.Modules.Files.Features.v1.ChangeVisibility;
using DES.Modules.Files.Features.v1.DeleteFile;
using DES.Modules.Files.Features.v1.FinalizeUpload;
using DES.Modules.Files.Features.v1.GetFileDownloadUrl;
using DES.Modules.Files.Features.v1.GetFileMetadata;
using DES.Modules.Files.Features.v1.ListMyFiles;
using DES.Modules.Files.Features.v1.ListSharedFiles;
using DES.Modules.Files.Features.v1.ListTrashedFiles;
using DES.Modules.Files.Features.v1.RequestUploadUrl;
using DES.Modules.Files.Features.v1.RestoreFile;
using DES.Modules.Files.Jobs;
using DES.Modules.Files.Services;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

[assembly: DesModule(typeof(DES.Modules.Files.FilesModule), 350)]

namespace DES.Modules.Files;

/// <summary>
/// Files module: presigned-URL file lifecycle (upload, finalize, serve, delete) shared across the
/// kit's owning features (Catalog product images, Ticket attachments, My Files, avatars, tenant
/// logos). Module order 350 places it between Auditing (300) and Webhooks (400); owning modules
/// (Catalog=600, Tickets=700) load later and register their <see cref="IFileAccessPolicy"/>
/// implementations during their own ConfigureServices.
/// </summary>
public sealed class FilesModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PermissionConstants.Register(FilesPermissions.All);

        builder.Services.Configure<FilesOptions>(builder.Configuration.GetSection("Files"));
        builder.Services.AddHeroDbContext<FilesDbContext>();
        builder.Services.AddScoped<IDbInitializer, FilesDbInitializer>();

        builder.Services.AddScoped<FileAccessPolicyRegistry>();
        builder.Services.AddSingleton<IFileScanner, NoOpFileScanner>();
        builder.Services.AddValidatorsFromAssembly(typeof(FilesModule).Assembly);

        // Default uploader-only policies for the built-in OwnerTypes. Owning modules register their
        // own policies for additional OwnerTypes via services.AddFileAccessPolicy<TPolicy>().
        builder.Services.AddScoped<IFileAccessPolicy>(_ => new DefaultUploaderOnlyPolicy("MyFiles"));
        builder.Services.AddScoped<IFileAccessPolicy>(_ => new DefaultUploaderOnlyPolicy("User"));

        builder.Services.AddHealthChecks().AddDbContextCheck<FilesDbContext>(
            name: "db:files",
            failureStatus: HealthStatus.Unhealthy);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var versionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("api/v{version:apiVersion}/files")
            .WithTags("Files")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization();

        // Literal routes first so they win over the /{id:guid} catch-all (matches the Catalog
        // pattern for /trash etc.).
        group.MapRequestUploadUrlEndpoint();         // POST  /upload-url
        group.MapListMyFilesEndpoint();              // GET   /mine
        group.MapListSharedFilesEndpoint();          // GET   /shared
        group.MapListTrashedFilesEndpoint();         // GET   /trash
        group.MapRestoreFileEndpoint();              // POST  /{id}/restore  (literal verb path)

        group.MapFinalizeUploadEndpoint();           // POST  /{id}/finalize
        group.MapGetFileDownloadUrlEndpoint();       // GET   /{id}/url
        group.MapChangeFileVisibilityEndpoint();     // PATCH /{id}/visibility
        group.MapGetFileMetadataEndpoint();          // GET   /{id}
        group.MapDeleteFileEndpoint();               // DELETE /{id}

        // Recurring Hangfire jobs (orphan + retention purges). Registration here matches the
        // pattern Billing uses for MonthlyInvoiceJob.
        var jobManager = endpoints.ServiceProvider.GetService<IRecurringJobManager>();
        if (jobManager is not null)
        {
            jobManager.AddOrUpdate<PurgeOrphanedFilesJob>(
                "files-purge-orphans",
                j => j.RunAsync(CancellationToken.None),
                "0 * * * *", // hourly
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

            jobManager.AddOrUpdate<PurgeDeletedFilesJob>(
                "files-purge-deleted",
                j => j.RunAsync(CancellationToken.None),
                "30 3 * * *", // daily 03:30 UTC
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
    }
}
