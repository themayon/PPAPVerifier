using DES.Modules.Billing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Wallets;

public sealed record GetMyWalletQuery : IQuery<WalletDto>;
