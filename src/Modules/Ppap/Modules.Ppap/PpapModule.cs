using Asp.Versioning;
using FluentValidation;
using DES.Framework.Eventing;
using DES.Framework.Persistence;
using DES.Framework.Shared.Constants;
using DES.Framework.Web.Modules;
using DES.Modules.Files.Contracts;
using DES.Modules.Ppap.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Features.v1.Credits.ApproveCreditTopupRequest;
using DES.Modules.Ppap.Features.v1.Credits.CreateCreditTopupRequest;
using DES.Modules.Ppap.Features.v1.Credits.GetMyCreditBalance;
using DES.Modules.Ppap.Features.v1.Credits.RejectCreditTopupRequest;
using DES.Modules.Ppap.Features.v1.Credits.SearchCreditTopupRequests;
using DES.Modules.Ppap.Features.v1.Credits.SearchMyCreditTopupRequests;
using DES.Modules.Ppap.Features.v1.Jobs.GetPpapJob;
using DES.Modules.Ppap.Features.v1.Jobs.SearchPpapJobs;
using DES.Modules.Ppap.Features.v1.Operations.GetPpapOperationsSummary;
using DES.Modules.Ppap.Features.v1.Parts;
using DES.Modules.Ppap.Features.v1.Reports.CreateManualPpapFinding;
using DES.Modules.Ppap.Features.v1.Reports.GetPpapReport;
using DES.Modules.Ppap.Features.v1.Reports.GetPpapReportReview;
using DES.Modules.Ppap.Features.v1.Reports.RevokePpapFindingWaiver;
using DES.Modules.Ppap.Features.v1.Reports.WaivePpapFinding;
using DES.Modules.Ppap.Features.v1.Submissions.AddSubmissionDocument;
using DES.Modules.Ppap.Features.v1.Submissions.CancelDraftSubmission;
using DES.Modules.Ppap.Features.v1.Submissions.CreateDraftSubmission;
using DES.Modules.Ppap.Features.v1.Submissions.GetPpapSubmission;
using DES.Modules.Ppap.Features.v1.Submissions.GetSubmissionDocuments;
using DES.Modules.Ppap.Features.v1.Submissions.MarkSubmissionDocumentNotApplicable;
using DES.Modules.Ppap.Features.v1.Submissions.SearchMyPpapSubmissions;
using DES.Modules.Ppap.Features.v1.Submissions.SubmitDraftSubmission;
using DES.Modules.Ppap.Features.v1.Submissions.UpdateDraftSubmission;
using DES.Modules.Ppap.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap;

public sealed class PpapModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PermissionConstants.Register(PpapPermissions.All);

        builder.Services.AddHeroDbContext<PpapDbContext>();
        builder.Services.AddScoped<IDbInitializer, PpapDbInitializer>();
        builder.Services.AddScoped<IFileAccessPolicy, PpapDocumentFileAccessPolicy>();
        builder.Services.AddOptions<PpapProcessingOptions>()
            .BindConfiguration(nameof(PpapProcessingOptions))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PpapProcessingOptions>, PpapProcessingOptionsValidator>();
        builder.Services.AddScoped<IPpapProcessingProvider, FixturePpapProcessingProvider>();
        builder.Services.AddScoped<IPpapProgressNotifier, SignalRPpapProgressNotifier>();
        builder.Services.AddScoped<PpapProcessingJob>();
        builder.Services.AddScoped<PpapRuleEvaluationJob>();
        builder.Services.AddScoped<PpapReportGenerationJob>();
        builder.Services.AddIntegrationEventHandlers(typeof(PpapModule).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(PpapModule).Assembly);
        builder.Services.AddHealthChecks().AddDbContextCheck<PpapDbContext>(
            name: "db:ppap",
            failureStatus: HealthStatus.Unhealthy);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var versionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints.MapGroup("api/v{version:apiVersion}/ppap")
            .WithTags("PPAP")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization();

        group.MapSearchMyPpapPartsEndpoint();
        group.MapCreatePpapPartEndpoint();
        group.MapGetPpapPartEndpoint();
        group.MapUpdatePpapPartEndpoint();
        group.MapAssignPpapPartTemplateEndpoint();
        group.MapCreateDraftFromPpapPartEndpoint();
        group.MapCreateDraftSubmissionEndpoint();
        group.MapGetPpapSubmissionEndpoint();
        group.MapGetPpapJobEndpoint();
        group.MapGetPpapReportEndpoint();
        group.MapGetPpapReportReviewEndpoint();
        group.MapCreateManualPpapFindingEndpoint();
        group.MapCreatePpapFindingWaiverEndpoint();
        group.MapRevokePpapFindingWaiverEndpoint();
        group.MapSearchMyPpapSubmissionsEndpoint();
        group.MapUpdateDraftSubmissionEndpoint();
        group.MapSubmitDraftSubmissionEndpoint();
        group.MapCancelDraftSubmissionEndpoint();
        group.MapAddSubmissionDocumentEndpoint();
        group.MapMarkSubmissionDocumentNotApplicableEndpoint();
        group.MapGetSubmissionDocumentsEndpoint();
        group.MapGetMyCreditBalanceEndpoint();
        group.MapCreateCreditTopupRequestEndpoint();
        group.MapSearchMyCreditTopupRequestsEndpoint();
        group.MapSearchCreditTopupRequestsEndpoint();
        group.MapApproveCreditTopupRequestEndpoint();
        group.MapRejectCreditTopupRequestEndpoint();
        group.MapGetPpapOperationsSummaryEndpoint();
        group.MapSearchPpapJobsEndpoint();
    }
}
