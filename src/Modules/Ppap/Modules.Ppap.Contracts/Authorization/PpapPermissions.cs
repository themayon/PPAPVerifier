using DES.Framework.Shared.Constants;

namespace DES.Modules.Ppap.Contracts.Authorization;

public static class PpapPermissions
{
    public static class Parts
    {
        public const string Resource = "Ppap.Parts";
        public const string View = $"Permissions.{Resource}.View";
        public const string Create = $"Permissions.{Resource}.Create";
        public const string Update = $"Permissions.{Resource}.Update";
        public const string AssignTemplate = $"Permissions.{Resource}.AssignTemplate";
    }

    public static class Submissions
    {
        public const string Resource = "Ppap.Submissions";
        public const string Create = $"Permissions.{Resource}.Create";
        public const string View = $"Permissions.{Resource}.View";
        public const string Update = $"Permissions.{Resource}.Update";
        public const string Submit = $"Permissions.{Resource}.Submit";
        public const string Cancel = $"Permissions.{Resource}.Cancel";
    }

    public static class Credits
    {
        public const string Resource = "Ppap.Credits";
        public const string View = $"Permissions.{Resource}.View";
        public const string RequestTopup = $"Permissions.{Resource}.RequestTopup";
        public const string Manage = $"Permissions.{Resource}.Manage";
    }

    public static class Operations
    {
        public const string Resource = "Ppap.Operations";
        public const string View = $"Permissions.{Resource}.View";
    }

    public static class Jobs
    {
        public const string Resource = "Ppap.Jobs";
        public const string View = $"Permissions.{Resource}.View";
    }

    public static class Findings
    {
        public const string Resource = "Ppap.Findings";
        public const string View = $"Permissions.{Resource}.View";
        public const string CreateManual = $"Permissions.{Resource}.CreateManual";
        public const string ManageWaivers = $"Permissions.{Resource}.ManageWaivers";
    }

    public static IReadOnlyList<DesPermission> All { get; } =
    [
        new("View PPAP Parts", ActionConstants.View, Parts.Resource, IsBasic: true),
        new("Create PPAP Parts", ActionConstants.Create, Parts.Resource),
        new("Update PPAP Parts", ActionConstants.Update, Parts.Resource),
        new("Assign PPAP Part Templates", "AssignTemplate", Parts.Resource),
        new("Create PPAP Submissions", ActionConstants.Create, Submissions.Resource),
        new("View PPAP Submissions", ActionConstants.View, Submissions.Resource, IsBasic: true),
        new("Update PPAP Submissions", ActionConstants.Update, Submissions.Resource),
        new("Submit PPAP Submissions", "Submit", Submissions.Resource),
        new("Cancel PPAP Submissions", "Cancel", Submissions.Resource),
        new("View PPAP Credits", ActionConstants.View, Credits.Resource, IsBasic: true),
        new("Request PPAP Credit Top-ups", "RequestTopup", Credits.Resource, IsBasic: true),
        new("Manage PPAP Credits", "Manage", Credits.Resource),
        new("View PPAP Operations", ActionConstants.View, Operations.Resource),
        new("View PPAP Jobs", ActionConstants.View, Jobs.Resource),
        new("View PPAP Findings", ActionConstants.View, Findings.Resource),
        new("Create Manual PPAP Findings", "CreateManual", Findings.Resource),
        new("Manage PPAP Finding Waivers", "ManageWaivers", Findings.Resource),
    ];
}
