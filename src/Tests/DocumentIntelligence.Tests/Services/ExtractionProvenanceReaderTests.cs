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

public sealed class ExtractionProvenanceReaderTests
{
    private const string TenantId = "tenant-a";
    private const string OtherTenantId = "tenant-b";

    #region Happy Path

    [Fact]
    public async Task ReadAsync_Should_ReturnOnlySafeProvenanceInRequestedOrderWithoutTracking_When_AttemptsAreSuccessful()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        Guid ownerUserId = Guid.CreateVersion7();
        (ExtractionAttempt FirstAttempt, CanonicalDocument FirstDocument) = CreateSuccessfulProvenance(
            TenantId,
            ownerUserId,
            "DOC-1",
            "schema-v1");
        (ExtractionAttempt SecondAttempt, CanonicalDocument SecondDocument) = CreateSuccessfulProvenance(
            TenantId,
            ownerUserId,
            "DOC-2",
            "schema-v2");
        dbContext.AddRange(FirstAttempt, FirstDocument, SecondAttempt, SecondDocument);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ExtractionProvenanceReader sut = new(dbContext);

        IReadOnlyList<ExtractionProvenanceDto> result = await sut.ReadAsync(
            ownerUserId,
            [SecondAttempt.Id, FirstAttempt.Id]);

        result.Select(item => item.ExtractionAttemptId).ShouldBe([SecondAttempt.Id, FirstAttempt.Id]);
        result[0].SubmissionDocumentId.ShouldBe(SecondAttempt.SubmissionDocumentId);
        result[0].SourceFileAssetId.ShouldBe(SecondAttempt.SourceFileAssetId);
        result[0].DocumentCode.ShouldBe("DOC-2");
        result[0].SourceSha256.ShouldBe(SecondAttempt.SourceSha256);
        result[0].Provider.ShouldBe(SecondAttempt.Provider);
        result[0].Model.ShouldBe(SecondAttempt.Model);
        result[0].ExtractorVersion.ShouldBe(SecondAttempt.ExtractorVersion);
        result[0].Status.ShouldBe(nameof(ExtractionAttemptStatus.Succeeded));
        result[0].CanonicalDocumentId.ShouldBe(SecondDocument.Id);
        result[0].CanonicalSchemaVersion.ShouldBe("schema-v2");
        result[0].RawResponseArtifactId.ShouldBe(SecondDocument.RawResponseFileAssetId);
        result[0].GeneratedExcelArtifactId.ShouldBe(SecondDocument.GeneratedExcelFileAssetId);
        result[0].CreatedAtUtc.ShouldBe(SecondAttempt.CreatedAtUtc);
        result[0].CompletedAtUtc.ShouldBe(SecondAttempt.CompletedAtUtc!.Value);
        dbContext.ChangeTracker.Entries<ExtractionAttempt>().ShouldBeEmpty();
        dbContext.ChangeTracker.Entries<CanonicalDocument>().ShouldBeEmpty();
    }

    [Fact]
    public void Contract_Should_ExposeOnlyApprovedProvenanceFields()
    {
        string[] propertyNames = typeof(ExtractionProvenanceDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        propertyNames.ShouldBe([
            nameof(ExtractionProvenanceDto.ExtractionAttemptId),
            nameof(ExtractionProvenanceDto.SubmissionDocumentId),
            nameof(ExtractionProvenanceDto.SourceFileAssetId),
            nameof(ExtractionProvenanceDto.DocumentCode),
            nameof(ExtractionProvenanceDto.SourceSha256),
            nameof(ExtractionProvenanceDto.Provider),
            nameof(ExtractionProvenanceDto.Model),
            nameof(ExtractionProvenanceDto.ExtractorVersion),
            nameof(ExtractionProvenanceDto.Status),
            nameof(ExtractionProvenanceDto.CanonicalDocumentId),
            nameof(ExtractionProvenanceDto.CanonicalSchemaVersion),
            nameof(ExtractionProvenanceDto.RawResponseArtifactId),
            nameof(ExtractionProvenanceDto.GeneratedExcelArtifactId),
            nameof(ExtractionProvenanceDto.CreatedAtUtc),
            nameof(ExtractionProvenanceDto.CompletedAtUtc)
        ]);
    }

    #endregion

    #region Exception Cases

    [Fact]
    public async Task ReadAsync_Should_ThrowUnauthorized_When_AmbientTenantIsMissing()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(null, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<UnauthorizedException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowNotFound_When_AttemptBelongsToAnotherTenant()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        ExtractionAttempt otherTenantAttempt;
        await using (DocumentIntelligenceDbContext otherTenantContext = CreateContext(OtherTenantId, databaseRoot))
        {
            (otherTenantAttempt, CanonicalDocument document) = CreateSuccessfulProvenance(
                OtherTenantId,
                Guid.CreateVersion7());
            otherTenantContext.AddRange(otherTenantAttempt, document);
            await otherTenantContext.SaveChangesAsync();
        }
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<NotFoundException>(() => sut.ReadAsync(
            otherTenantAttempt.OwnerUserId,
            [otherTenantAttempt.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowForbidden_When_AttemptOwnerDoesNotMatchExpectedOwner()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        (ExtractionAttempt attempt, CanonicalDocument document) = CreateSuccessfulProvenance(
            TenantId,
            Guid.CreateVersion7());
        dbContext.AddRange(attempt, document);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<ForbiddenException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [attempt.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowNotFound_When_AttemptIsMissing()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<NotFoundException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()]));
    }

    [Fact]
    public async Task ReadAsync_Should_RejectAttempt_When_StatusIsNotSucceeded()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        Guid ownerUserId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(TenantId, ownerUserId);
        dbContext.ExtractionAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<InvalidOperationException>(() => sut.ReadAsync(ownerUserId, [attempt.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowNotFound_When_SucceededAttemptHasNoCanonicalDocument()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        Guid ownerUserId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(TenantId, ownerUserId);
        attempt.Claim();
        attempt.Succeed();
        dbContext.ExtractionAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<NotFoundException>(() => sut.ReadAsync(ownerUserId, [attempt.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_ThrowForbidden_When_CanonicalOwnerDoesNotMatchExpectedOwner()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        Guid ownerUserId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(TenantId, ownerUserId);
        attempt.Claim();
        attempt.Succeed();
        CanonicalDocument document = CanonicalDocument.Create(
            TenantId,
            attempt.Id,
            Guid.CreateVersion7(),
            "schema-v1",
            "{}");
        dbContext.AddRange(attempt, document);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ExtractionProvenanceReader sut = new(dbContext);

        await Should.ThrowAsync<ForbiddenException>(() => sut.ReadAsync(ownerUserId, [attempt.Id]));
    }

    [Fact]
    public async Task ReadAsync_Should_RejectRequest_When_IdentifierCountExceedsBound()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);
        Guid[] ids = Enumerable.Range(0, IExtractionProvenanceReader.MaximumAttemptCount + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => sut.ReadAsync(Guid.CreateVersion7(), ids));
    }

    [Fact]
    public async Task ReadAsync_Should_RejectRequest_When_IdentifiersAreNotUnique()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);
        Guid id = Guid.CreateVersion7();

        await Should.ThrowAsync<ArgumentException>(() => sut.ReadAsync(Guid.CreateVersion7(), [id, id]));
    }

    [Fact]
    public async Task ReadAsync_Should_PropagateCancellation_When_QueryIsCanceled()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        ExtractionProvenanceReader sut = new(dbContext);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => sut.ReadAsync(
            Guid.CreateVersion7(),
            [Guid.CreateVersion7()],
            cancellation.Token));
    }

    #endregion

    private static (ExtractionAttempt Attempt, CanonicalDocument Document) CreateSuccessfulProvenance(
        string tenantId,
        Guid ownerUserId,
        string documentCode = "DOC-1",
        string schemaVersion = "schema-v1")
    {
        ExtractionAttempt attempt = CreateAttempt(tenantId, ownerUserId, documentCode);
        attempt.Claim();
        attempt.Succeed();
        CanonicalDocument document = CanonicalDocument.Create(
            tenantId,
            attempt.Id,
            ownerUserId,
            schemaVersion,
            "{}",
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        return (attempt, document);
    }

    private static ExtractionAttempt CreateAttempt(
        string tenantId,
        Guid ownerUserId,
        string documentCode = "DOC-1") => ExtractionAttempt.Create(
            tenantId,
            ownerUserId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            documentCode,
            new string('a', 64),
            Guid.CreateVersion7().ToString(),
            "provider",
            "model",
            "extractor-v1");

    private static DocumentIntelligenceDbContext CreateContext(
        string? tenantId,
        InMemoryDatabaseRoot databaseRoot)
    {
        DbContextOptions<DocumentIntelligenceDbContext> options =
            new DbContextOptionsBuilder<DocumentIntelligenceDbContext>()
                .UseInMemoryDatabase("extraction-provenance-reader", databaseRoot)
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
