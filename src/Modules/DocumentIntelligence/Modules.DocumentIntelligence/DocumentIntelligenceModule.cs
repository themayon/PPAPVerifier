using Asp.Versioning;
using DES.Framework.Eventing;
using DES.Framework.Persistence;
using DES.Framework.Shared.Constants;
using DES.Framework.Web.Modules;
using DES.Modules.DocumentIntelligence.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.GetExtractionAttempt;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.SearchMyExtractionAttempts;
using DES.Modules.DocumentIntelligence.Features.v1.Operations.GetOperatorExtractionAttempt;
using DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;
using DES.Modules.DocumentIntelligence.Processing;
using DES.Modules.DocumentIntelligence.Providers;
using DES.Modules.DocumentIntelligence.Services;
using DES.Modules.Files.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence;

public sealed class DocumentIntelligenceModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PermissionConstants.Register(ExtractionPermissions.All);
        builder.Services.AddHeroDbContext<DocumentIntelligenceDbContext>();
        builder.Services.AddScoped<IDbInitializer, DocumentIntelligenceDbInitializer>();
        builder.Services.AddScoped<IFileAccessPolicy, DocumentExtractionFileAccessPolicy>();
        builder.Services.AddScoped<IDocumentExtractionRequestService, DocumentExtractionRequestService>();
        builder.Services.AddScoped<ICanonicalDocumentReader, CanonicalDocumentReader>();
        builder.Services.AddScoped<IExtractionProvenanceReader, ExtractionProvenanceReader>();
        builder.Services.AddOptions<AspectPdfExtractionOptions>()
            .BindConfiguration(nameof(AspectPdfExtractionOptions))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<AspectPdfExtractionOptions>, AspectPdfExtractionOptionsValidator>();
        builder.Services.AddOptions<DiagramDrawingExtractionOptions>()
            .BindConfiguration(nameof(DiagramDrawingExtractionOptions))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DiagramDrawingExtractionOptions>, DiagramDrawingExtractionOptionsValidator>();
        builder.Services.AddHttpClient<AspectPdfExtractionProvider>((services, client) =>
        {
            AspectPdfExtractionOptions options = services.GetRequiredService<IOptions<AspectPdfExtractionOptions>>().Value;
            client.Timeout = TimeSpan.FromMinutes(options.TimeoutMinutes);
            ConfigureBaseAddress(client, options.BaseUrl);
        });
        builder.Services.AddHttpClient<DiagramDrawingExtractionProvider>((services, client) =>
        {
            DiagramDrawingExtractionOptions options = services.GetRequiredService<IOptions<DiagramDrawingExtractionOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;
            ConfigureBaseAddress(client, options.BaseUrl);
        });
        builder.Services.AddScoped<IDocumentExtractionProvider, DocumentExtractionProviderRouter>();
        builder.Services.AddScoped<ExtractionProcessingJob>();
        builder.Services.AddIntegrationEventHandlers(typeof(DocumentIntelligenceModule).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(DocumentIntelligenceModule).Assembly);
        builder.Services.AddHealthChecks().AddDbContextCheck<DocumentIntelligenceDbContext>(
            name: "db:document-intelligence",
            failureStatus: HealthStatus.Unhealthy);
    }

    private static void ConfigureBaseAddress(HttpClient client, string? configuredUrl)
    {
        if (Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri? baseUri))
        {
            char separator = Path.AltDirectorySeparatorChar;
            client.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd(separator) + separator, UriKind.Absolute);
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var versionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();
        var group = endpoints.MapGroup("api/v{version:apiVersion}/document-intelligence")
            .WithTags("Document Intelligence")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization();
        group.MapGetExtractionAttemptEndpoint();
        group.MapSearchMyExtractionAttemptsEndpoint();
        group.MapGetOperatorExtractionAttemptEndpoint();
        group.MapSearchOperatorExtractionAttemptsEndpoint();
    }
}
