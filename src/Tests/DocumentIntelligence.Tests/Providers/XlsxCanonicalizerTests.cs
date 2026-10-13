using System.Text.Json;
using DES.Modules.DocumentIntelligence.Providers;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Providers;

public sealed class XlsxCanonicalizerTests
{
    [Fact]
    public void Canonicalize_Should_ReadSharedInlineNumericAndBooleanCells()
    {
        string cells = "<c r=\"A1\" t=\"s\"><v>0</v></c>"
            + "<c r=\"B1\" t=\"inlineStr\"><is><t>Inline</t></is></c>"
            + "<c r=\"C1\"><v>12.5</v></c>"
            + "<c r=\"D1\" t=\"b\"><v>1</v></c>";
        string shared = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><r><t>Shared</t></r><r><t> Value</t></r></si></sst>";
        byte[] workbook = AspectPdfExtractionProviderTests.CreateWorkbook(cells, shared);

        string canonical = XlsxCanonicalizer.Canonicalize(workbook, "PPAP-4");

        using JsonDocument document = JsonDocument.Parse(canonical);
        JsonElement root = document.RootElement;
        root.GetProperty("schemaVersion").GetString().ShouldBe("1.0");
        root.GetProperty("documentCode").GetString().ShouldBe("PPAP-4");
        JsonElement cellsElement = root.GetProperty("sheets")[0].GetProperty("cells");
        cellsElement.GetArrayLength().ShouldBe(4);
        cellsElement[0].GetProperty("value").GetString().ShouldBe("Shared Value");
        cellsElement[1].GetProperty("value").GetString().ShouldBe("Inline");
        cellsElement[2].GetProperty("valueType").GetString().ShouldBe("number");
        cellsElement[3].GetProperty("value").GetString().ShouldBe("true");
        cellsElement[3].GetProperty("column").GetInt32().ShouldBe(4);
    }

    [Fact]
    public void Canonicalize_Should_RejectCellStringOverLimit()
    {
        string cells = $"<c r=\"A1\" t=\"inlineStr\"><is><t>{new string('x', 32 * 1024 + 1)}</t></is></c>";
        byte[] workbook = AspectPdfExtractionProviderTests.CreateWorkbook(cells);

        Should.Throw<InvalidDataException>(() => XlsxCanonicalizer.Canonicalize(workbook, "PPAP-4"));
    }

    [Fact]
    public void Canonicalize_Should_RejectNonZipContent()
    {
        Should.Throw<InvalidDataException>(() => XlsxCanonicalizer.Canonicalize([1, 2, 3], "PPAP-4"));
    }
}
