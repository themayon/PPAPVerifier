namespace DES.Modules.Identity.Contracts.DTOs;

/// <summary>
/// One entry in the host-wide permission catalog returned to the SPA so the role editor can
/// render every permission that exists, not just the ones the local TypeScript file remembered.
/// Mirrors <c>DES.Framework.Shared.Constants.DesPermission</c>; the API surface is the
/// authoritative source — modules contribute via <c>PermissionConstants.Register</c> on
/// startup and the editor reads back through this DTO.
/// </summary>
public sealed record PermissionCatalogEntryDto(
    string Name,
    string Description,
    string Resource,
    string Action,
    bool IsBasic,
    bool IsRoot);
