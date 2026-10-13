using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Framework.Storage.DTOs;
using DES.Framework.Storage.Services;
using DES.Modules.Files;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using DES.Modules.Files.Data;
using DES.Modules.Files.Domain;
using DES.Modules.Files.Services;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Files.Tests.Services;

public sealed class InternalFileArtifactServiceTests
{
    [Fact]
    public async Task OpenReadAsync_Should_ThrowNotFound_When_AssetIsMissing()
    {
        await using FilesDbContext db = CreateContext();
        IStorageService storage = Substitute.For<IStorageService>();
        IFileAccessPolicy policy = CreatePolicy();
        InternalFileArtifactService service = CreateService(db, storage, policy);

        await Should.ThrowAsync<NotFoundException>(() =>
            service.OpenReadAsync(Guid.CreateVersion7(), Guid.CreateVersion7().ToString()));

        await policy.DidNotReceiveWithAnyArgs().CanReadAsync(default!, default!, default);
    }

    [Fact]
    public async Task OpenReadAsync_Should_ThrowNotFound_When_AssetIsNotAvailable()
    {
        await using FilesDbContext db = CreateContext();
        FileAsset asset = CreatePendingAsset();
        db.FileAssets.Add(asset);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        IFileAccessPolicy policy = CreatePolicy();
        InternalFileArtifactService service = CreateService(db, Substitute.For<IStorageService>(), policy);

        await Should.ThrowAsync<NotFoundException>(() =>
            service.OpenReadAsync(asset.Id, Guid.CreateVersion7().ToString()));

        await policy.DidNotReceiveWithAnyArgs().CanReadAsync(default!, default!, default);
    }

    [Fact]
    public async Task OpenReadAsync_Should_ThrowForbidden_When_PolicyDeniesRead()
    {
        await using FilesDbContext db = CreateContext();
        FileAsset asset = CreateAvailableAsset();
        db.FileAssets.Add(asset);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        IFileAccessPolicy policy = CreatePolicy();
        policy.CanReadAsync(Arg.Any<FileAccessContext>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        InternalFileArtifactService service = CreateService(db, Substitute.For<IStorageService>(), policy);

        await Should.ThrowAsync<ForbiddenException>(() =>
            service.OpenReadAsync(asset.Id, Guid.CreateVersion7().ToString()));
    }

    [Fact]
    public async Task OpenReadAsync_Should_DisposeDownloadedStream_When_ContentIsDisposed()
    {
        await using FilesDbContext db = CreateContext();
        FileAsset asset = CreateAvailableAsset();
        db.FileAssets.Add(asset);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        IFileAccessPolicy policy = CreatePolicy();
        policy.CanReadAsync(Arg.Any<FileAccessContext>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        MemoryStream stream = new([1, 2, 3]);
        IStorageService storage = Substitute.For<IStorageService>();
        storage.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FileDownloadResponse
            {
                Stream = stream,
                ContentType = "application/octet-stream",
                FileName = "stored.xlsx",
                ContentLength = 3
            });
        InternalFileArtifactService service = CreateService(db, storage, policy);

        InternalFileArtifactContent content = await service.OpenReadAsync(asset.Id, Guid.CreateVersion7().ToString());
        await content.DisposeAsync();

        stream.CanRead.ShouldBeFalse();
        content.FileName.ShouldBe("artifact.xlsx");
        content.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.SizeBytes.ShouldBe(3);
    }

    [Fact]
    public async Task CreatePrivateAsync_Should_ThrowForbidden_When_AttachPolicyDenies()
    {
        await using FilesDbContext db = CreateContext();
        IFileAccessPolicy policy = CreatePolicy();
        policy.CanAttachAsync(Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        IStorageService storage = Substitute.For<IStorageService>();
        InternalFileArtifactService service = CreateService(db, storage, policy);
        GeneratedFileArtifactRequest request = new(
            "DocumentExtraction",
            Guid.CreateVersion7(),
            "artifact.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [1, 2, 3]);

        await Should.ThrowAsync<ForbiddenException>(() =>
            service.CreatePrivateAsync(request, Guid.CreateVersion7().ToString()));

        storage.ReceivedCalls().ShouldBeEmpty();
    }

    private static InternalFileArtifactService CreateService(
        FilesDbContext db,
        IStorageService storage,
        IFileAccessPolicy policy)
    {
        IFileScanner scanner = Substitute.For<IFileScanner>();
        FilesOptions settings = new()
        {
            UploadUrlTtlMinutes = 15,
            Categories = new Dictionary<string, FileCategoryOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["GeneratedArtifact"] = new()
                {
                    AllowedExtensions = [".xlsx", ".json"],
                    MaxBytes = 26_214_400
                }
            }
        };
        return new InternalFileArtifactService(
            db,
            storage,
            new FileAccessPolicyRegistry([policy]),
            scanner,
            Options.Create(settings),
            NullLogger<InternalFileArtifactService>.Instance);
    }

    private static IFileAccessPolicy CreatePolicy()
    {
        IFileAccessPolicy policy = Substitute.For<IFileAccessPolicy>();
        policy.OwnerType.Returns("DocumentExtraction");
        return policy;
    }

    private static FileAsset CreatePendingAsset() => FileAsset.CreatePending(
        Guid.CreateVersion7(),
        "DocumentExtraction",
        Guid.CreateVersion7(),
        "artifact.xlsx",
        "artifact.xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        3,
        "private-key",
        Visibility.Private,
        Guid.CreateVersion7().ToString(),
        DateTimeOffset.UtcNow.AddMinutes(15));

    private static FileAsset CreateAvailableAsset()
    {
        FileAsset asset = CreatePendingAsset();
        asset.MarkAvailable(3, ScanStatus.Clean);
        return asset;
    }

    private static FilesDbContext CreateContext()
    {
        DbContextOptions<FilesDbContext> dbOptions = new DbContextOptionsBuilder<FilesDbContext>()
            .UseInMemoryDatabase($"files-{Guid.NewGuid():N}")
            .Options;
        IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor =
            Substitute.For<IMultiTenantContextAccessor<AppTenantInfo>>();
        tenantAccessor.MultiTenantContext.Returns(
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo("test", "test")));
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return new FilesDbContext(
            tenantAccessor,
            dbOptions,
            Options.Create(new DatabaseOptions()),
            environment);
    }
}
