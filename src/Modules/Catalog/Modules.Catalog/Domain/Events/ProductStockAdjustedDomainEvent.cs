using DES.Framework.Core.Domain;

namespace DES.Modules.Catalog.Domain.Events;

public sealed record ProductStockAdjustedDomainEvent(
    Guid ProductId,
    int OldStock,
    int NewStock,
    int Delta,
    Guid EventId,
    DateTimeOffset OccurredOnUtc) : DomainEvent(EventId, OccurredOnUtc);
