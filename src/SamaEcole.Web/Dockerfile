# syntax=docker/dockerfile:1
#
# IMAGE UNIQUE de SamaEcole.Web. Ce fichier existe en DEUX exemplaires qui doivent rester IDENTIQUES :
#   - `Dockerfile` (racine)          — celui que `gcloud builds submit --tag …` construit par défaut ;
#   - `src/SamaEcole.Web/Dockerfile` — celui que référence docker-compose.yml (service `api`).
# Les deux supposent le CONTEXTE DE BUILD À LA RACINE du dépôt (les COPY sont préfixés par `src/`) :
#   docker build -f src/SamaEcole.Web/Dockerfile .
# Toute modification de l'un doit être reportée sur l'autre — ils avaient divergé (libgdiplus,
# globalisation, liaison du port) depuis e9c2e01, et l'image réellement déployée n'était plus celle
# qu'on testait en local.

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
# ttf-liberation : le bulletin (ReportCardDocument) demande la police « Times New Roman », police
# propriétaire Microsoft absente de tout dépôt Linux. Liberation Serif en est le clone À MÉTRIQUES
# IDENTIQUES (mêmes largeurs de caractère, mêmes sauts de ligne) — la substitution standard sur Linux
# (LibreOffice, etc.). L'alias fontconfig ci-dessous fait que demander « Times New Roman » dans le
# code renvoie Liberation Serif ici, sans rien changer côté application ni en développement (Windows,
# où la vraie Times New Roman est déjà installée).
# /etc/fonts/local.conf : hook d'override standard, déjà inclus par le fonts.conf par défaut du
# paquet fontconfig (`<include ignore_missing="yes">local.conf</include>`) — pas besoin d'y toucher.
#
# PAS de libgdiplus : il n'est utile qu'à System.Drawing.Common, qu'aucun projet de la solution
# n'utilise (QuestPDF → SkiaSharp, QRCoder → PngByteQRCode managé, ClosedXML → SixLabors). L'ajouter
# ne ferait qu'élargir la surface d'attaque de l'image de production.
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

# icu-libs est installé ci-dessus : on sort explicitement du mode « globalisation invariante » (défaut
# des images Alpine), sans quoi les comparaisons et formats dépendants de la culture — dates et
# montants XOF de tous les PDF officiels — retomberaient sur la culture Invariant.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app .

# Contrat de port. Program.cs lit PORT et appelle UseUrls("http://0.0.0.0:$PORT") — une seule source
# de vérité, d'où l'ABSENCE d'ASPNETCORE_URLS ici (deux variables pour la même chose finissaient par
# se contredire). Cloud Run injecte lui-même PORT=8080 et écarte le déploiement si le conteneur
# n'écoute pas dessus ; la valeur posée ici sert au `docker run` local et à docker-compose (5000:8080).
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SamaEcole.Web.dll"]
