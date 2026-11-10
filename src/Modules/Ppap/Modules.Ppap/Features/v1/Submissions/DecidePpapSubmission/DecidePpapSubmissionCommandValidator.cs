using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.DecidePpapSubmission;

public sealed class DecidePpapSubmissionCommandValidator : AbstractValidator<DecidePpapSubmissionCommand>
{
    public DecidePpapSubmissionCommandValidator()
    {
        RuleFor(x => x.SubmissionId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Decision).Must(x => TryParse(x, out _)).WithMessage("Decision must be Approved, InterimApproved, or Rejected.");
        RuleFor(x => x.Comment).MaximumLength(1000);
        RuleFor(x => x.Comment).NotEmpty().When(x => TryParse(x.Decision, out SubmissionDecision d) && d != SubmissionDecision.Approved)
            .WithMessage("A comment is required for interim approval or rejection.");
    }

    internal static bool TryParse(string? value, out SubmissionDecision decision)
    {
        decision = SubmissionDecision.None;
        foreach (SubmissionDecision candidate in new[] { SubmissionDecision.Approved, SubmissionDecision.InterimApproved, SubmissionDecision.Rejected })
        {
            if (string.Equals(candidate.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                decision = candidate;
                return true;
            }
        }
        return false;
    }
}
