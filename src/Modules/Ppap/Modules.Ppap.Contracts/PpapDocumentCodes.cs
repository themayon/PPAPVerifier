namespace DES.Modules.Ppap.Contracts;

public static class PpapDocumentCodes
{
    public const string Dwg = "DWG";
    public const string Ecd = "ECD";
    public const string Cea = "CEA";
    public const string Dfmea = "DFMEA";
    public const string Pfd = "PFD";
    public const string Pfmea = "PFMEA";
    public const string Cp = "CP";
    public const string Msa = "MSA";
    public const string Dimr = "DIMR";
    public const string Imds = "IMDS";
    public const string Mptr = "MPTR";
    public const string Ips = "IPS";
    public const string Qld = "QLD";
    public const string Aar = "AAR";
    public const string Samp = "SAMP";
    public const string Msam = "MSAM";
    public const string Caid = "CAID";
    public const string Apds = "APDS";
    public const string Csr = "CSR";
    public const string Psw = "PSW";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Dwg, Ecd, Cea, Dfmea, Pfd, Pfmea, Cp, Msa, Dimr, Imds,
        Mptr, Ips, Qld, Aar, Samp, Msam, Caid, Apds, Csr, Psw
    };
}
