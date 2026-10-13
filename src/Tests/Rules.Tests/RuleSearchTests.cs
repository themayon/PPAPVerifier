using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Features;
using DES.Modules.Rules.Runtime;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleSearchTests
{
    private const string Status = """{"type":"comparison","operator":"NotEqual","left":{"type":"field","documentCode":"DFMEA","path":"actions.status"},"right":{"type":"literal","value":"Completed"}}""";
    private const string AfterTargetOrEmpty = """{"type":"any","children":[{"type":"comparison","operator":"GreaterThan","left":{"type":"field","documentCode":"DFMEA","path":"actions.completedOn"},"right":{"type":"field","documentCode":"DFMEA","path":"actions.targetDate"}},{"type":"comparison","operator":"IsEmpty","left":{"type":"field","documentCode":"DFMEA","path":"actions.completedOn"}}]}""";
    private const string OwnerEmpty = """{"type":"comparison","operator":"IsEmpty","left":{"type":"field","documentCode":"DFMEA","path":"actions.owner"}}""";

    [Fact]
    public void CountConditions_Should_CountComparisonsAndTreatIncludeEmptyPairAsOne()
    {
        string ast = """{"type":"not","child":{"type":"all","children":[""" + Status + """,{"type":"any","children":[""" + AfterTargetOrEmpty + "," + OwnerEmpty + "]}]}}";

        RuleAstMetrics.CountConditions(ast).ShouldBe(3);
    }

    [Fact]
    public void CountConditions_Should_CountEmptyCheckOnDifferentFieldSeparately()
    {
        string ast = """{"type":"any","children":[""" + Status + "," + OwnerEmpty + "]}";

        RuleAstMetrics.CountConditions(ast).ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"type":"unknown"}""")]
    public void CountConditions_Should_ReturnZero_ForMissingOrInvalidAst(string? ast)
    {
        RuleAstMetrics.CountConditions(ast).ShouldBe(0);
    }

    [Fact]
    public void ParseCategories_Should_ParseCommaSeparatedListCaseInsensitively()
    {
        RuleSearchFilters.ParseCategories(" row, Document ,row,Custom ").ShouldBe([RuleCategory.Row, RuleCategory.Document, RuleCategory.Custom]);
        RuleSearchFilters.ParseCategories(null).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Row,Document", true)]
    [InlineData("ModelInference", true)]
    [InlineData("Row,Nope", false)]
    [InlineData("1", false)]
    public void Validator_Should_ValidateCategoryList(string categories, bool valid)
    {
        new SearchRulesQueryValidator().Validate(new SearchRulesQuery { Categories = categories }).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData("Error", true)]
    [InlineData("Warning", true)]
    [InlineData("Info", true)]
    [InlineData("Critical", false)]
    [InlineData("error", false)]
    public void Validator_Should_RestrictSeverity(string severity, bool valid)
    {
        new SearchRulesQueryValidator().Validate(new SearchRulesQuery { Severity = severity }).IsValid.ShouldBe(valid);
    }

    [Theory]
    [InlineData("updated", true)]
    [InlineData("-updated", true)]
    [InlineData("random", false)]
    public void Validator_Should_AcceptUpdatedSort(string sort, bool valid)
    {
        new SearchRulesQueryValidator().Validate(new SearchRulesQuery { Sort = sort }).IsValid.ShouldBe(valid);
    }
}
