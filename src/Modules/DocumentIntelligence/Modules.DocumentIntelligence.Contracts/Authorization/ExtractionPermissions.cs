using DES.Framework.Shared.Constants;

namespace DES.Modules.DocumentIntelligence.Contracts.Authorization;

public static class ExtractionPermissions
{
    public static class Attempts
    {
        public const string Resource = "DocumentIntelligence.Attempts";
        public const string View = $"Permissions.{Resource}.View";
        public const string Manage = $"Permissions.{Resource}.Manage";
    }

    public static IReadOnlyList<DesPermission> All { get; } =
    [
        new("View Document Extraction Attempts", ActionConstants.View, Attempts.Resource, IsBasic: true),
        new("Manage Document Extraction Attempts", "Manage", Attempts.Resource)
    ];
}
