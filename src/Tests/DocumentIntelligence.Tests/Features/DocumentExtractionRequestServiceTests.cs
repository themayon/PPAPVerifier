using System.Linq.Expressions;
using System.Text;
using DES.Framework.Jobs.Services;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using DES.Modules.DocumentIntelligence.Processing;
using DES.Modules.DocumentIntelligence.Providers;
using DES.Modules.Files.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Features;

public sealed class DocumentExtractionRequestServiceTests
{
    private const string TenantId = "test";
    private static readonly byte[] SourceBytes = Encoding.UTF8.GetBytes("trusted ppap document");

    #region Happy Path

    [Fact]
    public async Task RequestAsync_Should_CreateAttemptAndEnqueueProcessing_When_RequestIsNew()
    {
        await using DocumentIntelligenceDbContext dbContext = CreateContext();
        IInternalFileArtifactService artifacts = CreateArtifacts();
        IJobService jobService = Substitute.For<IJobService>();
        DocumentExtractionRequestService sut = CreateService(dbContext, artifacts, jobService);
        Guid ownerUserId = Guid.CreateVersion7();
        Guid submissionDocumentId = Guid.CreateVersion7();
        Guid sourceFileAssetId = Guid.CreateVersion7();
        using CancellationTokenSource cancellation = new();

        Guid result = await sut.RequestAsync(
            ownerUserId,
            submissionDocumentId,
            sourceFileAssetId,
            "PSW",
            cancellation.Token);

        var attempt = await dbContext.ExtractionAttempts.SingleAsync(cancellation.Token);
        result.ShouldBe(attempt.Id);
        attempt.TenantId.ShouldBe(TenantId);
        attempt.OwnerUserId.ShouldBe(ownerUserId);
        attempt.SubmissionDocumentId.ShouldBe(submissionDocumentId);
        attempt.SourceFileAssetId.ShouldBe(sourceFileAssetId);
        attempt.DocumentCode.ShouldBe("PSW");
        attempt.SourceSha256.ShouldNotBeNullOrWhiteSpace();
        await artifacts.Received(1).OpenReadAsync(sourceFileAssetId, ownerUserId.ToString(), cancellation.Token);
        jobService.Received(1).Enqueue<ExtractionProcessingJob>(
            Arg.Any<Expression<Func<ExtractionProcessingJob, Task>>>());
    }

    [Fact]
    public async Task RequestAsync_Should_UseDrawingSettings_When_DocumentCodeIsDwg()
    {
        await using DocumentIntelligenceDbContext dbContext = CreateContext();
        DiagramDrawingExtractionOptions drawingOptions = new()
        {
            Enabled = true,
            Provider = "drawing-provider",
            Model = "drawing-model",
            ExtractorVersion = "drawing-v2"
        };
        DocumentExtractionRequestService sut = CreateService(
            dbContext,
            CreateArtifacts(),
            Substitute.For<IJobService>(),
            drawingOptions);

        await sut.RequestAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "dwg",
            CancellationToken.None);

        var attempt = await dbContext.ExtractionAttempts.SingleAsync();
        attempt.Provider.ShouldBe("drawing-provider");
        attempt.Model.ShouldBe("drawing-model");
        attempt.ExtractorVersion.ShouldBe("drawing-v2");
    }

    [Fact]
    public async Task RequestAsync_Should_ReturnExistingAttemptAndReenqueue_When_PendingRequestIsDuplicate()
    {
        await using DocumentIntelligenceDbContext dbContext = CreateContext();
        IInternalFileArtifactService artifacts = CreateArtifacts();
        IJobService jobService = Substitute.For<IJobService>();
        DocumentExtractionRequestService sut = CreateService(dbContext, artifacts, jobService);
        Guid ownerUserId = Guid.CreateVersion7();
        Guid submissionDocumentId = Guid.CreateVersion7();
        Guid sourceFileAssetId = Guid.CreateVersion7();

        Guid first = await sut.RequestAsync(
            ownerUserId,
            submissionDocumentId,
            sourceFileAssetId,
            "PSW",
            CancellationToken.None);
        Guid duplicate = await sut.RequestAsync(
            ownerUserId,
            submissionDocumentId,
            sourceFileAssetId,
            "PSW",
            CancellationToken.None);

        duplicate.ShouldBe(first);
        (await dbContext.ExtractionAttempts.CountAsync()).ShouldBe(1);
        jobService.Received(2).Enqueue<ExtractionProcessingJob>(
            Arg.Any<Expression<Func<ExtractionProcessingJob, Task>>>());
    }

    #endregion

    private static DocumentExtractionRequestService CreateService(
        DocumentIntelligenceDbContext dbContext,
        IInternalFileArtifactService artifacts,
        IJobService jobService,
        DiagramDrawingExtractionOptions? drawingOptions = null) => new(
            dbContext,
            artifacts,
            jobService,
            Options.Create(new AspectPdfExtractionOptions
            {
                Enabled = true,
                Provider = "gemini",
                Model = "gemini-test",
                ExtractorVersion = "aspect-v1"
            }),
            Options.Create(drawingOptions ?? new DiagramDrawingExtractionOptions()));

    private static IInternalFileArtifactService CreateArtifacts()
    {
        IInternalFileArtifactService artifacts = Substitute.For<IInternalFileArtifactService>();
        artifacts.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new InternalFileArtifactContent(
                new MemoryStream(SourceBytes, writable: false),
                "application/pdf",
                "source.pdf",
                SourceBytes.Length));
        return artifacts;
    }

    private static DocumentIntelligenceDbContext CreateContext()
    {
        DbContextOptions<DocumentIntelligenceDbContext> options =
            new DbContextOptionsBuilder<DocumentIntelligenceDbContext>()
                .UseInMemoryDatabase($"document-intelligence-{Guid.NewGuid():N}")
                .Options;
        IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor = new TestTenantAccessor();
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return new DocumentIntelligenceDbContext(
            tenantAccessor,
            options,
            Options.Create(new DatabaseOptions()),
            environment);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
