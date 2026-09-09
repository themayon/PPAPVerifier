using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Invoices;

public sealed record MarkInvoicePaidCommand(Guid InvoiceId) : ICommand<Guid>;
