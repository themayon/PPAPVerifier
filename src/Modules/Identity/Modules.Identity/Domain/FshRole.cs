using Microsoft.AspNetCore.Identity;

namespace DES.Modules.Identity.Domain;

public class DesRole : IdentityRole
{
    public string? Description { get; set; }

    public DesRole(string name, string? description = null)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Description = description;
        NormalizedName = name.ToUpperInvariant();
    }
}