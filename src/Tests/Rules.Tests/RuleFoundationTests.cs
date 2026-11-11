using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Runtime;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleFoundationTests
{
    [Theory]
    [InlineData("Equal", "1", "1", RuleEvaluationStatus.Passed)]
    [InlineData("NotEqual", "1", "2", RuleEvaluationStatus.Passed)]
    [InlineData("GreaterThan", "2", "1", RuleEvaluationStatus.Passed)]
    [InlineData("GreaterThanOrEqual", "2", "2.0", RuleEvaluationStatus.Passed)]
    [InlineData("LessThan", "1", "2", RuleEvaluationStatus.Passed)]
    [InlineData("LessThanOrEqual", "2", "2", RuleEvaluationStatus.Passed)]
    [InlineData("Contains", "\"Alphabet\"", "\"PHA\"", RuleEvaluationStatus.Passed)]
    public void Evaluate_Should_CoverBinaryOperators(string op, string left, string right, RuleEvaluationStatus expected)
    {
        string ast = $"{{\"type\":\"comparison\",\"operator\":\"{op}\",\"left\":{{\"type\":\"literal\",\"value\":{left}}},\"right\":{{\"type\":\"literal\",\"value\":{right}}}}}";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement>())).Status.ShouldBe(expected);
    }

    [Fact] public void Evaluate_Should_HandleAllAnyNotAndIn()
    {
        const string ast = """{"type":"all","children":[{"type":"not","child":{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":2}}},{"type":"any","children":[{"type":"comparison","operator":"In","left":{"type":"literal","value":"a"},"right":{"type":"literal","value":["A","b"]}}]}]}""";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement>())).Status.ShouldBe(RuleEvaluationStatus.Passed);
    }

    [Fact]
    public void Evaluate_Should_PassAny_WhenAnotherBranchIsNotEvaluated()
    {
        const string ast = """{"type":"any","children":[{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}},{"type":"comparison","operator":"Equal","left":{"type":"field","documentCode":"MISSING","path":"value"},"right":{"type":"literal","value":1}}]}""";
        RuleEvaluationResult result = new RuleEvaluator().Evaluate(
            RuleAstSerializer.Parse(ast),
            new RuleEvaluationContext(new Dictionary<string, JsonElement>()));
        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
    }

    [Fact]
    public void Evaluate_Should_FailAll_WhenAnotherBranchIsNotEvaluated()
    {
        const string ast = """{"type":"all","children":[{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":2}},{"type":"comparison","operator":"Equal","left":{"type":"field","documentCode":"MISSING","path":"value"},"right":{"type":"literal","value":1}}]}""";
        RuleEvaluationResult result = new RuleEvaluator().Evaluate(
            RuleAstSerializer.Parse(ast),
            new RuleEvaluationContext(new Dictionary<string, JsonElement>()));
        result.Status.ShouldBe(RuleEvaluationStatus.Failed);
    }

    [Theory]
    [InlineData("IsEmpty", "null", RuleEvaluationStatus.Passed)]
    [InlineData("IsEmpty", "\"\"", RuleEvaluationStatus.Passed)]
    [InlineData("IsNotEmpty", "[]", RuleEvaluationStatus.Failed)]
    public void Evaluate_Should_HandleEmpty(string op, string value, RuleEvaluationStatus expected)
    {
        string ast = $"{{\"type\":\"comparison\",\"operator\":\"{op}\",\"left\":{{\"type\":\"literal\",\"value\":{value}}}}}";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement>())).Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"targetDate":"2026-01-01"}""", "IsNotEmpty", RuleEvaluationStatus.Passed)]
    [InlineData("""{"targetDate":null}""", "IsNotEmpty", RuleEvaluationStatus.Failed)]
    [InlineData("""{"targetDate":""}""", "IsNotEmpty", RuleEvaluationStatus.Failed)]
    [InlineData("""{"other":1}""", "IsNotEmpty", RuleEvaluationStatus.Failed)]
    [InlineData("""{"other":1}""", "IsEmpty", RuleEvaluationStatus.Passed)]
    public void Evaluate_Should_TreatMissingFieldAsNull_ForNullChecks(string document, string op, RuleEvaluationStatus expected)
    {
        using JsonDocument doc = JsonDocument.Parse(document);
        string ast = """{"type":"comparison","operator":"OP","left":{"type":"field","documentCode":"DFMEA","path":"targetDate"}}""".Replace("OP", op, StringComparison.Ordinal);
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement> { ["DFMEA"] = doc.RootElement.Clone() })).Status.ShouldBe(expected);
    }

    [Fact]
    public void Evaluate_Should_StayNotEvaluated_ForNullCheckOnMissingDocument()
    {
        const string ast = """{"type":"comparison","operator":"IsNotEmpty","left":{"type":"field","documentCode":"DFMEA","path":"targetDate"}}""";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement>())).Status.ShouldBe(RuleEvaluationStatus.NotEvaluated);
    }

    [Fact] public void Evaluate_Should_ReturnNotEvaluatedForMissingCaseSensitiveField()
    {
        using JsonDocument doc = JsonDocument.Parse("""{"Name":"x"}""");
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"field","documentCode":"D","path":"name"},"right":{"type":"literal","value":"x"}}""";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement> { ["D"] = doc.RootElement.Clone() })).Status.ShouldBe(RuleEvaluationStatus.NotEvaluated);
    }

    [Fact] public void Evaluate_Should_ReturnErrorForTypeMismatch()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":true},"right":{"type":"literal","value":1}}""";
        new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(ast), new(new Dictionary<string, JsonElement>())).Status.ShouldBe(RuleEvaluationStatus.Error);
    }

    [Fact] public void Evaluate_Should_CompareIsoDatesAndReplayDeterministically()
    {
        const string ast = """{"type":"comparison","operator":"LessThan","left":{"type":"literal","value":"2025-01-01"},"right":{"type":"literal","value":"2025-01-02"}}""";
        RuleNode node = RuleAstSerializer.Parse(ast); var evaluator = new RuleEvaluator(); var context = new RuleEvaluationContext(new Dictionary<string, JsonElement>());
        RuleEvaluationResult first = evaluator.Evaluate(node, context); RuleEvaluationResult second = evaluator.Evaluate(node, context);
        first.Status.ShouldBe(second.Status); first.Trace.ToArray().ShouldBe(second.Trace.ToArray());
    }

    [Fact]
    public void Parse_Should_RoundTripParameterOperand()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"parameter","name":"expected.value"},"right":{"type":"literal","value":1}}""";

        string serialized = RuleAstSerializer.Serialize(RuleAstSerializer.Parse(ast));

        serialized.ShouldBe(ast);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("$value")]
    public void Parse_Should_RejectUnsafeParameterName(string name)
    {
        string ast = $"{{\"type\":\"comparison\",\"operator\":\"Equal\",\"left\":{{\"type\":\"parameter\",\"name\":\"{name}\"}},\"right\":{{\"type\":\"literal\",\"value\":1}}}}";

        Should.Throw<ArgumentException>(() => RuleAstSerializer.Parse(ast));
    }

    [Fact]
    public void Evaluate_Should_ResolvePrimitiveAndArrayParametersWithSanitizedTrace()
    {
        const string ast = """{"type":"all","children":[{"type":"comparison","operator":"Equal","left":{"type":"parameter","name":"threshold"},"right":{"type":"literal","value":3}},{"type":"comparison","operator":"In","left":{"type":"literal","value":"B"},"right":{"type":"parameter","name":"allowed"}}]}""";
        IReadOnlyDictionary<string, JsonElement> parameters = RuleParameterContext.Parse("""{"threshold":3,"allowed":["A","B"]}""");

        RuleEvaluationResult result = new RuleEvaluator().Evaluate(
            RuleAstSerializer.Parse(ast),
            new RuleEvaluationContext(new Dictionary<string, JsonElement>(), parameters));

        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
        result.Trace.ShouldContain(x => x.Left == "3");
        result.Trace.ShouldContain(x => x.Right == "[array:2]");
    }

    [Fact]
    public void Evaluate_Should_ReturnSanitizedError_WhenParameterIsMissing()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"parameter","name":"secret"},"right":{"type":"literal","value":1}}""";

        RuleEvaluationResult result = new RuleEvaluator().Evaluate(
            RuleAstSerializer.Parse(ast),
            new RuleEvaluationContext(new Dictionary<string, JsonElement>(), RuleParameterContext.Parse("{}")));

        result.Status.ShouldBe(RuleEvaluationStatus.Error);
        result.Reason.ShouldBe("Parameter was not supplied.");
        result.Reason!.ShouldNotContain("secret");
        result.Trace.Single().Left.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"type\":\"script\"}")]
    [InlineData("{\"type\":\"comparison\",\"operator\":\"Execute\",\"left\":{\"type\":\"literal\",\"value\":1},\"right\":{\"type\":\"literal\",\"value\":1}}")]
    [InlineData("{\"type\":\"comparison\",\"operator\":\"Equal\",\"left\":{\"type\":\"field\",\"documentCode\":\"D\",\"path\":\"$.x\"},\"right\":{\"type\":\"literal\",\"value\":1}}")]
    [InlineData("{\"type\":\"comparison\",\"operator\":\"Equal\",\"left\":{\"type\":\"parameter\",\"name\":\"x\",\"value\":1},\"right\":{\"type\":\"literal\",\"value\":1}}")]
    [InlineData("{\"type\":\"comparison\",\"operator\":\"Equal\",\"left\":{\"type\":\"parameter\",\"name\":\"x\",\"name\":\"y\"},\"right\":{\"type\":\"literal\",\"value\":1}}")]
    public void Parse_Should_RejectMaliciousShapes(string json) => Should.Throw<ArgumentException>(() => RuleAstSerializer.Parse(json));

    [Fact] public void Parse_Should_RejectDepthAndNodeCountLimits()
    {
        string deep = Comparison();
        for (int i = 0; i < 10; i++) deep = $"{{\"type\":\"not\",\"child\":{deep}}}";
        Should.Throw<ArgumentException>(() => RuleAstSerializer.Parse(deep));
        string leaf = Comparison(); string branch = $"{{\"type\":\"all\",\"children\":[{string.Join(',', Enumerable.Repeat(leaf, 25))}]}}";
        string many = $"{{\"type\":\"all\",\"children\":[{string.Join(',', Enumerable.Repeat(branch, 5))}]}}";
        Should.Throw<ArgumentException>(() => RuleAstSerializer.Parse(many));
    }

    [Fact] public void Parse_Should_RoundTripCanonicalAst()
    {
        RuleNode parsed = RuleAstSerializer.Parse(Comparison()); string serialized = RuleAstSerializer.Serialize(parsed);
        RuleAstSerializer.Serialize(RuleAstSerializer.Parse(serialized)).ShouldBe(serialized);
    }

    [Fact] public void Lifecycle_Should_EnforceImmutabilityAndSequentialClone()
    {
        DateTime now = DateTime.UnixEpoch; Guid user = Guid.NewGuid();
        RuleDefinition rule = RuleDefinition.Create("tenant", " code ", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        RuleVersion first = rule.Versions.Single(); rule.SubmitForReview(first.Id, 1, now); rule.Publish(first.Id, 2, true, user, now);
        Should.Throw<Exception>(() => rule.UpdateDraft(first.Id, 3, "x", null, RuleCategory.Row, Comparison(), "{}", now));
        RuleVersion clone = rule.ClonePublished(first.Id, 3, user, now); clone.VersionNumber.ShouldBe(2); rule.StableCode.ShouldBe("CODE");
    }

    [Fact]
    public void EnsureDeletable_Should_Allow_WhenRuleWasNeverPublished()
    {
        DateTime now = DateTime.UnixEpoch; Guid user = Guid.NewGuid();
        RuleDefinition draft = RuleDefinition.Create("tenant", "DRAFT", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        Should.NotThrow(() => draft.EnsureDeletable(1));
        RuleDefinition inReview = RuleDefinition.Create("tenant", "REVIEW", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        inReview.SubmitForReview(inReview.Versions.Single().Id, 1, now);
        Should.NotThrow(() => inReview.EnsureDeletable(2));
    }

    [Fact]
    public void EnsureDeletable_Should_Allow_WhenRuleWasPublishedOrRetired()
    {
        DateTime now = DateTime.UnixEpoch; Guid user = Guid.NewGuid();
        RuleDefinition rule = RuleDefinition.Create("tenant", "LIVE", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        RuleVersion first = rule.Versions.Single(); rule.SubmitForReview(first.Id, 1, now); rule.Publish(first.Id, 2, true, user, now);
        Should.NotThrow(() => rule.EnsureDeletable(3));
        rule.Retire(first.Id, 3, now);
        Should.NotThrow(() => rule.EnsureDeletable(4));
    }

    [Fact]
    public void Archive_Should_SetArchivedState_AndRestore_Should_ClearIt()
    {
        DateTime now = DateTime.UnixEpoch; Guid user = Guid.NewGuid();
        RuleDefinition rule = RuleDefinition.Create("tenant", "ARCH", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        RuleVersion first = rule.Versions.Single(); rule.SubmitForReview(first.Id, 1, now); rule.Publish(first.Id, 2, true, user, now);

        rule.Archive(3, user, now);
        rule.IsArchived.ShouldBeTrue(); rule.ArchivedAtUtc.ShouldBe(now); rule.ArchivedByUserId.ShouldBe(user); rule.Version.ShouldBe(4);
        rule.CurrentPublishedVersionId.ShouldBe(first.Id);
        Should.Throw<CustomException>(() => rule.Archive(4, user, now));

        rule.Restore(4, now);
        rule.IsArchived.ShouldBeFalse(); rule.ArchivedByUserId.ShouldBeNull(); rule.Version.ShouldBe(5);
        Should.Throw<CustomException>(() => rule.Restore(5, now));
    }

    [Fact]
    public void Archive_Should_Throw_WhenExpectedVersionIsStale()
    {
        RuleDefinition rule = RuleDefinition.Create("tenant", "ARCHSTALE", "Name", null, RuleCategory.Header, Comparison(), "{}", Guid.NewGuid(), DateTime.UnixEpoch);
        Should.Throw<CustomException>(() => rule.Archive(5, Guid.NewGuid(), DateTime.UnixEpoch));
    }

    [Fact]
    public void ArchivedRule_Should_RejectLifecycleChanges_UntilRestored()
    {
        DateTime now = DateTime.UnixEpoch; Guid user = Guid.NewGuid();
        RuleDefinition rule = RuleDefinition.Create("tenant", "LOCKED", "Name", null, RuleCategory.Header, Comparison(), "{}", user, now);
        RuleVersion first = rule.Versions.Single(); rule.SubmitForReview(first.Id, 1, now); rule.Publish(first.Id, 2, true, user, now);
        rule.Archive(3, user, now);

        Should.Throw<CustomException>(() => rule.ClonePublished(first.Id, 4, user, now));
        Should.Throw<CustomException>(() => rule.Retire(first.Id, 4, now));
        rule.Restore(4, now);
        Should.NotThrow(() => rule.ClonePublished(first.Id, 5, user, now));
    }

    [Fact]
    public void EnsureDeletable_Should_Throw_WhenExpectedVersionIsStale()
    {
        RuleDefinition rule = RuleDefinition.Create("tenant", "STALE", "Name", null, RuleCategory.Header, Comparison(), "{}", Guid.NewGuid(), DateTime.UnixEpoch);
        Should.Throw<CustomException>(() => rule.EnsureDeletable(5));
    }

    private static string Comparison() => """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";
}
