using FluentValidation;
using DES.Modules.Identity.Contracts.v1.Sessions.RevokeSession;

namespace DES.Modules.Identity.Features.v1.Sessions.RevokeSession;

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("Session ID is required.");
    }
}