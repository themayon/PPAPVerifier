namespace DES.Modules.Ppap.Domain;

public static class PpapRegions
{
    public const string Europe = "Europe";
    public const string NorthAmerica = "North America";
    public const string LatinAmerica = "Latin America";
    public const string AsiaPacific = "Asia Pacific";
    public const string MiddleEastAfrica = "Middle East & Africa";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = [Europe, NorthAmerica, LatinAmerica, AsiaPacific, MiddleEastAfrica, Other];

    private static readonly Dictionary<string, string> Countries = Build(
        (Europe, ["Germany", "DE", "Deutschland", "France", "FR", "Italy", "IT", "Spain", "ES", "Portugal", "PT", "United Kingdom", "UK", "GB", "Great Britain", "England", "Scotland", "Wales", "Ireland", "IE",
            "Netherlands", "NL", "Holland", "Belgium", "BE", "Luxembourg", "LU", "Switzerland", "CH", "Austria", "AT", "Sweden", "SE", "Norway", "NO", "Denmark", "DK", "Finland", "FI", "Iceland", "IS",
            "Poland", "PL", "Czech Republic", "Czechia", "CZ", "Slovakia", "SK", "Hungary", "HU", "Romania", "RO", "Bulgaria", "BG", "Slovenia", "SI", "Croatia", "HR", "Serbia", "RS", "Greece", "GR",
            "Estonia", "EE", "Latvia", "LV", "Lithuania", "LT", "Ukraine", "UA", "Bosnia and Herzegovina", "North Macedonia", "Albania", "Moldova", "Belarus", "Russia", "RU"]),
        (NorthAmerica, ["United States", "United States of America", "USA", "US", "America", "Canada", "CA"]),
        (LatinAmerica, ["Mexico", "MX", "Brazil", "BR", "Argentina", "AR", "Chile", "CL", "Colombia", "CO", "Peru", "PE", "Uruguay", "UY", "Paraguay", "Venezuela", "Ecuador", "Bolivia", "Costa Rica", "Guatemala",
            "Honduras", "El Salvador", "Panama", "Dominican Republic"]),
        (AsiaPacific, ["China", "CN", "PRC", "Japan", "JP", "South Korea", "Korea", "KR", "Taiwan", "TW", "India", "IN", "Thailand", "TH", "Vietnam", "Viet Nam", "VN", "Malaysia", "MY", "Indonesia", "ID",
            "Philippines", "PH", "Singapore", "SG", "Australia", "AU", "New Zealand", "NZ", "Hong Kong", "HK", "Bangladesh", "Pakistan", "Sri Lanka", "Cambodia", "Myanmar"]),
        (MiddleEastAfrica, ["Turkey", "Türkiye", "TR", "Israel", "IL", "United Arab Emirates", "UAE", "AE", "Saudi Arabia", "SA", "Qatar", "Kuwait", "Oman", "Bahrain", "Jordan", "Iran", "Iraq", "Egypt", "EG",
            "Morocco", "MA", "Tunisia", "Algeria", "South Africa", "ZA", "Nigeria", "Kenya", "Ethiopia", "Ghana"]));

    public static string? FromCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        return Countries.TryGetValue(country.Trim().TrimEnd('.'), out string? region) ? region : Other;
    }

    private static Dictionary<string, string> Build(params (string Region, string[] Names)[] groups)
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string region, string[] names) in groups)
        {
            foreach (string name in names) map[name] = region;
        }
        return map;
    }
}
