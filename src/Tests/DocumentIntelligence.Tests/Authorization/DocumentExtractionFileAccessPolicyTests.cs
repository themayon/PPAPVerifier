using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Authorization;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.Files.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Authorization;

public sealed class DocumentExtractionFileAccessPolicyTests
{
    [Fact]
    public async Task CanAttachAsync_Should_AllowOwner_When_AttemptIsNonterminal()
    {
        await using DocumentIntelligenceDbContext db = CreateContext();
        Guid ownerId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(ownerId);
        db.ExtractionAttempts.Add(attempt);
        await db.SaveChangesAsync();
        DocumentExtractionFileAccessPolicy policy = new(db);

        bool result = await policy.CanAttachAsync(attempt.Id, ownerId.ToString(), CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanAttachAsync_Should_Deny_When_AttemptIsTerminal()
    {
        await using DocumentIntelligenceDbContext db = CreateContext();
        Guid ownerId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(ownerId);
        attempt.Claim();
        attempt.Succeed();
        db.ExtractionAttempts.Add(attempt);
        await db.SaveChangesAsync();
        DocumentExtractionFileAccessPolicy policy = new(db);

        bool result = await policy.CanAttachAsync(attempt.Id, ownerId.ToString(), CancellationToken.None);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CanReadAsync_Should_AllowOwner_When_AttemptIsTerminal()
    {
        await using DocumentIntelligenceDbContext db = CreateContext();
        Guid ownerId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(ownerId);
        attempt.Claim();
        attempt.Succeed();
        db.ExtractionAttempts.Add(attempt);
        await db.SaveChangesAsync();
        DocumentExtractionFileAccessPolicy policy = new(db);
        FileAccessContext context = new(
            Guid.CreateVersion7(),
            DocumentExtractionFileAccessPolicy.OwnerTypeName,
            attempt.Id,
            ownerId.ToString(),
            1);

        bool result = await policy.CanReadAsync(context, ownerId.ToString(), CancellationToken.None);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task CanDeleteAsync_Should_RequireOwnerUploaderAndNonterminalAttempt()
    {
        await using DocumentIntelligenceDbContext db = CreateContext();
        Guid ownerId = Guid.CreateVersion7();
        ExtractionAttempt attempt = CreateAttempt(ownerId);
        db.ExtractionAttempts.Add(attempt);
        await db.SaveChangesAsync();
        DocumentExtractionFileAccessPolicy policy = new(db);
        FileAccessContext ownerUpload = new(
            Guid.CreateVersion7(),
            DocumentExtractionFileAccessPolicy.OwnerTypeName,
            attempt.Id,
            ownerId.ToString(),
            1);
        FileAccessContext otherUpload = ownerUpload with { CreatedByUserId = Guid.CreateVersion7().ToString() };

        bool ownerResult = await policy.CanDeleteAsync(ownerUpload, ownerId.ToString(), CancellationToken.None);
        bool otherResult = await policy.CanDeleteAsync(otherUpload, ownerId.ToString(), CancellationToken.None);
        bool visibilityResult = await policy.CanChangeVisibilityAsync(ownerUpload, ownerId.ToString(), CancellationToken.None);

        ownerResult.ShouldBeTrue();
        otherResult.ShouldBeFalse();
        visibilityResult.ShouldBeFalse();
    }

    private static ExtractionAttempt CreateAttempt(Guid ownerId) => ExtractionAttempt.Create(
        "test",
        ownerId,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "DOC",
        new string('a', 64),
        Guid.CreateVersion7().ToString(),
        "provider",
        "model",
        "v1");

    private static DocumentIntelligenceDbContext CreateContext()
    {
        DbContextOptions<DocumentIntelligenceDbContext> dbOptions =
            new DbContextOptionsBuilder<DocumentIntelligenceDbContext>()
                .UseInMemoryDatabase($"document-intelligence-{Guid.NewGuid():N}")
                .Options;
        IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor = new TestTenantAccessor();
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return new DocumentIntelligenceDbContext(
            tenantAccessor,
            dbOptions,
            Options.Create(new DatabaseOptions()),
            environment);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo("test", "test"));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
