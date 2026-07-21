# syntax=docker/dockerfile:1

# Image de l'outil de migration — volontairement SÉPARÉE de celle du web.
#
# Elle seule reçoit les identifiants du rôle propriétaire (sama_ecole) au déploiement. Le conteneur
# vit le temps d'appliquer les migrations puis s'arrête ; le conteneur web, lui, tourne en continu
# avec le seul rôle applicatif. C'est cette séparation qui permet de respecter AGENTS.md règle #2 :
# PostgreSQL exempte le propriétaire d'une table de ses policies RLS, et faire tourner le web avec ce
# rôle désactiverait l'isolation multi-tenant en silence (RlsGuard refuse d'ailleurs de démarrer).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY SamaEcole.sln .
COPY src/SamaEcole.Domain/SamaEcole.Domain.csproj src/SamaEcole.Domain/
COPY src/SamaEcole.Application/SamaEcole.Application.csproj src/SamaEcole.Application/
COPY src/SamaEcole.Persistence/SamaEcole.Persistence.csproj src/SamaEcole.Persistence/
COPY tools/SamaEcole.Tools/SamaEcole.Tools.csproj tools/SamaEcole.Tools/
RUN dotnet restore tools/SamaEcole.Tools/SamaEcole.Tools.csproj

COPY src/ src/
COPY tools/ tools/
RUN dotnet publish tools/SamaEcole.Tools/SamaEcole.Tools.csproj -c Release -o /app --no-restore

# `runtime` et non `aspnet` : c'est une commande console, aucun serveur HTTP n'est hébergé. Ni
# fontconfig ni les polices de l'image web ne sont nécessaires — cet outil ne génère aucun PDF.
FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine AS runtime
WORKDIR /app

# icu-libs : Npgsql et EF Core s'appuient sur les comparaisons de chaînes culture-aware. Sans ICU,
# l'image Alpine bascule en mode Invariant, dont le comportement diffère subtilement de celui de
# l'environnement de développement — même raisonnement que le Dockerfile du web.
RUN apk add --no-cache icu-libs

COPY --from=build /app .

# Aucune valeur par défaut pour ConnectionStrings__Migrations : la commande échoue explicitement si
# elle n'est pas fournie (MigrateCommand.ResolveConnectionString), plutôt que de migrer une base
# inattendue. Le déploiement doit l'injecter, et s'arrêter si le code retour est non nul.
ENTRYPOINT ["dotnet", "SamaEcole.Tools.dll"]
CMD ["migrate"]
