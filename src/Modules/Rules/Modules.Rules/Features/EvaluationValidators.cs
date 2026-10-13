using System.Text;
using System.Text.Json;
using DES.Modules.Rules.Contracts;
using FluentValidation;
using FluentValidation.Results;

namespace DES.Modules.Rules.Features;

public sealed class EvaluateTemplateCommandValidator : AbstractValidator<EvaluateTemplateCommand>
{
    private const int MaxDocumentBytes = 256 * 1024;
    private const int MaxTotalBytes = 2 * 1024 * 1024;

    public EvaluateTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateVersionId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.InputSchemaVersion).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Documents).NotNull().Must(x => x is { Count: >= 1 and <= 50 })
            .WithMessage("Between 1 and 50 documents are required.");
        RuleFor(x => x.Documents).Custom(ValidateDocuments);
    }

    private static void ValidateDocuments(IReadOnlyDictionary<string, string>? documents, ValidationContext<EvaluateTemplateCommand> context)
    {
        if (documents is null) return;
        int totalBytes = 0;
        var normalizedCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string code, string json) in documents)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 32 || code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            {
                context.AddFailure(new ValidationFailure(nameof(EvaluateTemplateCommand.Documents), $"Document code '{code}' is invalid."));
            }
            else if (!normalizedCodes.Add(code.Trim().ToUpperInvariant()))
            {
                context.AddFailure(new ValidationFailure(nameof(EvaluateTemplateCommand.Documents), "Document codes must be unique ignoring case."));
            }

            if (json is null)
            {
                context.AddFailure(new ValidationFailure(nameof(EvaluateTemplateCommand.Documents), $"Document '{code}' must be a JSON object."));
                continue;
            }

            int bytes = Encoding.UTF8.GetByteCount(json);
            totalBytes += bytes;
            if (bytes > MaxDocumentBytes || !IsJsonObject(json))
            {
                context.AddFailure(new ValidationFailure(nameof(EvaluateTemplateCommand.Documents), $"Document '{code}' must be a JSON object no larger than 256KB."));
            }
        }

        if (totalBytes > MaxTotalBytes)
        {
            context.AddFailure(new ValidationFailure(nameof(EvaluateTemplateCommand.Documents), "The total document payload must not exceed 2MB."));
        }
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
