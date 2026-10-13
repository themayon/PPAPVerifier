using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Contracts.Authorization;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Rules.Features;

public sealed record ConfigureTemplateDraftRequest(Guid VersionId, long ExpectedAggregateVersion, string Name, string? Description, IReadOnlyList<TemplateDocumentRequirementInput> DocumentRequirements, IReadOnlyList<TemplateRuleAssignmentInput> RuleAssignments);
public sealed record TemplateVersionTransitionRequest(Guid VersionId, long ExpectedAggregateVersion);
public sealed record PublishTemplateRequest(Guid VersionId, long ExpectedAggregateVersion, bool RetireCurrent = true);

internal static class TemplateEndpoints
{
    internal static void MapTemplates(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/templates", ([AsParameters] SearchTemplatesQuery query, IMediator mediator, CancellationToken ct) => mediator.Send(query, ct)).RequirePermission(RulesPermissions.Templates.View);
        endpoints.MapGet("/templates/{id:guid}", (Guid id, IMediator mediator, CancellationToken ct) => mediator.Send(new GetTemplateQuery(id), ct)).RequirePermission(RulesPermissions.Templates.View);
        endpoints.MapPost("/templates", (CreateTemplateCommand command, IMediator mediator, CancellationToken ct) => mediator.Send(command, ct)).RequirePermission(RulesPermissions.Templates.Create).WithIdempotency();
        endpoints.MapPut("/templates/{id:guid}/draft", (Guid id, ConfigureTemplateDraftRequest request, IMediator mediator, CancellationToken ct) => mediator.Send(new ConfigureTemplateDraftCommand(id, request.VersionId, request.ExpectedAggregateVersion, request.Name, request.Description, request.DocumentRequirements, request.RuleAssignments), ct)).RequirePermission(RulesPermissions.Templates.Edit);
        endpoints.MapPost("/templates/{id:guid}/submit-review", (Guid id, TemplateVersionTransitionRequest request, IMediator mediator, CancellationToken ct) => mediator.Send(new SubmitTemplateForReviewCommand(id, request.VersionId, request.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Templates.Review).WithIdempotency();
        endpoints.MapPost("/templates/{id:guid}/publish", (Guid id, PublishTemplateRequest request, IMediator mediator, CancellationToken ct) => mediator.Send(new PublishTemplateCommand(id, request.VersionId, request.ExpectedAggregateVersion, request.RetireCurrent), ct)).RequirePermission(RulesPermissions.Templates.Publish).WithIdempotency();
        endpoints.MapPost("/templates/{id:guid}/retire", (Guid id, TemplateVersionTransitionRequest request, IMediator mediator, CancellationToken ct) => mediator.Send(new RetireTemplateCommand(id, request.VersionId, request.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Templates.Retire).WithIdempotency();
        endpoints.MapPost("/templates/{id:guid}/clone", (Guid id, TemplateVersionTransitionRequest request, IMediator mediator, CancellationToken ct) => mediator.Send(new ClonePublishedTemplateCommand(id, request.VersionId, request.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Templates.Edit).WithIdempotency();
    }
}
