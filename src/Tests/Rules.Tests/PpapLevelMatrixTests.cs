using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Persistence;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class PpapLevelMatrixTests
{
    private static readonly string[] ExpectedCodes =
    [
        "DWG", "ECD", "CEA", "DFMEA", "PFD", "PFMEA", "CP", "MSA", "DIMR",
        "MPTR", "IPS", "QLD", "AAR", "SAMP", "MSAM", "CAID", "CSR", "PSW",
    ];

    [Fact]
    public void Levels_Should_DefineAuthoritativeOrderedCatalogue()
    {
        PpapLevelMatrix.Levels.Select(x => x.Level).ShouldBe([1, 2, 3, 4, 5]);
        PpapLevelMatrix.Levels.Select(x => x.StableCode).ShouldBe(["PPAP-LEVEL-1", "PPAP-LEVEL-2", "PPAP-LEVEL-3", "PPAP-LEVEL-4", "PPAP-LEVEL-5"]);
        PpapLevelMatrix.Levels.Select(x => x.Name).ShouldBe(["PPAP Level 1", "PPAP Level 2", "PPAP Level 3", "PPAP Level 4", "PPAP Level 5"]);
        PpapLevelMatrix.Levels.Select(x => x.Description).ShouldBe(["PSW Only", "PSW + Limited Data", "PSW + Full PPAP", "PSW + Customer Requirements", "PSW + On-site Review"]);
        foreach (PpapLevelDefinition level in PpapLevelMatrix.Levels)
        {
            level.Documents.Count.ShouldBe(18);
            level.Documents.Select(x => x.Code).ShouldBe(ExpectedCodes);
            level.Documents.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count().ShouldBe(18);
            level.Documents.Select(x => x.Code).ShouldNotContain("IMDS");
            level.Documents.Select(x => x.Code).ShouldNotContain("APDS");
        }
    }

    [Fact]
    public void Levels_Should_MatchAuthoritativeDispositions()
    {
        PpapLevelDefinition level1 = PpapLevelMatrix.Levels[0];
        level1.Documents.Count(x => x.Disposition == TemplateDocumentDisposition.Submit).ShouldBe(1);
        level1.Documents.Single(x => x.Disposition == TemplateDocumentDisposition.Submit).Code.ShouldBe("PSW");

        PpapLevelDefinition level2 = PpapLevelMatrix.Levels[1];
        level2.Documents.Where(x => x.Disposition == TemplateDocumentDisposition.Submit).Select(x => x.Code)
            .ShouldBe(["DIMR", "MPTR", "IPS", "QLD", "AAR", "SAMP", "CSR", "PSW"]);
        level2.Documents.Count(x => x.Disposition == TemplateDocumentDisposition.Retain).ShouldBe(10);

        PpapLevelMatrix.Levels[2].Documents.ShouldAllBe(x => x.Disposition == TemplateDocumentDisposition.Submit);
        PpapLevelMatrix.Levels[3].Documents.Select(x => x.Disposition).ShouldBe(level1.Documents.Select(x => x.Disposition));
        PpapLevelMatrix.Levels[4].Documents.ShouldAllBe(x => x.Disposition == TemplateDocumentDisposition.Available);
    }
}
