using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using Mediator;

namespace DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;

public sealed record GetExtractionAttemptQuery(Guid Id) : IQuery<ExtractionAttemptDto>;
