using System.Text.Json;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Features;
using DES.Modules.Rules.Runtime;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleEvaluationRunTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(RuleEvaluationStatus.Passed, RuleEvaluationStatus.Failed, EvaluationRunStatus.Completed)]
    [InlineData(RuleEvaluationStatus.Passed, RuleEvaluationStatus.NotEvaluated, EvaluationRunStatus.PartiallyCompleted)]
    [InlineData(RuleEvaluationStatus.Passed, RuleEvaluationStatus.Error, EvaluationRunStatus.PartiallyCompleted)]
    [InlineData(RuleEvaluationStatus.Error, RuleEvaluationStatus.Error, EvaluationRunStatus.Failed)]
    public void Complete_Should_CalculateStatus(
        RuleEvaluationStatus first,
        RuleEvaluationStatus second,
        EvaluationRunStatus expected)
    {
        RuleEvaluationRun run = CreateRun();
        run.AppendResult(Guid.NewGuid(), Guid.NewGuid(), "A", first, null, "[]", Now);
        run.AppendResult(Guid.NewGuid(), Guid.NewGuid(), "B", second, null, "[]", Now);

        run.Complete(Now.AddSeconds(1));

        run.Status.ShouldBe(expected);
        run.CompletedAtUtc.ShouldBe(Now.AddSeconds(1));
    }

    [Fact]
    public void AppendResult_Should_RejectDuplicateRuleVersionAndDocumentScope()
    {
        RuleEvaluationRun run = CreateRun();
        Guid definitionId = Guid.NewGuid();
        Guid versionId = Guid.NewGuid();
        run.AppendResult(definitionId, versionId, "psw", RuleEvaluationStatus.Passed, null, "[]", Now);

        Should.Throw<InvalidOperationException>(() =>
            run.AppendResult(definitionId, versionId, "PSW", RuleEvaluationStatus.Failed, null, "[]", Now));
    }

    [Fact]
    public void TerminalRun_Should_RejectFurtherMutation()
    {
        RuleEvaluationRun run = CreateRun();
        run.AppendResult(Guid.NewGuid(), Guid.NewGuid(), null, RuleEvaluationStatus.Passed, null, "[]", Now);
        run.Complete(Now.AddSeconds(1));

        Should.Throw<InvalidOperationException>(() =>
            run.AppendResult(Guid.NewGuid(), Guid.NewGuid(), null, RuleEvaluationStatus.Passed, null, "[]", Now));
        Should.Throw<InvalidOperationException>(() => run.Complete(Now.AddSeconds(2)));
    }

    private static RuleEvaluationRun CreateRun() => RuleEvaluationRun.Start(
        "tenant",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "v1",
        Now);
}

public sealed class EvaluateTemplateCommandValidatorTests
{
    [Fact]
    public void Validate_Should_AcceptValidDocuments()
    {
        EvaluateTemplateCommand command = Create(new Dictionary<string, string> { ["PSW"] = "{\"approved\":true}" });

        new EvaluateTemplateCommandValidator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_RejectInvalidIdentifiersSchemaAndDocumentCount()
    {
        var command = new EvaluateTemplateCommand(Guid.Empty, Guid.Empty, Guid.Empty, new string('v', 33), new Dictionary<string, string>());

        var result = new EvaluateTemplateCommandValidator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.TemplateVersionId));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.SubjectId));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.OwnerUserId));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.InputSchemaVersion));
        result.Errors.ShouldContain(x => x.PropertyName == nameof(command.Documents));
    }

    [Fact]
    public void Validate_Should_RejectInvalidJsonCodePerDocumentAndTotalBounds()
    {
        string oversized = $"{{\"value\":\"{new string('x', 256 * 1024)}\"}}";
        var documents = Enumerable.Range(1, 9).ToDictionary(
            x => x == 1 ? "BAD CODE" : $"D{x}",
            x => x == 2 ? "[]" : oversized);
        EvaluateTemplateCommand command = Create(documents);

        var result = new EvaluateTemplateCommandValidator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.Count(x => x.PropertyName == nameof(command.Documents)).ShouldBeGreaterThanOrEqualTo(3);
    }

    private static EvaluateTemplateCommand Create(IReadOnlyDictionary<string, string> documents) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "v1",
        documents);
}

public sealed class TestRuleCommandTests
{
    private const string ParameterAst = """{"type":"comparison","operator":"In","left":{"type":"parameter","name":"selected"},"right":{"type":"parameter","name":"allowed"}}""";

    [Fact]
    public void Validate_Should_AcceptOptionalBoundedParameters()
    {
        var command = new TestRuleCommand(ParameterAst, new Dictionary<string, string>(), """{"selected":"B","allowed":["A","B"]}""");

        new TestRuleCommandValidator().Validate(command).IsValid.ShouldBeTrue();
        new TestRuleCommandValidator().Validate(command with { ParametersJson = null }).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_RejectMalformedNonObjectOversizedDeepAndUnsafeParameters()
    {
        string deep = "0";
        for (int i = 0; i < RuleParameterContext.MaxDepth; i++) deep = $"{{\"level\":{deep}}}";
        string[] invalid =
        [
            "{",
            "[]",
            $"{{\"value\":\"{new string('x', RuleParameterContext.MaxJsonBytes)}\"}}",
            deep,
            "{\"bad name\":1}",
        ];
        var validator = new TestRuleCommandValidator();

        foreach (string parametersJson in invalid)
        {
            var command = new TestRuleCommand(ParameterAst, new Dictionary<string, string>(), parametersJson);
            var result = validator.Validate(command);

            result.IsValid.ShouldBeFalse(parametersJson);
            result.Errors.ShouldContain(x => x.PropertyName == nameof(command.ParametersJson));
        }
    }

    [Fact]
    public async Task Handle_Should_EvaluatePrimitiveAndArrayParameters()
    {
        var command = new TestRuleCommand(ParameterAst, new Dictionary<string, string>(), """{"selected":"B","allowed":["A","B"]}""");

        RuleEvaluationResultDto result = await new TestRuleCommandHandler().Handle(command, CancellationToken.None);

        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
        result.Trace.Single().Left.ShouldBe("B");
        result.Trace.Single().Right.ShouldBe("[array:2]");
    }

    [Fact]
    public async Task Handle_Should_PreserveLiteralBehavior_WhenParametersAreOmitted()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";

        RuleEvaluationResultDto result = await new TestRuleCommandHandler().Handle(
            new TestRuleCommand(ast, new Dictionary<string, string>()),
            CancellationToken.None);

        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
    }
}

public sealed class TemplateEvaluationOrchestratorTests
{
    [Fact]
    public void Evaluate_Should_ReturnNotEvaluated_WhenAssignedDocumentIsMissing()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";
        var context = new RuleEvaluationContext(new Dictionary<string, JsonElement>());

        RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate(ast, "PSW", context);

        result.Status.ShouldBe(RuleEvaluationStatus.NotEvaluated);
        result.Reason.ShouldBe("Assigned document was not supplied.");
    }

    [Fact]
    public void Evaluate_Should_PreserveAllDocumentsForCrossDocumentRule()
    {
        using JsonDocument psw = JsonDocument.Parse("{\"value\":1}");
        using JsonDocument fmea = JsonDocument.Parse("{\"value\":1}");
        var context = new RuleEvaluationContext(new Dictionary<string, JsonElement>
        {
            ["PSW"] = psw.RootElement.Clone(),
            ["FMEA"] = fmea.RootElement.Clone(),
        });
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"field","documentCode":"PSW","path":"value"},"right":{"type":"field","documentCode":"FMEA","path":"value"}}""";

        RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate(ast, "PSW", context);

        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
    }

    [Fact]
    public void Evaluate_Should_ReturnError_WhenStoredAstIsMalformed()
    {
        RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate("{", null, new RuleEvaluationContext(new Dictionary<string, JsonElement>()));

        result.Status.ShouldBe(RuleEvaluationStatus.Error);
        result.Reason.ShouldBe("Stored rule AST is invalid.");
        TemplateEvaluationOrchestrator.SerializeTrace(result.Trace).ShouldBe("[]");
    }

    [Fact]
    public void Evaluate_Should_ReturnSanitizedError_WhenStoredParametersAreMalformed()
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";

        RuleEvaluationResult result = TemplateEvaluationOrchestrator.Evaluate(
            ast,
            null,
            new RuleEvaluationContext(new Dictionary<string, JsonElement>()),
            "{\"private-secret\":");

        result.Status.ShouldBe(RuleEvaluationStatus.Error);
        result.Reason.ShouldBe("Stored assignment parameters are invalid.");
        result.Reason!.ShouldNotContain("private-secret");
        result.Trace.ShouldBeEmpty();
    }
}

public sealed class OutcomeMessageParserTests
{
    [Theory]
    [InlineData(RuleEvaluationStatus.Failed, "Failed message")]
    [InlineData(RuleEvaluationStatus.NotEvaluated, "Not evaluated message")]
    [InlineData(RuleEvaluationStatus.Error, "Error message")]
    public void Parse_Should_ReturnMessageConfiguredForOutcome(RuleEvaluationStatus status, string expected)
    {
        const string json = """{"outcomes":{"failed":{"message":"Failed message"},"notEvaluated":"Not evaluated message","error":{"message":"Error message"}}}""";

        OutcomeMessageParser.Parse(json, status).ShouldBe(expected);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"failed\":42}")]
    public void Parse_Should_ReturnNull_WhenOutcomeConfigurationCannotProvideMessage(string json)
    {
        OutcomeMessageParser.Parse(json, RuleEvaluationStatus.Failed).ShouldBeNull();
    }
}
