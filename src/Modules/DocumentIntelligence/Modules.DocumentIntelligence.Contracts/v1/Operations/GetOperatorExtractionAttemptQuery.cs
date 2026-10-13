using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using Mediator;

namespace DES.Modules.DocumentIntelligence.Contracts.v1.Operations;

public sealed record GetOperatorExtractionAttemptQuery(Guid Id) : IQuery<OperatorExtractionAttemptDto>;
