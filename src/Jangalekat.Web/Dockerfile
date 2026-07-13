# syntax=docker/dockerfile:1

# --- Étape 1 : compilation du CSS Tailwind (Décision D-13, Volume 0 §0.13) ---
FROM node:20-alpine AS css-build
WORKDIR /src/web
COPY src/Jangalekat.Web/package.json src/Jangalekat.Web/tailwind.config.js ./
COPY src/Jangalekat.Web/Styles ./Styles
COPY src/Jangalekat.Web/Views ./Views
RUN npm install
RUN npm run build:css

# --- Étape 2 : build .NET ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Jangalekat.sln .
COPY src/Jangalekat.Domain/Jangalekat.Domain.csproj src/Jangalekat.Domain/
COPY src/Jangalekat.Application/Jangalekat.Application.csproj src/Jangalekat.Application/
COPY src/Jangalekat.Infrastructure/Jangalekat.Infrastructure.csproj src/Jangalekat.Infrastructure/
COPY src/Jangalekat.Persistence/Jangalekat.Persistence.csproj src/Jangalekat.Persistence/
COPY src/Jangalekat.Web/Jangalekat.Web.csproj src/Jangalekat.Web/
RUN dotnet restore src/Jangalekat.Web/Jangalekat.Web.csproj

COPY src/ src/
# Le CSS compilé à l'étape 1 remplace le placeholder avant publication.
COPY --from=css-build /src/web/wwwroot/css/site.css src/Jangalekat.Web/wwwroot/css/site.css
RUN dotnet publish src/Jangalekat.Web/Jangalekat.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Jangalekat.Web.dll"]
