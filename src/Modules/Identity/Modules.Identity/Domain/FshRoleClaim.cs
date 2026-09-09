using Microsoft.AspNetCore.Identity;

namespace DES.Modules.Identity.Domain;

public class DesRoleClaim : IdentityRoleClaim<string>
{
    public string? CreatedBy { get; init; }
    public DateTimeOffset CreatedOn { get; init; }
}