using Asp.Versioning;
using DES.Framework.Persistence;
using DES.Framework.Shared.Constants;
using DES.Framework.Web.Modules;
using DES.Modules.Rules.Contracts.Authorization;
using DES.Modules.Rules.Features;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Persistence;
using DES.Modules.Rules.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

[assembly: DesModule(typeof(DES.Modules.Rules.RulesModule), 770)]

namespace DES.Modules.Rules;

public sealed class RulesModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder); PermissionConstants.Register(RulesPermissions.All);
        builder.Services.AddHeroDbContext<RulesDbContext>(); builder.Services.AddScoped<IDbInitializer, RulesDbInitializer>();
        builder.Services.AddScoped<IRuleEvaluationService, RuleEvaluationService>();
        builder.Services.AddScoped<IRuleEvaluationResultReader, RuleEvaluationResultReader>();
        builder.Services.AddHealthChecks().AddDbContextCheck<RulesDbContext>("db:rules", HealthStatus.Unhealthy);
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints); var versions = endpoints.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).ReportApiVersions().Build();
        RouteGroupBuilder group = endpoints.MapGroup("api/v{version:apiVersion}/rules").WithTags("Rules").WithApiVersionSet(versions).RequireAuthorization(); group.MapRules(); group.MapTemplates();
    }
}
