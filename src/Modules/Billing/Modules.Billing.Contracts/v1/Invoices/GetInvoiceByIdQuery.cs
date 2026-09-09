using DES.Modules.Billing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Invoices;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId) : IQuery<InvoiceDto>;
