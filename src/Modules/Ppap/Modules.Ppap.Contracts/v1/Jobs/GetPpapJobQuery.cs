using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Jobs;

public sealed record GetPpapJobQuery(Guid Id) : IQuery<PpapJobDto>;
