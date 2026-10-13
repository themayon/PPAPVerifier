using System.Globalization;
using System.Text;
using System.Text.Json;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Runtime;

internal abstract record RuleNode;
internal sealed record AllNode(IReadOnlyList<RuleNode> Children) : RuleNode;
internal sealed record AnyNode(IReadOnlyList<RuleNode> Children) : RuleNode;
internal sealed record NotNode(RuleNode Child) : RuleNode;
internal sealed record ComparisonNode(ComparisonOperator Operator, Operand Left, Operand? Right) : RuleNode;
internal abstract record Operand;
internal sealed record FieldOperand(string DocumentCode, string Path) : Operand;
internal sealed record LiteralOperand(JsonElement Value) : Operand;
internal sealed record ParameterOperand(string Name) : Operand;
internal enum ComparisonOperator { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Contains, In, IsEmpty, IsNotEmpty }
internal sealed record RuleEvaluationContext(
    IReadOnlyDictionary<string, JsonElement> Documents,
    IReadOnlyDictionary<string, JsonElement>? Parameters = null);
internal sealed record RuleTrace(string Node, string? Operator, string? Left, string? Right, RuleEvaluationStatus Status, string? Reason);
internal sealed record RuleEvaluationResult(RuleEvaluationStatus Status, string? Reason, IReadOnlyList<RuleTrace> Trace);

internal static class RuleAstSerializer
{
    internal const int MaxJsonLength = 65536;
    public static RuleNode Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) throw new ArgumentException("AST JSON is empty or too large.", nameof(json));
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        int count = 0;
        RuleNode node = ParseNode(document.RootElement, 1, ref count);
        return node;
    }

    public static string Serialize(RuleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteNode(writer, node);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static RuleNode ParseNode(JsonElement value, int depth, ref int count)
    {
        RequireObject(value, "node");
        if (depth > 10) throw new ArgumentException("AST depth exceeds 10.");
        if (++count > 100) throw new ArgumentException("AST node count exceeds 100.");
        string type = RequiredString(value, "type", 32);
        RejectUnknown(value, type == "comparison" ? ["type", "operator", "left", "right"] : type == "not" ? ["type", "child"] : ["type", "children"]);
        return type switch
        {
            "all" => new AllNode(ParseChildren(value, depth, ref count)),
            "any" => new AnyNode(ParseChildren(value, depth, ref count)),
            "not" => new NotNode(ParseNode(Required(value, "child"), depth + 1, ref count)),
            "comparison" => ParseComparison(value),
            _ => throw new ArgumentException($"Unknown AST node '{type}'."),
        };
    }

    private static IReadOnlyList<RuleNode> ParseChildren(JsonElement value, int depth, ref int count)
    {
        JsonElement children = Required(value, "children");
        if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() is < 1 or > 25) throw new ArgumentException("All/Any require 1 to 25 children.");
        var result = new List<RuleNode>();
        foreach (JsonElement child in children.EnumerateArray()) result.Add(ParseNode(child, depth + 1, ref count));
        return result;
    }

    private static ComparisonNode ParseComparison(JsonElement value)
    {
        if (!Enum.TryParse(RequiredString(value, "operator", 32), false, out ComparisonOperator op)) throw new ArgumentException("Unknown comparison operator.");
        Operand left = ParseOperand(Required(value, "left"));
        Operand? right = value.TryGetProperty("right", out JsonElement r) ? ParseOperand(r) : null;
        bool unary = op is ComparisonOperator.IsEmpty or ComparisonOperator.IsNotEmpty;
        if (unary != (right is null)) throw new ArgumentException(unary ? "Unary operator cannot have a right operand." : "Binary operator requires a right operand.");
        if (op == ComparisonOperator.In)
        {
            if (right is LiteralOperand literal)
            {
                if (literal.Value.ValueKind != JsonValueKind.Array) throw new ArgumentException("In requires an array operand.");
                if (literal.Value.GetArrayLength() > 100) throw new ArgumentException("In array exceeds 100 values.");
            }
            else if (right is not ParameterOperand)
            {
                throw new ArgumentException("In requires a literal or parameter array.");
            }
        }
        return new ComparisonNode(op, left, right);
    }

    private static Operand ParseOperand(JsonElement value)
    {
        RequireObject(value, "operand"); string type = RequiredString(value, "type", 16);
        if (type == "field")
        {
            RejectUnknown(value, ["type", "documentCode", "path"]);
            string code = RequiredString(value, "documentCode", 64); string path = RequiredString(value, "path", 512);
            string[] segments = path.Split('.', StringSplitOptions.None);
            if (segments.Length > 10 || segments.Any(s => s.Length is 0 or > 128 || s is ".." || s.Contains('[') || s.Contains(']') || s.Contains('$'))) throw new ArgumentException("Invalid field path.");
            return new FieldOperand(code, path);
        }
        if (type == "literal") { RejectUnknown(value, ["type", "value"]); return new LiteralOperand(Required(value, "value").Clone()); }
        if (type == "parameter")
        {
            RejectUnknown(value, ["type", "name"]);
            if (value.EnumerateObject().Count() != 2) throw new ArgumentException("Parameter operand must contain exactly 'type' and 'name'.");
            string name = RequiredString(value, "name", RuleParameterContext.MaxNameLength);
            if (!RuleParameterContext.IsSafeName(name)) throw new ArgumentException("Invalid parameter name.");
            return new ParameterOperand(name);
        }
        throw new ArgumentException($"Unknown operand '{type}'.");
    }

    private static void WriteNode(Utf8JsonWriter w, RuleNode n)
    {
        w.WriteStartObject();
        switch (n)
        {
            case AllNode a: w.WriteString("type", "all"); WriteChildren(w, a.Children); break;
            case AnyNode a: w.WriteString("type", "any"); WriteChildren(w, a.Children); break;
            case NotNode x: w.WriteString("type", "not"); w.WritePropertyName("child"); WriteNode(w, x.Child); break;
            case ComparisonNode c: w.WriteString("type", "comparison"); w.WriteString("operator", c.Operator.ToString()); w.WritePropertyName("left"); WriteOperand(w, c.Left); if (c.Right is not null) { w.WritePropertyName("right"); WriteOperand(w, c.Right); } break;
            default: throw new ArgumentException("Unknown AST node.", nameof(n));
        }
        w.WriteEndObject();
    }
    private static void WriteChildren(Utf8JsonWriter w, IReadOnlyList<RuleNode> children) { w.WritePropertyName("children"); w.WriteStartArray(); foreach (RuleNode child in children) WriteNode(w, child); w.WriteEndArray(); }
    private static void WriteOperand(Utf8JsonWriter w, Operand o) { w.WriteStartObject(); if (o is FieldOperand f) { w.WriteString("type", "field"); w.WriteString("documentCode", f.DocumentCode); w.WriteString("path", f.Path); } else if (o is LiteralOperand l) { w.WriteString("type", "literal"); w.WritePropertyName("value"); l.Value.WriteTo(w); } else if (o is ParameterOperand p) { w.WriteString("type", "parameter"); w.WriteString("name", p.Name); } else throw new ArgumentException("Unknown operand.", nameof(o)); w.WriteEndObject(); }
    private static JsonElement Required(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement value) ? value : throw new ArgumentException($"Missing '{name}'.");
    private static string RequiredString(JsonElement e, string name, int max) { JsonElement v = Required(e, name); if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()) || v.GetString()!.Length > max) throw new ArgumentException($"Invalid '{name}'."); return v.GetString()!; }
    private static void RequireObject(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{name} must be an object."); }
    private static void RejectUnknown(JsonElement e, IReadOnlyCollection<string> allowed) { foreach (JsonProperty p in e.EnumerateObject()) if (!allowed.Contains(p.Name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown property '{p.Name}'."); }
}

internal static class RuleAstMetrics
{
    public static int CountConditions(string? astJson)
    {
        if (string.IsNullOrWhiteSpace(astJson)) return 0;
        try { return Count(RuleAstSerializer.Parse(astJson)); }
        catch (JsonException) { return 0; }
        catch (ArgumentException) { return 0; }
    }

    private static int Count(RuleNode node) => node switch
    {
        AnyNode { Children: [ComparisonNode value, ComparisonNode { Operator: ComparisonOperator.IsEmpty } empty] } when value.Left == empty.Left && value.Operator is not (ComparisonOperator.IsEmpty or ComparisonOperator.IsNotEmpty) => 1,
        AllNode all => all.Children.Sum(Count),
        AnyNode any => any.Children.Sum(Count),
        NotNode not => Count(not.Child),
        ComparisonNode => 1,
        _ => 0,
    };
}

internal static class RuleParameterContext
{
    internal const int MaxJsonBytes = 16 * 1024;
    internal const int MaxDepth = 10;
    internal const int MaxPropertyCount = 100;
    internal const int MaxNameLength = 64;

    internal static IReadOnlyDictionary<string, JsonElement> Parse(string? json)
    {
        string value = json ?? "{}";
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > MaxJsonBytes) throw new ArgumentException("Parameters JSON is empty or too large.", nameof(json));
        using JsonDocument document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = MaxDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Parameters JSON must be an object.", nameof(json));
        int propertyCount = 0;
        ValidateValue(document.RootElement, 1, ref propertyCount);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!result.TryAdd(property.Name, property.Value.Clone())) throw new ArgumentException("Parameters JSON contains a duplicate property.", nameof(json));
        }
        return result;
    }

    internal static bool IsSafeName(string name) => name.Length is > 0 and <= MaxNameLength
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static void ValidateValue(JsonElement value, int depth, ref int propertyCount)
    {
        if (depth > MaxDepth) throw new ArgumentException("Parameters JSON depth exceeds 10.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (++propertyCount > MaxPropertyCount) throw new ArgumentException("Parameters JSON property count exceeds 100.");
                if (!IsSafeName(property.Name)) throw new ArgumentException("Parameters JSON contains an invalid property name.");
                if (!names.Add(property.Name)) throw new ArgumentException("Parameters JSON contains a duplicate property.");
                ValidateValue(property.Value, depth + 1, ref propertyCount);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) ValidateValue(item, depth + 1, ref propertyCount);
        }
    }
}

internal sealed class RuleEvaluator
{
    private const int MaxTrace = 100;
    public RuleEvaluationResult Evaluate(RuleNode node, RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(node); ArgumentNullException.ThrowIfNull(context);
        var trace = new List<RuleTrace>(); Outcome outcome = Visit(node, context, trace);
        return new(outcome.Status, outcome.Reason, trace);
    }

    private static Outcome Visit(RuleNode node, RuleEvaluationContext context, List<RuleTrace> trace)
    {
        Outcome result = node switch
        {
            AllNode all => Combine(all.Children, context, trace, true), AnyNode any => Combine(any.Children, context, trace, false),
            NotNode not => Negate(Visit(not.Child, context, trace)), ComparisonNode comparison => Compare(comparison, context),
            _ => new(RuleEvaluationStatus.Error, "Unknown node."),
        };
        ComparisonNode? currentComparison = node as ComparisonNode;
        string? left = currentComparison is null ? null : Sanitize(Resolve(currentComparison.Left, context).Value);
        string? right = currentComparison?.Right is null ? null : Sanitize(Resolve(currentComparison.Right, context).Value);
        AddTrace(trace, new(node.GetType().Name.Replace("Node", string.Empty, StringComparison.Ordinal), currentComparison?.Operator.ToString(), left, right, result.Status, result.Reason));
        return result;
    }

    private static Outcome Combine(IReadOnlyList<RuleNode> children, RuleEvaluationContext context, List<RuleTrace> trace, bool all)
    {
        var outcomes = children.Select(x => Visit(x, context, trace)).ToArray();
        if (outcomes.Any(x => x.Status == RuleEvaluationStatus.Error)) return outcomes.First(x => x.Status == RuleEvaluationStatus.Error);
        if (all && outcomes.Any(x => x.Status == RuleEvaluationStatus.Failed)) return new(RuleEvaluationStatus.Failed, null);
        if (!all && outcomes.Any(x => x.Status == RuleEvaluationStatus.Passed)) return new(RuleEvaluationStatus.Passed, null);
        if (outcomes.Any(x => x.Status == RuleEvaluationStatus.NotEvaluated)) return outcomes.First(x => x.Status == RuleEvaluationStatus.NotEvaluated);
        return new(all ? RuleEvaluationStatus.Passed : RuleEvaluationStatus.Failed, null);
    }
    private static Outcome Negate(Outcome x) => x.Status switch { RuleEvaluationStatus.Passed => new(RuleEvaluationStatus.Failed, null), RuleEvaluationStatus.Failed => new(RuleEvaluationStatus.Passed, null), _ => x };

    private static Outcome Compare(ComparisonNode c, RuleEvaluationContext context)
    {
        Resolved left = Resolve(c.Left, context); if (left.Outcome is not null) return left.Outcome;
        if (c.Operator is ComparisonOperator.IsEmpty or ComparisonOperator.IsNotEmpty)
        {
            bool empty = left.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || left.Value.ValueKind == JsonValueKind.String && left.Value.GetString()!.Length == 0 || left.Value.ValueKind == JsonValueKind.Array && left.Value.GetArrayLength() == 0 || left.Value.ValueKind == JsonValueKind.Object && !left.Value.EnumerateObject().Any();
            return Bool(c.Operator == ComparisonOperator.IsEmpty ? empty : !empty);
        }
        Resolved right = Resolve(c.Right!, context); if (right.Outcome is not null) return right.Outcome;
        if (c.Operator == ComparisonOperator.In) return In(left.Value, right.Value);
        if (c.Operator == ComparisonOperator.Contains)
        {
            if (left.Value.ValueKind != JsonValueKind.String || right.Value.ValueKind != JsonValueKind.String) return TypeError();
            return Bool(left.Value.GetString()!.Contains(right.Value.GetString()!, StringComparison.OrdinalIgnoreCase));
        }
        int? order = Order(left.Value, right.Value); if (order is null) return TypeError();
        return Bool(c.Operator switch { ComparisonOperator.Equal => order == 0, ComparisonOperator.NotEqual => order != 0, ComparisonOperator.GreaterThan => order > 0, ComparisonOperator.GreaterThanOrEqual => order >= 0, ComparisonOperator.LessThan => order < 0, ComparisonOperator.LessThanOrEqual => order <= 0, _ => false });
    }

    private static Resolved Resolve(Operand operand, RuleEvaluationContext context)
    {
        if (operand is LiteralOperand literal) return new(literal.Value, null);
        if (operand is ParameterOperand parameter)
        {
            if (context.Parameters is null || !context.Parameters.TryGetValue(parameter.Name, out JsonElement value)) return new(default, new(RuleEvaluationStatus.Error, "Parameter was not supplied."));
            return new(value, null);
        }
        var field = (FieldOperand)operand;
        if (!context.Documents.TryGetValue(field.DocumentCode, out JsonElement current)) return new(default, new(RuleEvaluationStatus.NotEvaluated, "Document was not supplied."));
        foreach (string segment in field.Path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return new(default, TypeError());
            if (!current.TryGetProperty(segment, out current)) return new(default, new(RuleEvaluationStatus.NotEvaluated, "Field was not found."));
        }
        return new(current, null);
    }
    private static Outcome In(JsonElement left, JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 100) return TypeError();
        foreach (JsonElement item in array.EnumerateArray()) { int? result = Order(left, item); if (result == 0) return Bool(true); if (result is null) continue; }
        return Bool(false);
    }
    private static int? Order(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out decimal ad) && b.TryGetDecimal(out decimal bd)) return ad.CompareTo(bd);
        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
        {
            string av = a.GetString()!; string bv = b.GetString()!;
            bool aDate = DateTimeOffset.TryParseExact(av, ["O", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at);
            bool bDate = DateTimeOffset.TryParseExact(bv, ["O", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset bt);
            if (aDate != bDate) return null;
            return aDate ? at.CompareTo(bt) : StringComparer.OrdinalIgnoreCase.Compare(av, bv);
        }
        if (a.ValueKind is JsonValueKind.True or JsonValueKind.False && b.ValueKind is JsonValueKind.True or JsonValueKind.False) return a.GetBoolean().CompareTo(b.GetBoolean());
        if (a.ValueKind == JsonValueKind.Null && b.ValueKind == JsonValueKind.Null) return 0;
        return null;
    }
    private static string? Sanitize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString() is { } text ? text[..Math.Min(text.Length, 128)] : null,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => value.GetRawText(),
        JsonValueKind.Array => $"[array:{value.GetArrayLength().ToString(CultureInfo.InvariantCulture)}]",
        JsonValueKind.Object => "[object]",
        _ => null,
    };
    private static Outcome Bool(bool value) => new(value ? RuleEvaluationStatus.Passed : RuleEvaluationStatus.Failed, null);
    private static Outcome TypeError() => new(RuleEvaluationStatus.Error, "Operand types are incompatible.");
    private static void AddTrace(List<RuleTrace> trace, RuleTrace item) { if (trace.Count < MaxTrace) trace.Add(item); }
    private sealed record Outcome(RuleEvaluationStatus Status, string? Reason);
    private readonly record struct Resolved(JsonElement Value, Outcome? Outcome);
}
