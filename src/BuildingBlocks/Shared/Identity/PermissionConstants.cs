namespace DES.Framework.Shared.Constants;

/// <summary>
/// Central permission registry. Each module/component contributes its own permissions
/// via <see cref="Register"/> during startup. The registry has no built-ins — every
/// permission is owned by the module it belongs to.
/// </summary>
public static class PermissionConstants
{
    private static readonly List<DesPermission> _all = new();

    public const string RequiredPermissionPolicyName = "RequiredPermission";

    /// <summary>
    /// Registers permissions from a module/component. Duplicates (by Name) are skipped.
    /// </summary>
    public static void Register(IEnumerable<DesPermission> additionalPermissions)
    {
        ArgumentNullException.ThrowIfNull(additionalPermissions);
        _all.AddRange(from permission in additionalPermissions
                      where !_all.Any(p => p.Name == permission.Name)
                      select permission);
    }

    public static IReadOnlyList<DesPermission> All => _all.AsReadOnly();
    public static IReadOnlyList<DesPermission> Root => [.. _all.Where(p => p.IsRoot)];
    public static IReadOnlyList<DesPermission> Admin => [.. _all.Where(p => !p.IsRoot)];
    public static IReadOnlyList<DesPermission> Basic => [.. _all.Where(p => p.IsBasic)];
}

public record DesPermission(string Description, string Action, string Resource, bool IsBasic = false, bool IsRoot = false)
{
    public string Name => NameFor(Action, Resource);
    public static string NameFor(string action, string resource)
    {
        return $"Permissions.{resource}.{action}";
    }
}
