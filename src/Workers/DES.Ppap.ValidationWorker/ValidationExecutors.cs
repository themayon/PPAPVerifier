using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DES.Modules.Ppap.Contracts.Events;

namespace DES.Ppap.ValidationWorker;

internal sealed record ComplexValidationExecutionResult(
    string Status,
    string? Message,
    string ExecutorVersion,
    long DurationMilliseconds,
    IReadOnlyList<ComplexValidationEvidence> Evidence);

internal interface IComplexValidationExecutor
{
    bool CanExecute(string executorKey);
    ValueTask<ComplexValidationExecutionResult> ExecuteAsync(
        ComplexValidationRequestedIntegrationEvent request,
        CancellationToken cancellationToken);
}

internal sealed class ComplexValidationExecutorRegistry(IEnumerable<IComplexValidationExecutor> executors)
{
    private readonly IComplexValidationExecutor[] _executors = [.. executors];

    public IComplexValidationExecutor Resolve(string executorKey) =>
        _executors.FirstOrDefault(executor => executor.CanExecute(executorKey))
        ?? throw new InvalidOperationException($"No complex validation executor is registered for '{executorKey}'.");
}

internal sealed class JsonPathComparisonExecutor : IComplexValidationExecutor
{
    private const string ExecutorKey = "json-path-compare:v1";

    public bool CanExecute(string executorKey) => string.Equals(executorKey, ExecutorKey, StringComparison.OrdinalIgnoreCase);

    public ValueTask<ComplexValidationExecutionResult> ExecuteAsync(
        ComplexValidationRequestedIntegrationEvent request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch stopwatch = Stopwatch.StartNew();
        ComparisonParameters parameters = JsonSerializer.Deserialize<ComparisonParameters>(request.ParametersJson, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Validation parameters are invalid.");
        ComplexValidationDocumentInput? leftDocument = request.Documents.FirstOrDefault(item => string.Equals(item.DocumentCode, parameters.LeftDocumentCode, StringComparison.OrdinalIgnoreCase));
        ComplexValidationDocumentInput? rightDocument = request.Documents.FirstOrDefault(item => string.Equals(item.DocumentCode, parameters.RightDocumentCode, StringComparison.OrdinalIgnoreCase));
        if (leftDocument is null || rightDocument is null)
        {
            return ValueTask.FromResult(new ComplexValidationExecutionResult("NotEvaluated", "A referenced document is not ready.", "1.0", stopwatch.ElapsedMilliseconds, []));
        }

        using JsonDocument leftJson = JsonDocument.Parse(leftDocument.CanonicalJson);
        using JsonDocument rightJson = JsonDocument.Parse(rightDocument.CanonicalJson);
        if (!TryResolve(leftJson.RootElement, parameters.LeftPath, out JsonElement left)
            || !TryResolve(rightJson.RootElement, parameters.RightPath, out JsonElement right))
        {
            return ValueTask.FromResult(new ComplexValidationExecutionResult("NotEvaluated", "A referenced value was not found.", "1.0", stopwatch.ElapsedMilliseconds, []));
        }

        string leftValue = Scalar(left);
        string rightValue = Scalar(right);
        bool passed = Compare(leftValue, rightValue, parameters.Operator);
        ComplexValidationEvidence[] evidence =
        [
            Evidence(leftDocument, parameters.LeftPath, parameters.LeftSheetName, parameters.LeftCell, leftValue, rightValue),
            Evidence(rightDocument, parameters.RightPath, parameters.RightSheetName, parameters.RightCell, rightValue, leftValue)
        ];
        stopwatch.Stop();
        return ValueTask.FromResult(new ComplexValidationExecutionResult(
            passed ? "Passed" : "Failed",
            passed ? null : $"{parameters.LeftDocumentCode} value '{leftValue}' does not satisfy {parameters.Operator} against {parameters.RightDocumentCode} value '{rightValue}'.",
            "1.0",
            stopwatch.ElapsedMilliseconds,
            evidence));
    }

    private static ComplexValidationEvidence Evidence(
        ComplexValidationDocumentInput document,
        string path,
        string? sheetName,
        string? cell,
        string observed,
        string expected) => new(
            document.DocumentCode,
            document.SubmissionDocumentId,
            document.DocumentRevisionId,
            "GeneratedWorkbook",
            null,
            null,
            null,
            null,
            null,
            sheetName,
            cell,
            cell,
            path,
            observed,
            expected);

    private static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(segment, out JsonElement property))
            {
                value = property;
                continue;
            }
            if (value.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                && index >= 0
                && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }
            value = default;
            return false;
        }
        return true;
    }

    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText()
    };

    private static bool Compare(string left, string right, string comparisonOperator)
    {
        if (decimal.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal leftNumber)
            && decimal.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rightNumber))
        {
            return comparisonOperator.ToUpperInvariant() switch
            {
                "EQUAL" => leftNumber == rightNumber,
                "NOTEQUAL" => leftNumber != rightNumber,
                "GREATERTHAN" => leftNumber > rightNumber,
                "GREATERTHANOREQUAL" => leftNumber >= rightNumber,
                "LESSTHAN" => leftNumber < rightNumber,
                "LESSTHANOREQUAL" => leftNumber <= rightNumber,
                _ => throw new InvalidDataException("The numeric comparison operator is unsupported.")
            };
        }
        return comparisonOperator.ToUpperInvariant() switch
        {
            "EQUAL" => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            "NOTEQUAL" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            "CONTAINS" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidDataException("The text comparison operator is unsupported.")
        };
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated by System.Text.Json deserialization.")]
    private sealed record ComparisonParameters(
        string LeftDocumentCode,
        string LeftPath,
        string RightDocumentCode,
        string RightPath,
        string Operator,
        string? LeftSheetName,
        string? LeftCell,
        string? RightSheetName,
        string? RightCell);
}
