using DES.Modules.Identity.Contracts.DTOs;
using Mediator;

namespace DES.Modules.Identity.Contracts.v1.Impersonation.EndImpersonation;

public sealed record EndImpersonationCommand() : ICommand<TokenResponse>;
