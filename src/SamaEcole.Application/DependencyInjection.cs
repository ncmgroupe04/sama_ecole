using System.Reflection;
using FluentValidation;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.ClassJournal;
using SamaEcole.Application.Common.Behaviors;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.Notifications;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
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

        // Contrôle de propriété des créneaux d'emploi du temps : partagé par la création, la
        // modification et la suppression. Scoped — il lit le compte de la requête courante.
        services.AddScoped<ScheduleOwnershipAuthorizer>();

        // Portée d'écriture du cahier de texte (ticket JGK-P04) : partagée par la création, la
        // modification et la suppression d'entrées de journal. Scoped — elle lit le compte de la
        // requête courante.
        services.AddScoped<ClassJournalScopeAuthorizer>();

        // Portée de lecture des dossiers d'examen (ticket JGK-J08) : partagée par la liste et la fiche
        // détaillée. Scoped — elle lit le compte de la requête courante.
        services.AddScoped<ExamDossierScopeAuthorizer>();

        // Agrégation d'assiduité (tickets JGK-R02/R03) : partagée par le rapport paginé et l'export
        // de fichier. Scoped — elle lit sous la RLS de la requête courante.
        services.AddScoped<AttendanceReportAggregator>();

        // Calcul du ReportCardDto d'un élève (JGK-G03) : partagé par le bulletin individuel et les
        // bulletins de classe (ZIP, PDF fusionné). Scoped — elle lit sous la RLS de la requête courante.
        services.AddScoped<ReportCardDataService>();

        // Point de passage unique de tout SMS sortant (formule, commutateur d'école, solde,
        // historique) — voir SmsDispatcher. Scoped : il écrit sous la RLS de la requête courante.
        services.AddScoped<ISmsDispatcher, SmsDispatcher>();

        // Dépilage de la file (voir SmsQueueProcessor). Scoped comme le store dont il dépend : le
        // service hébergé ouvre une portée par tour plutôt que de retenir un DbContext à vie.
        services.AddScoped<ISmsQueueProcessor, SmsQueueProcessor>();

        // Ordre significatif : ValidationBehavior D'ABORD, pour qu'une requête mal formée s'arrête
        // avant d'atteindre AuditLoggingBehavior — une erreur de saisie n'est pas une « écriture
        // sensible » au sens du journal d'audit (JGK-H01), seul un Handler effectivement atteint l'est.
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(AuditLoggingBehavior<,>));

        return services;
    }
}
