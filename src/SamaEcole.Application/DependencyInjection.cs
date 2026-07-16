using System.Reflection;
using FluentValidation;
using SamaEcole.Application.Common.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace SamaEcole.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Ordre significatif : ValidationBehavior D'ABORD, pour qu'une requête mal formée s'arrête
        // avant d'atteindre AuditLoggingBehavior — une erreur de saisie n'est pas une « écriture
        // sensible » au sens du journal d'audit (JGK-H01), seul un Handler effectivement atteint l'est.
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(AuditLoggingBehavior<,>));

        return services;
    }
}
