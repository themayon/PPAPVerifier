using DES.Modules.Auditing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Auditing.Contracts.v1.GetAuditById;

public sealed record GetAuditByIdQuery(Guid Id) : IQuery<AuditDetailDto>;