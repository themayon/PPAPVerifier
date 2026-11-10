using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Features;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class TemplateSetupTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void DocumentSetting_Update_Should_ReplaceRuleMappings_AndBumpVersion()
    {
        DocumentSetting setting = DocumentSetting.Create("acme", "pfmea", "Process FMEA", 7, Now);
        Guid first = Guid.NewGuid(); Guid second = Guid.NewGuid(); Guid third = Guid.NewGuid();
        setting.Update(1, "Process FMEA", true, 7, TemplateDocumentDisposition.Submit, [first, second, second], Now);

        setting.Update(2, "PFMEA", false, 3, TemplateDocumentDisposition.Available, [second, third], Now);

        setting.DocumentCode.ShouldBe("PFMEA");
        setting.Name.ShouldBe("PFMEA");
        setting.IsEnabled.ShouldBeFalse();
        setting.DefaultDisposition.ShouldBe(TemplateDocumentDisposition.Available);
        setting.Rules.Select(x => x.RuleDefinitionId).ShouldBe([second, third], ignoreOrder: true);
        setting.Version.ShouldBe(3);
    }

    [Fact]
    public void DocumentSetting_Update_Should_RejectStaleVersion()
    {
        DocumentSetting setting = DocumentSetting.Create("acme", "CP", "Control Plan", 1, Now);
        Should.Throw<CustomException>(() => setting.Update(9, "Control Plan", true, 1, TemplateDocumentDisposition.Submit, [], Now));
    }

    [Fact]
    public void ConfigureSetup_Should_RejectRulesForUnknownDocuments_AndDuplicates()
    {
        RuleTemplate template = RuleTemplate.CreateForSetup("acme", "TIER-1", "Tier 1", null, Now);
        Guid rule = Guid.NewGuid();
        TemplateSetupDocumentInput[] documents = [new("PFMEA", true, TemplateDocumentDisposition.Submit, 1)];

        Should.Throw<CustomException>(() => template.ConfigureSetup(template.Version, "Tier 1", null, documents, [new("DFMEA", rule, true)], Now));
        Should.Throw<CustomException>(() => template.ConfigureSetup(template.Version, "Tier 1", null, documents, [new("PFMEA", rule, true), new("pfmea", rule, false)], Now));
    }

    [Fact]
    public void PublishSetup_Should_CreatePublishedVersion_AndRetirePrevious()
    {
        RuleTemplate template = RuleTemplate.CreateForSetup("acme", "TIER-1", "Tier 1", null, Now);
        Guid ruleVersion = Guid.NewGuid();
        template.ConfigureSetup(template.Version, "Tier 1", null, [new("PFMEA", true, TemplateDocumentDisposition.Submit, 1), new("DFMEA", false, TemplateDocumentDisposition.Submit, 2)], [], Now);

        RuleTemplateVersion first = template.PublishSetup(
            [new TemplateDocumentRequirementInput("PFMEA", true, 1, TemplateDocumentDisposition.Submit)],
            [new TemplateRuleAssignmentInput(ruleVersion, "PFMEA", "{}")], UserId, Now);
        RuleTemplateVersion second = template.PublishSetup([new TemplateDocumentRequirementInput("PFMEA", false, 1, TemplateDocumentDisposition.Retain)], [], UserId, Now.AddMinutes(1));

        first.VersionNumber.ShouldBe(1);
        first.Status.ShouldBe(RuleLifecycle.Retired);
        second.VersionNumber.ShouldBe(2);
        second.Status.ShouldBe(RuleLifecycle.Published);
        template.CurrentPublishedVersionId.ShouldBe(second.Id);
        template.Lifecycle.ShouldBe(RuleLifecycle.Published);
        template.Documents.Count.ShouldBe(2);
        first.RuleAssignments.Single().DocumentCode.ShouldBe("PFMEA");
    }

    [Fact]
    public void SaveValidator_Should_RejectDuplicateDocuments()
    {
        var validator = new SaveTemplateSetupCommandValidator();
        SaveTemplateSetupCommand command = new(Guid.NewGuid(), 1, "Tier 1", null, [new("PFMEA", true, TemplateDocumentDisposition.Submit, 1), new("pfmea", false, TemplateDocumentDisposition.Submit, 2)], []);

        validator.Validate(command).IsValid.ShouldBeFalse();
        validator.Validate(command with { Documents = [new("PFMEA", true, TemplateDocumentDisposition.Submit, 1)] }).IsValid.ShouldBeTrue();
    }
}
