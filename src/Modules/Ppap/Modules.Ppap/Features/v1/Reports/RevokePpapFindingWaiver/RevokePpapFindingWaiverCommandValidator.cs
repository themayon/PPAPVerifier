using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Reports.RevokePpapFindingWaiver;

public sealed class RevokePpapFindingWaiverCommandValidator : AbstractValidator<RevokePpapFindingWaiverCommand>
{
    public RevokePpapFindingWaiverCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.WaiverId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PpapWaiverRevocation.ReasonMaxLength);
    }
}
