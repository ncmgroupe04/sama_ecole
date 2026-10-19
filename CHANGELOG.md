# Changelog — Sama Ecole (Unikol)

Format : `MAJEURE.MINEURE.CORRECTIF` (`docs/Volume_9_Deployment_Operations.md` §13). Chaque version
distingue les changements **visibles pour les écoles** des changements **internes**.

## [Non publié]

### Visible pour les écoles

- **Contre-passation d'un paiement** (fiche élève, historique des paiements) : passé le délai d'annulation de 48 heures, le
  Directeur peut « contre-passer » un versement. Le reçu est conservé et marqué annulé ; l'écriture inverse est datée
  d'aujourd'hui, donc les journées et périodes déjà arrêtées ne changent pas. Réservée au Directeur.
- **Droit d'annulation de la Finance, réglable** (Paramètres → Autorisations de caisse) : le caissier peut annuler un paiement
  saisi par erreur pendant 48 heures ; le Directeur peut retirer ce droit à tout moment. Ouvert par défaut.
- **Export d'année : les paiements annulés y figurent** (`paiements.csv`) avec leur statut, la date et le motif d'annulation et la
  destination de l'argent, y compris pour un élève ou une inscription supprimés depuis. Colonnes d'origine inchangées.
- **Franco-Arabe — en-tête officiel en arabe** (Paramètres → Intégration étatique, sous l'IA et l'IEF français) : nom de l'établissement,
  Inspection d'Académie et IEF en arabe, destinés aux documents bilingues. Facultatifs ; un champ vide
  laisse la ligne arabe à compléter à la main.
- **Calendrier de fermeture** (Paramètres → Années scolaires) : jours fériés et vacances de l'année,
  avec libellé arabe facultatif. Préparation du cahier de roulement bilingue (Bloc 5) ; ne change pas le
  refus d'appel un jour de repos.

- **Cahier de roulement bilingue** (Surveillance › Cahier de roulement, `/cahier-de-roulement`) : registre d'appel
  mensuel d'une classe, une case par élève et par demi-journée (matin / après-midi). Une saisie du cahier l'emporte
  sur le statut calculé à partir des appels par cours, avec retour possible au calcul ; saisie au clic ou au clavier
  (P, R, J, A), motif et durée du retard par case, enregistrement en un lot. Affichage français, arabe (grille de
  droite à gauche) ou bilingue sur les établissements Franco-Arabes. Statistiques de présence, d'absence et
  d'assiduité par élève, classe et niveau, pour un mois ou une période d'évaluation, avec le taux de couverture
  (part des demi-journées renseignées).
- **Export PDF du cahier de roulement pour l'inspection** : A4 paysage, bandeau officiel (République, Ministère, IA, IEF,
  établissement) en français, en arabe ou bilingue, une grille par mois (légende, totaux et taux de la classe, ligne
  « Absents » par demi-journée), signatures et cachet. Pour une période d'évaluation : une grille par mois, le
  récapitulatif mois par mois et, pour la direction et le secrétariat, la synthèse du niveau. Le Surveillant peut
  exporter. Un nom d'école ou d'inspection non renseigné en arabe laisse une ligne pointillée à compléter à la main.

### Interne

- Migration `AddAllowFinanceToCancelPaymentsToSchoolSettings` : colonne `school_settings.AllowFinanceToCancelPayments`, booléen non nul, défaut vrai (additive ; les écoles existantes gardent le droit actuel de la Finance).

- Migration `AddRollBookEntries` : table `roll_book_entries` (RLS, sans `DELETE` pour le rôle applicatif, une seule
  saisie vivante par élève, jour et demi-journée), inscrite dans `reset_school_data` et `delete_school_year`.
- Migration `AddSchoolClosuresAndArabicSchoolNames` : table `school_closures` (RLS, sans `DELETE` pour le
  rôle applicatif) et trois colonnes nullables sur `schools`. `delete_school_year` supprime désormais les
  fermetures de l'année ; `reset_school_data` est inchangée (elle conserve les années scolaires).

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
