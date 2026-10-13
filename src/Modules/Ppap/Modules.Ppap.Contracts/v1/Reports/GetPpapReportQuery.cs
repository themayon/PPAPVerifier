using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Reports;

public sealed record GetPpapReportQuery(Guid Id) : IQuery<PpapReportDto>;
