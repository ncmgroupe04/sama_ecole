using System.Reflection;
using FluentValidation;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common.Behaviors;
using SamaEcole.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace SamaEcole.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        // Portée de saisie de l'appel (ticket JGK-D06) : partagée par l'initialisation et la
        // soumission d'une fiche de présence. Scoped — elle lit le tenant/compte de la requête courante.
        services.AddScoped<AttendanceScopeAuthorizer>();

        // Agrégation d'assiduité (tickets JGK-R02/R03) : partagée par le rapport paginé et l'export
        // de fichier. Scoped — elle lit sous la RLS de la requête courante.
        services.AddScoped<AttendanceReportAggregator>();

        // Ordre significatif : ValidationBehavior D'ABORD, pour qu'une requête mal formée s'arrête
        // avant d'atteindre AuditLoggingBehavior — une erreur de saisie n'est pas une « écriture
        // sensible » au sens du journal d'audit (JGK-H01), seul un Handler effectivement atteint l'est.
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(AuditLoggingBehavior<,>));

        return services;
    }
}
