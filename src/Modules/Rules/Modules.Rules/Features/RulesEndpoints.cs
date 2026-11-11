using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Contracts.Authorization;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Rules.Features;

public sealed record UpdateRuleDraftRequest(Guid VersionId, long ExpectedAggregateVersion, string Name, string? Description, RuleCategory Category, string AstJson, string OutcomeJson);
public sealed record RuleVersionTransitionRequest(Guid VersionId, long ExpectedAggregateVersion);
public sealed record PublishRuleRequest(Guid VersionId, long ExpectedAggregateVersion, bool RetireCurrent = true);

internal static class RulesEndpoints
{
    internal static void MapRules(this IEndpointRouteBuilder e)
    {
        e.MapPost("/definitions/test", (TestRuleCommand c, IMediator m, CancellationToken ct) => m.Send(c, ct)).RequirePermission(RulesPermissions.Definitions.Test);
        e.MapGet("/definitions", ([AsParameters] SearchRulesQuery q, IMediator m, CancellationToken ct) => m.Send(q, ct)).RequirePermission(RulesPermissions.Definitions.View);
        e.MapGet("/definitions/{id:guid}", (Guid id, IMediator m, CancellationToken ct) => m.Send(new GetRuleQuery(id), ct)).RequirePermission(RulesPermissions.Definitions.View);
        e.MapPost("/definitions", (CreateRuleCommand c, IMediator m, CancellationToken ct) => m.Send(c, ct)).RequirePermission(RulesPermissions.Definitions.Create).WithIdempotency();
        e.MapPut("/definitions/{id:guid}/draft", (Guid id, UpdateRuleDraftRequest r, IMediator m, CancellationToken ct) => m.Send(new UpdateRuleDraftCommand(id, r.VersionId, r.ExpectedAggregateVersion, r.Name, r.Description, r.Category, r.AstJson, r.OutcomeJson), ct)).RequirePermission(RulesPermissions.Definitions.Edit);
        e.MapPost("/definitions/{id:guid}/submit-review", (Guid id, RuleVersionTransitionRequest r, IMediator m, CancellationToken ct) => m.Send(new SubmitRuleForReviewCommand(id, r.VersionId, r.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Definitions.Review).WithIdempotency();
        e.MapPost("/definitions/{id:guid}/publish", (Guid id, PublishRuleRequest r, IMediator m, CancellationToken ct) => m.Send(new PublishRuleCommand(id, r.VersionId, r.ExpectedAggregateVersion, r.RetireCurrent), ct)).RequirePermission(RulesPermissions.Definitions.Publish).WithIdempotency();
        e.MapPost("/definitions/{id:guid}/retire", (Guid id, RuleVersionTransitionRequest r, IMediator m, CancellationToken ct) => m.Send(new RetireRuleCommand(id, r.VersionId, r.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Definitions.Retire).WithIdempotency();
        e.MapPost("/definitions/{id:guid}/clone", (Guid id, RuleVersionTransitionRequest r, IMediator m, CancellationToken ct) => m.Send(new ClonePublishedRuleCommand(id, r.VersionId, r.ExpectedAggregateVersion), ct)).RequirePermission(RulesPermissions.Definitions.Edit).WithIdempotency();
        e.MapDelete("/definitions/{id:guid}", (Guid id, long expectedVersion, IMediator m, CancellationToken ct) => m.Send(new DeleteRuleCommand(id, expectedVersion), ct)).RequirePermission(RulesPermissions.Definitions.Delete);
        e.MapPost("/definitions/{id:guid}/restore", (Guid id, long expectedVersion, IMediator m, CancellationToken ct) => m.Send(new RestoreRuleCommand(id, expectedVersion), ct)).RequirePermission(RulesPermissions.Definitions.Delete);
    }
}
