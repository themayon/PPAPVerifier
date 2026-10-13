using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapReportTests
{
    private const string ValidManifest = "{\"schemaVersion\":\"1.0\"}";

    [Fact]
    public void Create_Should_InitializeImmutableOutcomeCounts()
    {
        PpapReport report = PpapReport.Create(
            " tenant-1 ", Guid.NewGuid(), Guid.NewGuid(), 4, Guid.NewGuid(), 8, 2, 1, 6, 3, 2, 1, ValidManifest);

        report.Id.ShouldNotBe(Guid.Empty);
        report.TenantId.ShouldBe("tenant-1");
        report.SubmissionVersion.ShouldBe(4);
        report.AvailableDocumentCount.ShouldBe(8);
        report.NotApplicableDocumentCount.ShouldBe(2);
        report.QuarantinedDocumentCount.ShouldBe(1);
        report.FindingCount.ShouldBe(6);
        report.FailedFindingCount.ShouldBe(3);
        report.NotEvaluatedFindingCount.ShouldBe(2);
        report.ErrorFindingCount.ShouldBe(1);
        report.GeneratedAtUtc.ShouldNotBe(default);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Create_Should_RejectNegativeCounts(int available, int notApplicable, int quarantined, int findings)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PpapReport.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
            available, notApplicable, quarantined, findings, 0, 0, 0, ValidManifest));
    }

    [Fact]
    public void Create_Should_RejectOutcomeCountsThatDoNotEqualTotal()
    {
        Should.Throw<ArgumentException>(() => PpapReport.Create(
            "tenant-1", Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 1, 0, 0, 2, 1, 0, 0, ValidManifest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":\"2.0\"}")]
    public void Create_Should_RejectInvalidManifestObjectOrSchema(string manifestJson)
    {
        Should.Throw<ArgumentException>(() => CreateReport(manifestJson));
    }

    [Fact]
    public void Create_Should_RejectManifestLargerThan256KiB()
    {
        string manifestJson = $"{{\"schemaVersion\":\"1.0\",\"value\":\"{new string('x', PpapReport.ReproducibilityManifestMaxBytes)}\"}}";

        Should.Throw<ArgumentOutOfRangeException>(() => CreateReport(manifestJson));
    }

    [Fact]
    public void Create_Should_StoreManifestWithNoPublicMutationPath()
    {
        PpapReport report = CreateReport(ValidManifest);

        report.ReproducibilityManifestJson.ShouldBe(ValidManifest);
        typeof(PpapReport).GetProperty(nameof(PpapReport.ReproducibilityManifestJson))!
            .SetMethod!.IsPrivate.ShouldBeTrue();
    }

    private static PpapReport CreateReport(string manifestJson) => PpapReport.Create(
        "tenant-1", Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 1, 0, 0, 0, 0, 0, 0, manifestJson);
}
