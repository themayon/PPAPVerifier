using DES.Modules.Multitenancy.Contracts;
using DES.Modules.Multitenancy.Contracts.v1.AdjustTenantValidity;
using Mediator;

namespace DES.Modules.Multitenancy.Features.v1.AdjustTenantValidity;

public sealed class AdjustTenantValidityCommandHandler(ITenantService tenantService)
    : ICommandHandler<AdjustTenantValidityCommand, AdjustTenantValidityCommandResponse>
{
    public async ValueTask<AdjustTenantValidityCommandResponse> Handle(
        AdjustTenantValidityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validUpto = await tenantService
            .AdjustValidityAsync(command.TenantId, command.ValidUpto, cancellationToken)
            .ConfigureAwait(false);

        return new AdjustTenantValidityCommandResponse(command.TenantId, validUpto);
    }
}
