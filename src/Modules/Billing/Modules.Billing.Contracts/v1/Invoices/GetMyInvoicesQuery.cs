using DES.Framework.Shared.Persistence;
using DES.Modules.Billing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Invoices;

public sealed record GetMyInvoicesQuery(
    InvoiceStatus? Status = null,
    int? PeriodYear = null,
    int? PeriodMonth = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResponse<InvoiceDto>>;
