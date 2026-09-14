using DES.Modules.Multitenancy.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Multitenancy.Contracts.v1.GetTenantStatus;

public sealed record GetTenantStatusQuery(string TenantId) : IQuery<TenantStatusDto>;