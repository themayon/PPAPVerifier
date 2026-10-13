using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Features;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleTemplateTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;
    private static readonly Guid UserId = Guid.Parse("01991dd6-6e91-76b0-9974-31ec42c95821");
    private static readonly Guid RuleVersionId = Guid.Parse("01991dd6-84ae-7ad7-a6a9-eecbc56e5408");

    [Fact]
    public void Create_Should_NormalizeCodeAndCreateFirstDraft()
    {
        RuleTemplate template = RuleTemplate.Create("tenant", " generic.ppap ", "Generic", " Description ", UserId, Now);

        template.StableCode.ShouldBe("GENERIC.PPAP");
        template.Name.ShouldBe("Generic");
        template.Description.ShouldBe("Description");
        template.Lifecycle.ShouldBe(RuleLifecycle.Draft);
        template.Version.ShouldBe(1);
        template.Versions.Single().VersionNumber.ShouldBe(1);
        template.Versions.Single().Status.ShouldBe(RuleLifecycle.Draft);
    }

    [Fact]
    public void Lifecycle_Should_EnforceExpectedVersionAndPublishedImmutability()
    {
        RuleTemplate template = CreateConfigured();
        RuleTemplateVersion first = template.Versions.Single();

        Should.Throw<Exception>(() => template.SubmitForReview(first.Id, 1, Now));
        template.SubmitForReview(first.Id, 2, Now);
        template.Publish(first.Id, 3, true, UserId, Now);

        template.CurrentPublishedVersionId.ShouldBe(first.Id);
        first.Status.ShouldBe(RuleLifecycle.Published);
        Should.Throw<Exception>(() => template.ConfigureDraft(first.Id, 4, "Changed", null, [], [], Now));
    }

    [Fact]
    public void ConfigureDraft_Should_ReplaceCollectionsAndRejectDuplicatesAtomically()
    {
        RuleTemplate template = CreateConfigured();
        RuleTemplateVersion version = template.Versions.Single();
        Guid originalRequirementId = version.DocumentRequirements.Single().Id;
        Guid originalAssignmentId = version.RuleAssignments.Single().Id;

        var duplicateRequirements = new[]
        {
            new TemplateDocumentRequirementInput("PSW", true, 1),
            new TemplateDocumentRequirementInput("psw", false, 2),
        };
        Should.Throw<Exception>(() => template.ConfigureDraft(version.Id, 2, "Changed", null, duplicateRequirements, [], Now));

        template.Name.ShouldBe("Template");
        version.DocumentRequirements.Single().Id.ShouldBe(originalRequirementId);
        version.RuleAssignments.Single().Id.ShouldBe(originalAssignmentId);

        var duplicateAssignments = new[]
        {
            new TemplateRuleAssignmentInput(RuleVersionId, "PSW", "{}"),
            new TemplateRuleAssignmentInput(RuleVersionId, "psw", "{}"),
        };
        Should.Throw<Exception>(() => template.ConfigureDraft(version.Id, 2, "Changed", null, [], duplicateAssignments, Now));
        version.DocumentRequirements.Single().Id.ShouldBe(originalRequirementId);
    }

    [Fact]
    public void ConfigureDraft_Should_DeriveRequiredAndPreserveDisposition()
    {
        RuleTemplate template = RuleTemplate.Create("tenant", "template", "Template", null, UserId, Now);
        RuleTemplateVersion version = template.Versions.Single();

        template.ConfigureDraft(version.Id, 1, "Template", null,
            [new TemplateDocumentRequirementInput("PSW", true, 1, TemplateDocumentDisposition.Available)], [], Now);

        TemplateDocumentRequirement requirement = version.DocumentRequirements.Single();
        requirement.Disposition.ShouldBe(TemplateDocumentDisposition.Available);
        requirement.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void ClonePublished_Should_CreateSequentialDeepDraftCopy()
    {
        RuleTemplate template = CreateConfigured();
        RuleTemplateVersion published = template.Versions.Single();
        template.SubmitForReview(published.Id, 2, Now);
        template.Publish(published.Id, 3, true, UserId, Now);

        RuleTemplateVersion clone = template.ClonePublished(published.Id, 4, UserId, Now);

        clone.VersionNumber.ShouldBe(2);
        clone.Status.ShouldBe(RuleLifecycle.Draft);
        clone.DocumentRequirements.Single().DocumentCode.ShouldBe("PSW");
        clone.DocumentRequirements.Single().Id.ShouldNotBe(published.DocumentRequirements.Single().Id);
        clone.RuleAssignments.Single().RuleVersionId.ShouldBe(RuleVersionId);
        clone.RuleAssignments.Single().Id.ShouldNotBe(published.RuleAssignments.Single().Id);
        template.CurrentPublishedVersionId.ShouldBe(published.Id);
    }

    [Fact]
    public void ConfigureDraft_Should_AllowSameRuleOnDifferentDocuments()
    {
        RuleTemplate template = RuleTemplate.Create("tenant", "template", "Template", null, UserId, Now);
        RuleTemplateVersion version = template.Versions.Single();
        var assignments = new[]
        {
            new TemplateRuleAssignmentInput(RuleVersionId, "PSW", "{}"),
            new TemplateRuleAssignmentInput(RuleVersionId, "FMEA", "{}"),
        };

        template.ConfigureDraft(version.Id, 1, "Template", null, [], assignments, Now);

        version.RuleAssignments.Count.ShouldBe(2);
    }

    private static RuleTemplate CreateConfigured()
    {
        RuleTemplate template = RuleTemplate.Create("tenant", "template", "Template", null, UserId, Now);
        RuleTemplateVersion version = template.Versions.Single();
        template.ConfigureDraft(
            version.Id,
            1,
            "Template",
            null,
            [new TemplateDocumentRequirementInput("psw", true, 1)],
            [new TemplateRuleAssignmentInput(RuleVersionId, "psw", "{\"tolerance\":1}")],
            Now);
        return template;
    }
}

public sealed class RuleTemplateValidatorTests
{
    [Fact]
    public void ConfigureValidator_Should_AcceptValidDefinition()
    {
        var command = new ConfigureTemplateDraftCommand(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Template", null,
            [new TemplateDocumentRequirementInput("PSW", true, 1)],
            [new TemplateRuleAssignmentInput(Guid.NewGuid(), "PSW", "{\"value\":1}")]);

        new ConfigureTemplateDraftCommandValidator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ConfigureValidator_Should_NormalizeLegacyAndRejectInconsistentDisposition()
    {
        var validLegacy = new ConfigureTemplateDraftCommand(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Template", null,
            [new TemplateDocumentRequirementInput("PSW", true, 1)], []);
        var inconsistent = validLegacy with
        {
            DocumentRequirements = [new TemplateDocumentRequirementInput("PSW", false, 1, TemplateDocumentDisposition.Available)],
        };

        new ConfigureTemplateDraftCommandValidator().Validate(validLegacy).IsValid.ShouldBeTrue();
        new ConfigureTemplateDraftCommandValidator().Validate(inconsistent).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void ConfigureValidator_Should_RejectDuplicateCodesAndAssignmentKeys()
    {
        Guid ruleVersionId = Guid.NewGuid();
        var command = new ConfigureTemplateDraftCommand(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Template", null,
            [new TemplateDocumentRequirementInput("PSW", true, 1), new TemplateDocumentRequirementInput("psw", false, 2)],
            [new TemplateRuleAssignmentInput(ruleVersionId, null, "{}"), new TemplateRuleAssignmentInput(ruleVersionId, null, "{}")]);

        var result = new ConfigureTemplateDraftCommandValidator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.DocumentRequirements));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.RuleAssignments));
    }

    [Fact]
    public void ConfigureValidator_Should_RejectLimitsAndInvalidParameterShape()
    {
        var command = new ConfigureTemplateDraftCommand(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Template", null,
            Enumerable.Range(1, 51).Select(x => new TemplateDocumentRequirementInput($"D{x}", true, x)).ToArray(),
            [new TemplateRuleAssignmentInput(Guid.NewGuid(), null, "[]"), new TemplateRuleAssignmentInput(Guid.NewGuid(), null, $"{{\"value\":\"{new string('x', 16 * 1024)}\"}}"), new TemplateRuleAssignmentInput(Guid.NewGuid(), null, "{\"bad name\":1}")]);

        var result = new ConfigureTemplateDraftCommandValidator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.DocumentRequirements));
        result.Errors.Count(x => x.PropertyName.Contains(nameof(command.RuleAssignments), StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(3);
    }
}
