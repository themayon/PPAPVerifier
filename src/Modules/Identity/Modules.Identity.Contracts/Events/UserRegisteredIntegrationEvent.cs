using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Identity.Contracts.Events;

/// <summary>
/// Integration event raised when a new user is registered.
/// </summary>
public sealed record UserRegisteredIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string? TenantId,
    string CorrelationId,
    string Source,
    string UserId,
    string Email,
    string FirstName,
    string LastName)
    : IIntegrationEvent;