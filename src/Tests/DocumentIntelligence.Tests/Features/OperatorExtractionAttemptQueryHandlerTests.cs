using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Persistence.Context;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.DocumentIntelligence.Features.v1.Operations.GetOperatorExtractionAttempt;
using DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Features;

public sealed class OperatorExtractionAttemptQueryHandlerTests
{
    private const string TenantId = "tenant-a";
    private const string OtherTenantId = "tenant-b";

    #region Happy Path

    [Fact]
    public async Task Search_Should_ReturnAttemptsForAllOwnersWithinTenant_When_OperatorQueries()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        Guid firstOwnerId = Guid.CreateVersion7();
        Guid secondOwnerId = Guid.CreateVersion7();
        ExtractionAttempt first = CreateAttempt(TenantId, firstOwnerId, "PSW", 'a');
        ExtractionAttempt second = CreateAttempt(TenantId, secondOwnerId, "FMEA", 'b');
        ExtractionAttempt otherTenant = CreateAttempt(OtherTenantId, Guid.CreateVersion7(), "PSW", 'c');
        await using (DocumentIntelligenceDbContext seed = CreateContext(TenantId, databaseRoot))
        {
            seed.ExtractionAttempts.AddRange(first, second);
            await seed.SaveChangesAsync();
        }
        await using (DocumentIntelligenceDbContext seed = CreateContext(OtherTenantId, databaseRoot))
        {
            seed.ExtractionAttempts.Add(otherTenant);
            await seed.SaveChangesAsync();
        }
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        SearchOperatorExtractionAttemptsQueryHandler sut = new(dbContext, CreateOperator());

        PagedResponse<OperatorExtractionAttemptDto> result = await sut.Handle(
            new SearchOperatorExtractionAttemptsQuery { PageNumber = 1, PageSize = 20 },
            CancellationToken.None);

        result.TotalCount.ShouldBe(2);
        result.Items.Select(x => x.OwnerUserId).ShouldBe([firstOwnerId, secondOwnerId], ignoreOrder: true);
        result.Items.ShouldNotContain(x => x.Id == otherTenant.Id);
    }

    [Fact]
    public async Task Search_Should_ApplyStatusDocumentOwnerAndSortFilters_When_Provided()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        Guid ownerId = Guid.CreateVersion7();
        ExtractionAttempt deadLettered = CreateAttempt(TenantId, ownerId, "PSW", 'd');
        deadLettered.Claim();
        deadLettered.Fail("ProviderFailed", "The extraction provider failed.");
        deadLettered.DeadLetter(deadLettered.ErrorCode!, deadLettered.ErrorMessage!);
        ExtractionAttempt pending = CreateAttempt(TenantId, ownerId, "FMEA", 'e');
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        dbContext.ExtractionAttempts.AddRange(deadLettered, pending);
        await dbContext.SaveChangesAsync();
        SearchOperatorExtractionAttemptsQueryHandler sut = new(dbContext, CreateOperator());

        PagedResponse<OperatorExtractionAttemptDto> result = await sut.Handle(
            new SearchOperatorExtractionAttemptsQuery
            {
                Status = "DeadLettered",
                DocumentCode = "PSW",
                OwnerUserId = ownerId,
                PageNumber = 1,
                PageSize = 1,
                Sort = "documentCode"
            },
            CancellationToken.None);

        result.TotalCount.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(deadLettered.Id);
        result.Items.Single().Status.ShouldBe("DeadLettered");
    }

    [Fact]
    public async Task Get_Should_ReturnOtherOwnersSanitizedMetadataAndArtifactIds_When_AttemptIsInTenant()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        Guid ownerId = Guid.CreateVersion7();
        Guid rawArtifactId = Guid.CreateVersion7();
        Guid excelArtifactId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(TenantId, ownerId, "PSW", 'f');
        attempt.Claim();
        attempt.Fail("Provider_Failed!!!", "  Safe\r\n operator\tmessage  ");
        attempt.DeadLetter(attempt.ErrorCode!, attempt.ErrorMessage!);
        CanonicalDocument document = CanonicalDocument.Create(
            TenantId,
            attempt.Id,
            ownerId,
            "v1",
            "{}",
            rawArtifactId,
            excelArtifactId);
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        dbContext.ExtractionAttempts.Add(attempt);
        dbContext.CanonicalDocuments.Add(document);
        await dbContext.SaveChangesAsync();
        GetOperatorExtractionAttemptQueryHandler sut = new(dbContext, CreateOperator());

        OperatorExtractionAttemptDto result = await sut.Handle(
            new GetOperatorExtractionAttemptQuery(attempt.Id),
            CancellationToken.None);

        result.OwnerUserId.ShouldBe(ownerId);
        result.ErrorCode.ShouldBe("Provider_Failed");
        result.ErrorMessage.ShouldBe("Safe operator message");
        result.CanonicalDocumentId.ShouldBe(document.Id);
        result.RawResponseFileAssetId.ShouldBe(rawArtifactId);
        result.GeneratedExcelFileAssetId.ShouldBe(excelArtifactId);
    }

    #endregion

    #region Exception

    [Fact]
    public async Task Get_Should_ThrowNotFound_When_AttemptBelongsToAnotherTenant()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        ExtractionAttempt otherTenant = CreateAttempt(OtherTenantId, Guid.CreateVersion7(), "PSW", '1');
        await using (DocumentIntelligenceDbContext seed = CreateContext(OtherTenantId, databaseRoot))
        {
            seed.ExtractionAttempts.Add(otherTenant);
            await seed.SaveChangesAsync();
        }
        await using DocumentIntelligenceDbContext dbContext = CreateContext(TenantId, databaseRoot);
        GetOperatorExtractionAttemptQueryHandler sut = new(dbContext, CreateOperator());

        await Should.ThrowAsync<NotFoundException>(() => sut.Handle(
            new GetOperatorExtractionAttemptQuery(otherTenant.Id),
            CancellationToken.None).AsTask());
    }

    #endregion

    private static ExtractionAttempt CreateAttempt(
        string tenantId,
        Guid ownerUserId,
        string documentCode,
        char hashCharacter) => ExtractionAttempt.Create(
            tenantId,
            ownerUserId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            documentCode,
            new string(hashCharacter, 64),
            Guid.NewGuid().ToString("N"),
            "Aspect",
            "ppap-v1",
            "extractor-v1");

    private static ICurrentUser CreateOperator()
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.GetUserId().Returns(Guid.CreateVersion7());
        return currentUser;
    }

    private static DocumentIntelligenceDbContext CreateContext(
        string? tenantId,
        InMemoryDatabaseRoot databaseRoot)
    {
        DbContextOptions<DocumentIntelligenceDbContext> options =
            new DbContextOptionsBuilder<DocumentIntelligenceDbContext>()
                .UseInMemoryDatabase("operator-extraction-attempts", databaseRoot)
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
