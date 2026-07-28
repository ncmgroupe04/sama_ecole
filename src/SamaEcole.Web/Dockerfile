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

# Alpine plutôt que l'image Debian par défaut : empreinte disque très réduite et bien moins de
# paquets système, donc une surface d'attaque plus étroite pour l'image qui tourne réellement en
# production (les étapes précédentes ne survivent pas au build multi-stage).
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# QuestPDF (reçus, bulletins — AGENTS.md règle #12) s'appuie sur SkiaSharp, qui délègue le rendu du
# texte à fontconfig/freetype et à une police système : contrairement à l'image Debian, l'Alpine de
# base n'embarque ni l'un ni l'autre, et la génération de PDF échouerait silencieusement (texte vide
# ou exception au premier document généré). icu-libs : composants Unicode complets, pour rester au
# plus près du comportement de l'image Debian précédente plutôt que de basculer en mode Invariant.
#
# ttf-liberation : le bulletin (ReportCardDocument) demande la police « Times New Roman », polie
# propriétaire Microsoft absente de tout dépôt Linux. Liberation Serif en est le clone À MÉTRIQUES
# IDENTIQUES (mêmes largeurs de caractère, mêmes sauts de ligne) — la substitution standard sur Linux
# (LibreOffice, etc.). L'alias fontconfig ci-dessous fait que demander « Times New Roman » dans le
# code renvoie Liberation Serif ici, sans rien changer côté application ni en développement (Windows,
# où la vraie Times New Roman est déjà installée).
# /etc/fonts/local.conf : hook d'override standard, déjà inclus par le fonts.conf par défaut du
# paquet fontconfig (`<include ignore_missing="yes">local.conf</include>`) — pas besoin d'y toucher.
RUN apk add --no-cache fontconfig freetype ttf-dejavu ttf-liberation icu-libs \
    && printf '%s\n' \
        '<?xml version="1.0"?>' \
        '<!DOCTYPE fontconfig SYSTEM "fonts.dtd">' \
        '<fontconfig>' \
        '  <match target="pattern">' \
        '    <test name="family"><string>Times New Roman</string></test>' \
        '    <edit name="family" mode="assign" binding="strong"><string>Liberation Serif</string></edit>' \
        '  </match>' \
        '</fontconfig>' \
        > /etc/fonts/local.conf

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SamaEcole.Web.dll"]
