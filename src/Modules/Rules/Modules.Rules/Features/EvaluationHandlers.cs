using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;
using Mediator;

namespace DES.Modules.Rules.Features;

public sealed class EvaluateTemplateCommandHandler(
    IRuleEvaluationService evaluationService,
    ICurrentUser currentUser) : ICommandHandler<EvaluateTemplateCommand, Guid>
{
    public async ValueTask<Guid> Handle(EvaluateTemplateCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!currentUser.IsAuthenticated()) throw new UnauthorizedException("An authenticated user is required.");
        Guid userId = currentUser.GetUserId();
        if (userId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        if (command.OwnerUserId != userId) throw new ForbiddenException("The evaluation owner must be the current user.");

        return await evaluationService.EvaluateAsync(
            new RuleEvaluationRequest(
                command.TemplateVersionId,
                command.SubjectId,
                command.OwnerUserId,
                command.InputSchemaVersion,
                command.Documents),
            ct).ConfigureAwait(false);
    }
}

public sealed class GetRuleEvaluationQueryHandler(
    IRuleEvaluationResultReader resultReader,
    ICurrentUser currentUser) : IQueryHandler<GetRuleEvaluationQuery, EvaluationRunDto>
{
    public async ValueTask<EvaluationRunDto> Handle(GetRuleEvaluationQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!currentUser.IsAuthenticated()) throw new UnauthorizedException("An authenticated user is required.");
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");

        RuleEvaluationRunDetails run = await resultReader
            .ReadAsync(query.Id, ownerUserId, cancellationToken: ct)
            .ConfigureAwait(false);
        return new EvaluationRunDto(
            run.Id,
            run.TemplateVersionId,
            run.SubjectId,
            run.OwnerUserId,
            run.InputSchemaVersion,
            run.Status,
            run.CreatedAtUtc,
            run.CompletedAtUtc,
            run.Version,
            run.Results.Select(result => new EvaluationResultDto(
                result.Id,
                result.RuleDefinitionId,
                result.RuleVersionId,
                result.DocumentCode,
                result.Status,
                result.Reason,
                result.TraceJson,
                result.EvaluatedAtUtc)).ToArray());
    }
}
