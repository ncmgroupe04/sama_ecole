# Changelog — Sama Ecole (Unikol)

Format : `MAJEURE.MINEURE.CORRECTIF` (`docs/Volume_9_Deployment_Operations.md` §13). Chaque version
distingue les changements **visibles pour les écoles** des changements **internes**.

## [1.2.0] — 2026-09-26

### Visible pour les écoles

- **Facturation hybride — annuelle ou mensuelle** pour les établissements de la grille tarifaire :
  - **Annuelle** : forfait annuel de la grille.
  - **Mensuelle** : forfait annuel ÷ 12, arrondi au multiple supérieur de 100 FCFA.
  - L'historique des paiements d'abonnement indique la période couverte (Mensuel / Annuel).
- **Refonte de la barre supérieure (topbar)** : horodateur (date et heure, mois abrégé, en gras),
  badge « mode test », affichage de l'année scolaire, et masquage de l'assistant de démarrage une
  fois la progression à 100 %.
- **Corrections des routes et vues partielles** : les pages `/enseignants` et `/cahier-de-texte`
  répondaient en erreur 500 (chemins des vues partielles incorrects) ; elles s'affichent de nouveau.

### Interne

- Numéro de version porté par `SamaEcole.Web.csproj` (`<Version>`), `openapi.yaml` (`info.version`)
  et le nom du cache du service worker (`wwwroot/sw.js`, `samaecole-static-v1.2.0`).
- Test de rendu `PagesRenderTests` : chaque route de page doit renvoyer du HTML.
- Documentation mise à jour : `docs/GUIDE_FONCTIONNEL_MODULES.md` (facturation hybride, topbar,
  volumes horaires, cahier de texte) et aide en ligne.

## [1.1.0]

Version antérieure — voir l'historique Git (`git log v1.0.0-release..`).
