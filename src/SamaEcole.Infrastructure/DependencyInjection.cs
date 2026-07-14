using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Multitenancy;
using SamaEcole.Infrastructure.Notifications;
using SamaEcole.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SamaEcole.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Authentification (ticket JGK-A04).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        // Mot de passe initial du Directeur (ticket JGK-B01).
        services.AddSingleton<IPasswordGenerator, PasswordGenerator>();

        // ⚠️ N'ENVOIE RIEN : journalise l'e-mail. L'adaptateur SMTP réel (MailKit) est le ticket
        // JGK-G03. À remplacer avant toute exploitation réelle — voir LoggingEmailSender.
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        // TODO (ticket JGK-E02) : enregistrer ici la génération PDF (QuestPDF).

        return services;
    }
}
