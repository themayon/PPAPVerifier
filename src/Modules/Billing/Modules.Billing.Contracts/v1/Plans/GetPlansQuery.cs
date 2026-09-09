using DES.Modules.Billing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Plans;

public sealed record GetPlansQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<BillingPlanDto>>;
