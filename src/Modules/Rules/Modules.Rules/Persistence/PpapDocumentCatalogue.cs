namespace DES.Modules.Rules.Persistence;

internal static class PpapDocumentCatalogue
{
    internal static IReadOnlyList<(string Code, string Name)> Documents { get; } =
    [
        ("PSW", "Part Submission Warrant"),
        ("DWG", "Design Records"),
        ("ECD", "Engineering Change Documents"),
        ("CEA", "Customer Engineering Approval"),
        ("DFMEA", "Design FMEA"),
        ("PFD", "Process Flow Diagram"),
        ("PFMEA", "Process FMEA"),
        ("CP", "Control Plan"),
        ("MSA", "Measurement System Analysis"),
        ("DIMR", "Dimensional Results"),
        ("MPTR", "Material / Performance Test Results"),
        ("IPS", "Initial Process Study"),
        ("QLD", "Qualified Laboratory Documentation"),
        ("AAR", "Appearance Approval Report"),
        ("SAMP", "Sample Production Parts"),
        ("MSAM", "Master Sample"),
        ("CAID", "Checking Aids"),
        ("CSR", "Records of Compliance (CSR)"),
        ("IMDS", "IMDS Material Data"),
        ("APDS", "Additional PPAP Data"),
    ];
}
