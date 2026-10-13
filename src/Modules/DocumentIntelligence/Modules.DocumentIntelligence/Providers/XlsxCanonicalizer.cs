using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace DES.Modules.DocumentIntelligence.Providers;

internal static class XlsxCanonicalizer
{
    private const int MaxSheets = 100;
    private const int MaxNonEmptyCells = 100_000;
    private const int MaxCellBytes = 32 * 1024;
    private const long MaxUncompressedXmlBytes = 100L * 1024 * 1024;
    private static readonly XNamespace SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipDocumentNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Canonicalize(byte[] workbookBytes, string documentCode)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentCode);
        if (workbookBytes.Length < 4 || workbookBytes[0] != (byte)'P' || workbookBytes[1] != (byte)'K'
            || workbookBytes[2] != 3 || workbookBytes[3] != 4)
        {
            throw new InvalidDataException("The generated workbook is not an XLSX ZIP archive.");
        }

        using MemoryStream stream = new(workbookBytes, writable: false);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: false);
        ValidateArchive(archive);

        XDocument workbook = LoadXml(RequiredEntry(archive, "xl/workbook.xml"));
        XDocument relationships = LoadXml(RequiredEntry(archive, "xl/_rels/workbook.xml.rels"));
        Dictionary<string, string> relationshipTargets = relationships
            .Descendants(PackageRelationshipNamespace + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => NormalizeWorksheetTarget(element.Attribute("Target")!.Value),
                StringComparer.Ordinal);
        List<string> sharedStrings = ReadSharedStrings(archive);
        List<CanonicalSheet> sheets = [];
        int nonEmptyCellCount = 0;

        IEnumerable<XElement> sheetElements = workbook.Descendants(SpreadsheetNamespace + "sheet");
        foreach (XElement sheetElement in sheetElements)
        {
            if (sheets.Count == MaxSheets)
            {
                throw new InvalidDataException($"Workbook exceeds the {MaxSheets} sheet limit.");
            }

            string name = sheetElement.Attribute("name")?.Value
                ?? throw new InvalidDataException("A workbook sheet has no name.");
            string relationshipId = sheetElement.Attribute(RelationshipDocumentNamespace + "id")?.Value
                ?? throw new InvalidDataException("A workbook sheet has no relationship identifier.");
            if (!relationshipTargets.TryGetValue(relationshipId, out string? target))
            {
                throw new InvalidDataException("A workbook sheet relationship is missing.");
            }

            XDocument worksheet = LoadXml(RequiredEntry(archive, target));
            List<CanonicalCell> cells = [];
            foreach (XElement cellElement in worksheet.Descendants(SpreadsheetNamespace + "c"))
            {
                CanonicalCell? cell = ReadCell(cellElement, sharedStrings);
                if (cell is null)
                {
                    continue;
                }

                nonEmptyCellCount++;
                if (nonEmptyCellCount > MaxNonEmptyCells)
                {
                    throw new InvalidDataException($"Workbook exceeds the {MaxNonEmptyCells} non-empty cell limit.");
                }

                if (Encoding.UTF8.GetByteCount(cell.Value) > MaxCellBytes)
                {
                    throw new InvalidDataException($"Cell {cell.Reference} exceeds the {MaxCellBytes} byte string limit.");
                }

                cells.Add(cell);
            }

            sheets.Add(new CanonicalSheet(name, cells));
        }

        CanonicalWorkbook result = new("1.0", documentCode, sheets);
        return JsonSerializer.Serialize(result, SerializerOptions);
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        long totalXmlBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('\\', '/');
            if (normalizedName.StartsWith('/')
                || normalizedName.Split('/').Any(part => part == ".."))
            {
                throw new InvalidDataException("Workbook contains an unsafe ZIP entry path.");
            }

            if (!normalizedName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !normalizedName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            totalXmlBytes = checked(totalXmlBytes + entry.Length);
            if (totalXmlBytes > MaxUncompressedXmlBytes)
            {
                throw new InvalidDataException("Workbook exceeds the uncompressed XML limit.");
            }

            if (entry.Length > 1024 * 1024
                && (entry.CompressedLength == 0 || entry.Length / Math.Max(1, entry.CompressedLength) > 1000))
            {
                throw new InvalidDataException("Workbook contains an unsafe ZIP compression ratio.");
            }
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        XDocument document = LoadXml(entry);
        return document.Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList();
    }

    private static CanonicalCell? ReadCell(XElement cell, List<string> sharedStrings)
    {
        string reference = cell.Attribute("r")?.Value
            ?? throw new InvalidDataException("A worksheet cell has no reference.");
        (int row, int column) = ParseReference(reference);
        string? type = cell.Attribute("t")?.Value;
        string? rawValue = cell.Element(SpreadsheetNamespace + "v")?.Value;
        string value;
        string valueType;

        switch (type)
        {
            case "s":
                if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out int sharedIndex)
                    || sharedIndex < 0 || sharedIndex >= sharedStrings.Count)
                {
                    throw new InvalidDataException($"Cell {reference} has an invalid shared string index.");
                }

                value = sharedStrings[sharedIndex];
                valueType = "string";
                break;
            case "inlineStr":
                value = string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
                valueType = "string";
                break;
            case "b":
                value = rawValue switch
                {
                    "0" => "false",
                    "1" => "true",
                    _ => throw new InvalidDataException($"Cell {reference} has an invalid boolean value.")
                };
                valueType = "boolean";
                break;
            case "str":
                value = rawValue ?? string.Empty;
                valueType = "string";
                break;
            case "e":
                value = rawValue ?? string.Empty;
                valueType = "error";
                break;
            default:
                value = rawValue ?? string.Empty;
                valueType = "number";
                break;
        }

        return value.Length == 0 ? null : new CanonicalCell(reference, row, column, value, valueType);
    }

    private static (int Row, int Column) ParseReference(string reference)
    {
        int index = 0;
        int column = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = checked((column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1));
            index++;
        }

        if (column == 0 || index == reference.Length
            || !int.TryParse(reference.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture, out int row)
            || row < 1)
        {
            throw new InvalidDataException($"Cell reference '{reference}' is invalid.");
        }

        return (row, column);
    }

    private static string NormalizeWorksheetTarget(string target)
    {
        string candidate = target.Replace('\\', '/');
        if (candidate.StartsWith('/'))
        {
            candidate = candidate.TrimStart('/');
        }
        else
        {
            candidate = "xl/" + candidate;
        }

        Stack<string> parts = new();
        foreach (string part in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException("Workbook relationship target escapes the archive root.");
                }

                parts.Pop();
                continue;
            }

            parts.Push(part);
        }

        string normalized = string.Join('/', parts.Reverse());
        if (!normalized.StartsWith("xl/worksheets/", StringComparison.Ordinal)
            || !normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Workbook relationship target is not a worksheet XML file.");
        }

        return normalized;
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string path) =>
        archive.GetEntry(path) ?? throw new InvalidDataException($"Workbook entry '{path}' is missing.");

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxUncompressedXmlBytes
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private sealed record CanonicalWorkbook(string SchemaVersion, string DocumentCode, IReadOnlyList<CanonicalSheet> Sheets);
    private sealed record CanonicalSheet(string Name, IReadOnlyList<CanonicalCell> Cells);
    private sealed record CanonicalCell(string Reference, int Row, int Column, string Value, string ValueType);
}
