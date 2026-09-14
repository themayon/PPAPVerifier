using FluentValidation;
using DES.Framework.Web.Validation;
using DES.Modules.Multitenancy.Contracts.v1.GetTenants;

namespace DES.Modules.Multitenancy.Features.v1.GetTenants;

public sealed class GetTenantsQueryValidator : AbstractValidator<GetTenantsQuery>
{
    public GetTenantsQueryValidator()
    {
        Include(new PagedQueryValidator<GetTenantsQuery>());
    }
}