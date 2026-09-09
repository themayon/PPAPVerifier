using DES.Framework.Web.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace DES.Framework.Web.Mediator;

public static class Extensions
{
    public static IServiceCollection
        EnableMediator(this IServiceCollection services, params Assembly[] featureAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

}