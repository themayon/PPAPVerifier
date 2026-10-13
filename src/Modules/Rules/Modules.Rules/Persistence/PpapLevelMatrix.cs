using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Persistence;

internal sealed record PpapMatrixDocument(string Code, string Name, TemplateDocumentDisposition Disposition);
internal sealed record PpapLevelDefinition(int Level, string StableCode, string Name, string Description, IReadOnlyList<PpapMatrixDocument> Documents);

internal static class PpapLevelMatrix
{
    private static readonly (string Code, string Name)[] Catalogue =
    [
        ("DWG", "Design Records"),
        ("ECD", "Engineering Change Documents"),
        ("CEA", "Customer Engineering Approval"),
        ("DFMEA", "DFMEA"),
        ("PFD", "Process Flow Diagram"),
        ("PFMEA", "PFMEA"),
        ("CP", "Control Plan"),
        ("MSA", "MSA"),
        ("DIMR", "Dimensional Results"),
        ("MPTR", "Material Test Results"),
        ("IPS", "Initial Process Study"),
        ("QLD", "Qualified Laboratory Documentation"),
        ("AAR", "Appearance Approval Report"),
        ("SAMP", "Sample Production Parts"),
        ("MSAM", "Master Sample"),
        ("CAID", "Checking Aids"),
        ("CSR", "Records of Compliance"),
        ("PSW", "Part Submission Warrant"),
    ];

    private static readonly IReadOnlySet<string> LevelTwoSubmit = new HashSet<string>(StringComparer.Ordinal)
    {
        "DIMR", "MPTR", "IPS", "QLD", "AAR", "SAMP", "CSR", "PSW",
    };

    internal static IReadOnlyList<PpapLevelDefinition> Levels { get; } =
    [
        Create(1, "PSW Only", code => code == "PSW" ? TemplateDocumentDisposition.Submit : TemplateDocumentDisposition.Retain),
        Create(2, "PSW + Limited Data", code => LevelTwoSubmit.Contains(code) ? TemplateDocumentDisposition.Submit : TemplateDocumentDisposition.Retain),
        Create(3, "PSW + Full PPAP", _ => TemplateDocumentDisposition.Submit),
        Create(4, "PSW + Customer Requirements", code => code == "PSW" ? TemplateDocumentDisposition.Submit : TemplateDocumentDisposition.Retain),
        Create(5, "PSW + On-site Review", _ => TemplateDocumentDisposition.Available),
    ];

    private static PpapLevelDefinition Create(int level, string description, Func<string, TemplateDocumentDisposition> disposition) =>
        new(level, $"PPAP-LEVEL-{level}", $"PPAP Level {level}", description, Catalogue.Select(x => new PpapMatrixDocument(x.Code, x.Name, disposition(x.Code))).ToArray());
}
