using DES.Framework.Shared.Constants;

namespace DES.Modules.Rules.Contracts.Authorization;

public static class RulesPermissions
{
    public static class Definitions
    {
        public const string Resource = nameof(Definitions);
        public const string View = $"Permissions.{Resource}.View";
        public const string Create = $"Permissions.{Resource}.Create";
        public const string Edit = $"Permissions.{Resource}.Edit";
        public const string Review = $"Permissions.{Resource}.Review";
        public const string Publish = $"Permissions.{Resource}.Publish";
        public const string Retire = $"Permissions.{Resource}.Retire";
        public const string Delete = $"Permissions.{Resource}.Delete";
        public const string Test = $"Permissions.{Resource}.Test";
    }

    public static class Templates
    {
        public const string Resource = nameof(Templates);
        public const string View = $"Permissions.{Resource}.View";
        public const string Create = $"Permissions.{Resource}.Create";
        public const string Edit = $"Permissions.{Resource}.Edit";
        public const string Review = $"Permissions.{Resource}.Review";
        public const string Publish = $"Permissions.{Resource}.Publish";
        public const string Retire = $"Permissions.{Resource}.Retire";
    }

    public static class Columns
    {
        public const string Resource = "RuleColumns";
        public const string View = $"Permissions.{Resource}.View";
        public const string Create = $"Permissions.{Resource}.Create";
        public const string Edit = $"Permissions.{Resource}.Update";
        public const string Delete = $"Permissions.{Resource}.Delete";
    }

    public static class DocumentSettings
    {
        public const string Resource = "RuleDocumentSettings";
        public const string View = $"Permissions.{Resource}.View";
        public const string Edit = $"Permissions.{Resource}.Update";
    }

    public static IReadOnlyList<DesPermission> All { get; } =
    [
        new("View Document Settings", ActionConstants.View, DocumentSettings.Resource, IsBasic: true),
        new("Edit Document Settings", ActionConstants.Update, DocumentSettings.Resource),
        new("View Rule Columns", ActionConstants.View, Columns.Resource, IsBasic: true),
        new("Create Rule Columns", ActionConstants.Create, Columns.Resource),
        new("Edit Rule Columns", ActionConstants.Update, Columns.Resource),
        new("Delete Rule Columns", ActionConstants.Delete, Columns.Resource),
        new("View Rules", ActionConstants.View, Definitions.Resource, IsBasic: true),
        new("Create Rules", ActionConstants.Create, Definitions.Resource),
        new("Edit Rules", "Edit", Definitions.Resource),
        new("Review Rules", "Review", Definitions.Resource),
        new("Publish Rules", "Publish", Definitions.Resource),
        new("Retire Rules", "Retire", Definitions.Resource),
        new("Delete Rules", ActionConstants.Delete, Definitions.Resource),
        new("Test Rules", "Test", Definitions.Resource),
        new("View Rule Templates", ActionConstants.View, Templates.Resource, IsBasic: true),
        new("Create Rule Templates", ActionConstants.Create, Templates.Resource),
        new("Edit Rule Templates", "Edit", Templates.Resource),
        new("Review Rule Templates", "Review", Templates.Resource),
        new("Publish Rule Templates", "Publish", Templates.Resource),
        new("Retire Rule Templates", "Retire", Templates.Resource),
    ];
}
