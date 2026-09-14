using FluentValidation;
using DES.Modules.Multitenancy.Contracts.v1.ChangeTenantActivation;

namespace DES.Modules.Multitenancy.Features.v1.ChangeTenantActivation;

internal sealed class ChangeTenantActivationCommandValidator : AbstractValidator<ChangeTenantActivationCommand>
{
    public ChangeTenantActivationCommandValidator() =>
       RuleFor(t => t.TenantId)
           .NotEmpty();
}