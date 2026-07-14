# syntax=docker/dockerfile:1

# --- Étape 1 : compilation du CSS Tailwind (Décision D-13, Volume 0 §0.13) ---
FROM node:20-alpine AS css-build
WORKDIR /src/web
COPY src/SamaEcole.Web/package.json src/SamaEcole.Web/tailwind.config.js ./
COPY src/SamaEcole.Web/Styles ./Styles
COPY src/SamaEcole.Web/Views ./Views
RUN npm install
RUN npm run build:css

# --- Étape 2 : build .NET ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY SamaEcole.sln .
COPY src/SamaEcole.Domain/SamaEcole.Domain.csproj src/SamaEcole.Domain/
COPY src/SamaEcole.Application/SamaEcole.Application.csproj src/SamaEcole.Application/
COPY src/SamaEcole.Infrastructure/SamaEcole.Infrastructure.csproj src/SamaEcole.Infrastructure/
COPY src/SamaEcole.Persistence/SamaEcole.Persistence.csproj src/SamaEcole.Persistence/
COPY src/SamaEcole.Web/SamaEcole.Web.csproj src/SamaEcole.Web/
RUN dotnet restore src/SamaEcole.Web/SamaEcole.Web.csproj

COPY src/ src/
# Le CSS compilé à l'étape 1 remplace le placeholder avant publication.
COPY --from=css-build /src/web/wwwroot/css/site.css src/SamaEcole.Web/wwwroot/css/site.css
RUN dotnet publish src/SamaEcole.Web/SamaEcole.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SamaEcole.Web.dll"]
