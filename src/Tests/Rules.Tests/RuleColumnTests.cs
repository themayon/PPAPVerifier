using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Features;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleColumnTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_TrimValues_AndNormalizeHeaderNames()
    {
        RuleColumn column = RuleColumn.Create("acme", "  Completion Date ", " completionDate ", RuleColumnKind.Column, RuleColumnDataType.Date, "Completion Date,\n completed date , Completion Date", null, Now);

        column.Name.ShouldBe("Completion Date");
        column.Key.ShouldBe("completionDate");
        column.HeaderNames.ShouldBe("Completion Date, completed date");
        column.IsActive.ShouldBeTrue();
        column.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData("completionDate", true)]
    [InlineData("header.partNumber", true)]
    [InlineData("rpn_2", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("a..b", false)]
    [InlineData("items[0]", false)]
    [InlineData("$root", false)]
    public void IsValidKey_Should_AcceptDotSeparatedSafeSegments(string key, bool valid)
    {
        RuleColumn.IsValidKey(key).ShouldBe(valid);
    }

    [Fact]
    public void Update_Should_ApplyChanges_AndBumpVersion()
    {
        RuleColumn column = RuleColumn.Create("acme", "Severity", "severity", RuleColumnKind.Column, RuleColumnDataType.Number, null, null, Now);

        column.Update(1, "Severity (S)", "severity", RuleColumnDataType.Number, "SEV, S", "1-10 scale", false, Now.AddMinutes(1));

        column.Name.ShouldBe("Severity (S)");
        column.IsActive.ShouldBeFalse();
        column.Description.ShouldBe("1-10 scale");
        column.Version.ShouldBe(2);
        column.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Update_Should_RejectStaleVersion()
    {
        RuleColumn column = RuleColumn.Create("acme", "Severity", "severity", RuleColumnKind.Column, RuleColumnDataType.Number, null, null, Now);

        Should.Throw<CustomException>(() => column.Update(5, "Severity", "severity", RuleColumnDataType.Number, null, null, true, Now));
    }

    [Fact]
    public void CreateValidator_Should_RejectInvalidKey()
    {
        var validator = new CreateRuleColumnCommandValidator();

        validator.Validate(new CreateRuleColumnCommand("Target Date", "targetDate", RuleColumnKind.Column, RuleColumnDataType.Date, null, null)).IsValid.ShouldBeTrue();
        validator.Validate(new CreateRuleColumnCommand("Target Date", "target date", RuleColumnKind.Column, RuleColumnDataType.Date, null, null)).IsValid.ShouldBeFalse();
        validator.Validate(new CreateRuleColumnCommand("", "targetDate", RuleColumnKind.Column, RuleColumnDataType.Date, null, null)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void SearchValidator_Should_CapPageSizeAt500()
    {
        var validator = new SearchRuleColumnsQueryValidator();

        validator.Validate(new SearchRuleColumnsQuery { PageSize = 500 }).IsValid.ShouldBeTrue();
        validator.Validate(new SearchRuleColumnsQuery { PageSize = 501 }).IsValid.ShouldBeFalse();
    }
}
