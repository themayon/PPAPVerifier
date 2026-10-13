using DES.Framework.Eventing.Abstractions;
using DES.Modules.DocumentIntelligence.Contracts.Events;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Contracts;

public sealed class DocumentExtractionIntegrationEventTests
{
    [Fact]
    public void Events_Should_ImplementStableIntegrationEventContract()
    {
        Guid eventId = Guid.NewGuid();
        DateTime occurredOnUtc = DateTime.UtcNow;
        Guid attemptId = Guid.NewGuid();
        Guid submissionDocumentId = Guid.NewGuid();
        Guid ownerUserId = Guid.NewGuid();

        DocumentExtractionCompletedIntegrationEvent completed = new(
            eventId, occurredOnUtc, "tenant", "correlation", "DocumentIntelligence",
            attemptId, submissionDocumentId, ownerUserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PSW", "gemini", "model");
        DocumentExtractionFailedIntegrationEvent failed = new(
            eventId, occurredOnUtc, "tenant", "correlation", "DocumentIntelligence",
            attemptId, submissionDocumentId, ownerUserId, "PSW", "gemini", "model",
            "InvalidDocument", "InvalidDocument");

        completed.ShouldBeAssignableTo<IIntegrationEvent>();
        failed.ShouldBeAssignableTo<IIntegrationEvent>();
        completed.Id.ShouldBe(eventId);
        failed.Id.ShouldBe(eventId);
    }
}
