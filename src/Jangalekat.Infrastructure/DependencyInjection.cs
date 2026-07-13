using Jangalekat.Application.Common.Interfaces;
using Jangalekat.Infrastructure.Multitenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Jangalekat.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // TODO (tickets JGK-E02, JGK-G03) : enregistrer ici la génération PDF (QuestPDF)
        // et le service d'envoi d'email (MailKit) une fois les tickets correspondants démarrés.

        return services;
    }
}
