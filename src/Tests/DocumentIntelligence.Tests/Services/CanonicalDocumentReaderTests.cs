using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.DocumentIntelligence.Services;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Services;

public sealed class CanonicalDocumentReaderTests
{
    private const string TenantId = "tenant-a";
    private const string OtherTenantId = "tenant-b";

    #region Happy Path

    [Fact]
    public async Task ReadAsync_Should_ReturnCanonicalMetadataInRequestedOrderWithoutTracking_When_DocumentsBelongToOwnerAndTenant()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        Guid ownerUserId = Guid.CreateVersion7();
        CanonicalDocument first = CreateDocument(TenantId, ownerUserId, "schema-v1", "{\"document\":1}");
        CanonicalDocument second = CreateDocument(TenantId, ownerUserId, "schema-v2", "{\"document\":2}");
        dbContext.CanonicalDocuments.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        CanonicalDocumentReader sut = new(dbContext);
        using CancellationTokenSource cancellation = new();

        IReadOnlyList<CanonicalDocumentDto> result = await sut.ReadAsync(
            ownerUserId,
            [second.Id, first.Id],
            cancellation.Token);

        result.Select(document => document.Id).ShouldBe([second.Id, first.Id]);
        result[0].SchemaVersion.ShouldBe("schema-v2");
        result[0].CanonicalJson.ShouldBe("{\"document\":2}");
        dbContext.ChangeTracker.Entries<CanonicalDocument>().ShouldBeEmpty();
    }

    #endregion

    #region Exception Cases

    [Fact]
    public async Task ReadAsync_Should_ThrowNotFound_When_RequestedIdIsMissing()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        CanonicalDocumentReader sut = new(dbContext);

        await Should.ThrowAsync<NotFoundException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowForbidden_When_DocumentOwnerDoesNotMatchExpectedOwner()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        CanonicalDocument document = CreateDocument(TenantId, Guid.CreateVersion7());
        dbContext.CanonicalDocuments.Add(document);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        CanonicalDocumentReader sut = new(dbContext);

        await Should.ThrowAsync<ForbiddenException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [document.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowNotFound_When_DocumentBelongsToAnotherTenant()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        CanonicalDocument otherTenantDocument;
        await using (DocumentIntelligenceDbContext otherTenantContext = CreateContext(OtherTenantId, databaseRoot))
        {
            otherTenantDocument = CreateDocument(OtherTenantId, Guid.CreateVersion7());
            otherTenantContext.CanonicalDocuments.Add(otherTenantDocument);
            await otherTenantContext.SaveChangesAsync();
        }
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        CanonicalDocumentReader sut = new(dbContext);

        await Should.ThrowAsync<NotFoundException>(() => sut.ReadAsync(
            otherTenantDocument.OwnerUserId,
            [otherTenantDocument.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowUnauthorized_When_AmbientTenantIsMissing()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(null, databaseRoot);
        CanonicalDocumentReader sut = new(dbContext);

        await Should.ThrowAsync<UnauthorizedException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()]));
    }

    [Fact]
    public async Task ReadAsync_Should_RejectRequest_When_IdentifierCountExceedsBound()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        CanonicalDocumentReader sut = new(dbContext);
        Guid[] ids = Enumerable.Range(0, ICanonicalDocumentReader.MaximumDocumentCount + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => sut.ReadAsync(Guid.CreateVersion7(), ids));
    }

    [Fact]
    public async Task ReadAsync_Should_PropagateCancellation_When_QueryIsCanceled()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        CanonicalDocumentReader sut = new(dbContext);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()],
            cancellation.Token));
    }

    #endregion

    private static CanonicalDocument CreateDocument(
        string tenantId,
        Guid ownerUserId,
        string schemaVersion = "schema-v1",
        string canonicalJson = "{}") => CanonicalDocument.Create(
            tenantId,
            Guid.CreateVersion7(),
            ownerUserId,
            schemaVersion,
            canonicalJson);

    private static DocumentIntelligenceDbContext CreateContext(
        string? tenantId,
        InMemoryDatabaseRoot databaseRoot)
    {
        DbContextOptions<DocumentIntelligenceDbContext> options =
            new DbContextOptionsBuilder<DocumentIntelligenceDbContext>()
                .UseInMemoryDatabase("canonical-document-reader", databaseRoot)
                .Options;
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return new DocumentIntelligenceDbContext(
            new TestTenantAccessor(tenantId),
            options,
            Options.Create(new DatabaseOptions()),
            environment);
    }

    private sealed class TestTenantAccessor(string? tenantId) : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(tenantId is null ? null : new AppTenantInfo(tenantId, tenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
