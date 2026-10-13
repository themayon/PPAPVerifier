using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Reports.WaivePpapFinding;

public sealed class WaivePpapFindingCommandValidator : AbstractValidator<WaivePpapFindingCommand>
{
    public WaivePpapFindingCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.FindingKind).IsInEnum();
        RuleFor(x => x.FindingId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PpapFindingWaiver.ReasonMaxLength);
    }
}
