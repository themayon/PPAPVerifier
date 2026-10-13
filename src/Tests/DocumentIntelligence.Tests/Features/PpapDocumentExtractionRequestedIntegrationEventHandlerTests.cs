using DES.Framework.Shared.Multitenancy;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using DES.Modules.DocumentIntelligence.IntegrationEventHandlers;
using DES.Modules.Ppap.Contracts.Events;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Features;

public sealed class PpapDocumentExtractionRequestedIntegrationEventHandlerTests
{
    private const string TenantId = "test";

    #region Happy Path

    [Fact]
    public async Task HandleAsync_Should_DelegateTrustedMetadata_When_TenantMatches()
    {
        TestTenantAccessor tenantAccessor = new(TenantId);
        IDocumentExtractionRequestService requestService = Substitute.For<IDocumentExtractionRequestService>();
        PpapDocumentExtractionRequestedIntegrationEvent @event = CreateEvent(TenantId);
        using CancellationTokenSource cancellation = new();
        PpapDocumentExtractionRequestedIntegrationEventHandler sut = new(tenantAccessor, requestService);

        await sut.HandleAsync(@event, cancellation.Token);

        await requestService.Received(1).RequestAsync(
            @event.OwnerUserId,
            @event.SubmissionDocumentId,
            @event.FileAssetId,
            @event.DocumentCode,
            cancellation.Token);
    }

    #endregion

    #region Tenant Validation

    [Theory]
    [InlineData("other")]
    [InlineData(null)]
    public async Task HandleAsync_Should_Throw_When_AmbientTenantDoesNotExactlyMatchEvent(string? ambientTenantId)
    {
        TestTenantAccessor tenantAccessor = new(ambientTenantId);
        IDocumentExtractionRequestService requestService = Substitute.For<IDocumentExtractionRequestService>();
        PpapDocumentExtractionRequestedIntegrationEvent @event = CreateEvent(TenantId);
        PpapDocumentExtractionRequestedIntegrationEventHandler sut = new(tenantAccessor, requestService);

        await Should.ThrowAsync<InvalidOperationException>(() => sut.HandleAsync(@event));

        await requestService.DidNotReceive().RequestAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    private static PpapDocumentExtractionRequestedIntegrationEvent CreateEvent(string tenantId) => new(
        Guid.CreateVersion7(),
        DateTime.UtcNow,
        tenantId,
        Guid.CreateVersion7().ToString(),
        "Ppap",
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "PSW");

    private sealed class TestTenantAccessor(string? tenantId) : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(tenantId is null ? null : new AppTenantInfo(tenantId, tenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
