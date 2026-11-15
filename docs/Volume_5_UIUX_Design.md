# JANGALEKAT

# VOLUME 5 — UI/UX Design Specification (UDS)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0
**Changement principal :** le frontend cible est **exclusivement le web responsive** (ASP.NET Core MVC + Razor, Volume 0/2). Toute mention antérieure de WPF, .NET MAUI, Avalonia ou Blazor Hybrid — qui présupposait une application desktop installée — est retirée : Jangalekat est consulté depuis un navigateur, sur ordinateur, tablette ou smartphone.

---

## Table des matières

1. Philosophie de l'interface
2. Design system
3. Structure générale de l'application
4. Tableau de bord par rôle
5. Ergonomie des formulaires
6. Ergonomie des tableaux
7. Bibliothèque de composants (référence)
8. Navigation et accessibilité
9. Résilience réseau côté interface

---

## 1. Philosophie de l'interface

L'interface doit être moderne, professionnelle, très rapide, intuitive, et **responsive de la taille smartphone à un écran de bureau 32"**. Un directeur ou un secrétaire doit pouvoir maîtriser l'application après moins d'une heure de prise en main.

Principes UX :
- maximum 3 clics pour toute fonctionnalité courante ;
- informations importantes toujours visibles sans défilement excessif ;
- composants homogènes sur tous les écrans ;
- recherche disponible sur toutes les listes ;
- aucune fenêtre modale inutile ;
- **chargement perçu rapide** : affichage d'un état de chargement squelette (skeleton) plutôt qu'un écran blanc pendant les appels réseau, important sur une connexion mobile sénégalaise moyenne.

## 2. Design system

Design system unique et partagé (couleurs, polices, espacements, icônes, boutons, tableaux, formulaires, cartes) — aucune page ne doit réinventer son propre style.

**Références visuelles exactes** : le reçu d'inscription, le bulletin de notes et le gabarit général de dashboard sont fournis en images dans `docs/design-references/` (+ description détaillée dans `docs/design-references/README.md`) et font foi — à reproduire à l'identique, sans réinterprétation. En cas de divergence entre ces références et les règles génériques ci-dessous, les références l'emportent.

**Implémentation technique : Tailwind CSS** (décision D-13, Volume 0 §0.13). La palette, la typographie et les points de rupture ci-dessous doivent être déclarés une seule fois dans `tailwind.config.js` (thème étendu) et jamais recopiés en valeurs brutes dans les vues Razor — un agent qui a besoin de la couleur "Bleu institutionnel" utilise la classe utilitaire correspondante (ex. `bg-primary`), jamais un code hexadécimal en dur. Voir `docs/REPO_STRUCTURE.md` pour l'emplacement du fichier de configuration dans `Jangalekat.Web`.

### 2.1 Palette de couleurs

**Mise à jour** : la couleur principale passe du bleu institutionnel au **violet/indigo**, pour correspondre exactement à `docs/design-references/dashboard-reference.jpg` (référence qui fait foi, voir `docs/design-references/README.md`).

| Couleur | Usage |
|---|---|
| Violet/Indigo `#6366F1` (principale) | Menus, boutons principaux, liens, en-têtes de tableau, logo |
| Vert | Validation, succès, paiement effectué, abonnement actif |
| Orange | Avertissements, échéances proches, paiement partiel |
| Rouge | Erreurs, suppression, abonnement expiré/restreint, impayés |
| Gris | Textes secondaires, séparateurs, arrière-plans |

### 2.1 ter Menu et modules par profil d'établissement

Le profil choisi à l'Onboarding (`EstablishmentProfilePresets`) règle les quatre interrupteurs de modules ; la sidebar, la barre de raccourcis et les gardes de page les suivent. Le Directeur peut ensuite les modifier dans Paramètres › Modules (le profil ne les écrase qu'à sa propre sélection).

| Profil | Pédagogie | Finance | Internat (+ Halqas, suivi coranique, Oustaz) | Coran |
|---|---|---|---|---|
| Comptabilité & Rapports | non | oui | non | non |
| Élémentaire / Primaire, Enseignement Général | oui | oui | non | non |
| Franco-Arabe (hybride) | oui | oui | **oui** | oui |
| Internat / Daara Moderne | oui | oui | oui | oui |
| **Daara — Mémorisation** | **non** | oui | oui | oui |

- **Pédagogie coupée** masque Notes, Examens officiels, Programmes, Cahier de texte, Matières & Coefficients, rapports pédagogiques et toute la Surveillance générale ; l'onglet « Notes & bulletins » de la fiche élève disparaît aussi. Les pages concernées portent `[PageModule(SchoolModule.Pedagogy)]` (redirection ergonomique) ; chaque API répond déjà `403 MODULE_DISABLED`.
- Les Halqas, le suivi coranique et les Oustaz suivent l'interrupteur **Internat** (décision G-D1). Un profil généraliste qui active Internat à la main les voit donc aussi ; rattacher ces écrans au module Coran est un chantier distinct.
- Changer le préréglage d'un profil n'affecte que les écoles qui le choisissent ensuite : les réglages existants ne sont pas réécrits.
### 2.1 bis Mode sombre

Le thème (clair / sombre / système) se choisit dans le menu de la carte de profil (« Apparence ») et se mémorise dans le navigateur (`localStorage` « sama-theme »). Par défaut : clair (`DEFAULT` dans `theme-boot.js` et `theme.js` — passer à `system` pour suivre le thème du système).

- **Aucune classe `dark:` dans les vues.** Les vues écrivent toujours `bg-white`, `text-slate-700`, `border-slate-200`… ; `tailwind.dark-theme.js` redéfinit la valeur de ces classes en sombre (variables CSS échangées par la classe `.dark` posée sur `<html>`). Les opacités (`bg-white/85`) fonctionnent.
- **Règle des trois jeux** : *fond* (`bg`, dégradés, `fill`) — les teintes claires 50–300 deviennent sombres, les aplats 600+ restent (un bouton `bg-emerald-600 text-white` reste plein) ; *encre* (`text`, `placeholder`) — les teintes foncées 600–900 deviennent claires ; *trait* (`border`, `divide`, `ring`, `stroke`).
- **Surfaces** : page `bg-*-50` → slate-950, carte `bg-white` → slate-900, fond discret `bg-*-100` → slate-800. Toutes les familles neutres (slate, gray, zinc, stone) visent l'échelle slate.
- Jetons de statut : `success` / `warning` / `danger` gardent leur aplat, leur texte s'éclaircit et leur fond `-bg` devient une teinte sombre.
- Hors périmètre : la console super-admin (déjà sombre) et les pages publiques (`_LayoutPublic`, `_AuthLayout`), qui ne chargent pas `theme-boot.js`.
- Écran qui ne passe pas bien en sombre (couleur écrite en dur, graphique) : corriger la vue en utilisant une classe de palette plutôt qu'un hexadécimal, ou ajouter un `dark:` ponctuel.

### 2.2 Typographie

Police : **Inter** (ou Segoe UI en repli système). Titre 22px, sous-titre 18px, texte courant 14px, tableaux 13px.

### 2.3 Icônes

Bibliothèque unique (Material Symbols ou Fluent UI System Icons) — pas de mélange de plusieurs jeux d'icônes.

### 2.4 Points de rupture responsive

| Point de rupture | Largeur | Comportement |
|---|---|---|
| Mobile | < 600px | Menu latéral masqué (accessible via bouton hamburger), tableaux en cartes empilées |
| Tablette | 600–1024px | Menu latéral repliable, tableaux compacts |
| Desktop | > 1024px | Menu latéral fixe, disposition complète |

---

## 3. Structure générale de l'application

```
┌───────────────────────────────────────────────┐
│ Barre supérieure                               │
├──────────────┬────────────────────────────────┤
│ Menu latéral │ Zone principale                 │
│ (repliable)  │                                 │
├──────────────┴────────────────────────────────┤
│ Barre d'état                                   │
└───────────────────────────────────────────────┘
```

### 3.1 Barre supérieure

Barre **blanche** (`bg-white`, filet bas), **une seule rangée de 64 px** (refonte du 12/11/2026). **Groupe gauche** : bouton d'ouverture du menu (mobile uniquement — le bouton de réduction de la barre latérale vit dans l'en-tête du menu), puis, dès `lg`, la navigation rapide entre modules (`_QuickNav`, onglets marine soulignés, sans icône sous 1750 px, texte réduit sous 1500 puis 1320 px). Le titre de l'écran n'est plus affiché (il reste dans le titre accessible `sr-only`). **Groupe droit** (`shrink-0`), dans l'ordre :

| Élément | Comportement |
|---|---|
| Sélecteur d'établissement | Uniquement pour un compte rattaché à plusieurs écoles (groupe scolaire) ; masqué sous `sm`. |
| Pastille de régime | **Mode test** : badge orange **compact** (`px-2.5 py-1.5 text-xs`), libellé « MODE TEST » ; le complément (« : Cliquez ici pour passer en Mode Réel » pour le Directeur, « : données d'essai uniquement » pour les autres rôles) n'apparaît qu'au-dessus de 2000 px (`.sandbox-suffix`). Cliquable pour le seul Directeur (Paramètres système, ancre `#mode-reel`). **Mode réel** : pastille « Mode réel », masquée sous `sm`. |
| Assistant « Démarrage » | Directeur et Secrétariat seulement. Pastille avec pourcentage d'avancement, icône + pourcentage sous 1500 px (`.setup-assistant-label` masqué). **À 100 % (`allDone`), le bouton disparaît** (`x-show="!allDone"`) ; la modale reste dans le DOM. S'ouvre seul à la première connexion. |
| Année scolaire active | Rétablie le 15/11/2026 (JGK-C01) : pastille grise avec le millésime (« 2026-2027 »), visible sur chaque écran dès `sm`. Cliquable pour le seul Directeur (vers `/annees-scolaires`, Paramètres › Années), informative pour les autres rôles. Sous 1500 px, l'icône est masquée et le remplissage réduit. |
| Salutation | Selon l'heure, sans donnée personnelle ; masquée sous `sm` et sous 2100 px (elle cède sa place à l'année active). |
| « Comment faire ? » | Bouton jaune (`.btn-help`), visible dès `lg` : ouvre l'aide contextuelle de l'écran. |

Retirés de la barre : l'**horodateur** (refonte du 12/11/2026 ; la pastille d'année scolaire active, retirée à cette occasion, a été **rétablie** le 15/11/2026), ainsi que, depuis le 26/08/2026, le badge de connectivité (la barre d'état en bas d'écran porte seule cette information, §9), le raccourci « Aide » (menu latéral) et le bouton de déconnexion (carte de profil du menu latéral). La recherche globale n'est pas livrée.

### 3.2 Menu latéral

Modules affichés selon les permissions de l'utilisateur (jamais tous les modules pour tout le monde) : Tableau de bord, Élèves, Inscriptions, Enseignants, Classes, Matières, Présences, Notes, Bulletins, Finance, Rapports, Paramètres, Administration (Super Admin uniquement).

**Réduction (26/08/2026).** Un bouton dans la barre supérieure (desktop uniquement, état persisté côté navigateur) réduit le menu à ses icônes seules — libellés, chevrons et sous-menus masqués. Les pôles à sous-menus (Gestion Scolaire, Surveillance, Comptabilité) révèlent leurs liens dans un tiroir flottant au survol de l'icône, plutôt que par l'accordéon habituel au clic, le libellé n'ayant plus la place de s'afficher en ligne. La carte de profil en bas du menu (e-mail et rôle sur une ligne, bouton Déconnexion sur la seconde) se réduit à l'avatar seul dans cet état.

> **Règle de sécurité (non négociable, voir AGENTS.md et Volume 7 §9)** : masquer un onglet dans le menu est une commodité d'ergonomie, **jamais une mesure de sécurité**. Chaque route API correspondante doit refuser l'accès indépendamment de l'affichage du menu (`[Authorize(Roles=...)]` côté backend) — un utilisateur qui devinerait ou taperait directement une URL non affichée dans son menu doit recevoir un `403`, pas seulement ne pas voir le lien.

### 3.3 Barre d'état

Affiche : état de la connexion réseau (voir §9), statut de l'abonnement de l'école, version de l'application, utilisateur, heure locale.

> La mention « connexion à la base de données » et « sauvegarde automatique » affichée en barre d'état dans une version antérieure de ce document n'a plus de sens côté client : ce sont des préoccupations serveur (Volume 9), invisibles et non pertinentes pour l'utilisateur final d'une plateforme en ligne.

---

## 4. Tableau de bord par rôle

| Rôle | Indicateurs affichés |
|---|---|
| **Directeur** | Effectifs élèves/enseignants, recettes du mois, dépenses, impayés, évolution financière, taux de réussite, alertes, statut de l'abonnement |
| **Secrétariat** | Nouvelles inscriptions, réinscriptions, dossiers incomplets, classes complètes/liste d'attente |
| **Finance** | Recettes, dépenses, impayés, paiements du jour, taux de recouvrement |
| **Enseignant** | Classes et matières assignées, évaluations à saisir, bulletins à préparer, statistiques de présence |
| **Super Admin** | Écoles actives/suspendues, abonnements arrivant à expiration, indicateurs de santé plateforme |

---

## 5. Ergonomie des formulaires

Disposition homogène sur tous les formulaires :

```
Nom          [__________]
Prénom       [__________]
Date de naissance [__/__/____]
Téléphone    [__________]
Classe       [▼]

[ Enregistrer ]   [ Annuler ]
```

Règles obligatoires :
- navigation clavier complète (Tab, Entrée, Échap) ;
- validation en temps réel des champs (avant soumission) ;
- indication visuelle claire des erreurs, à côté du champ concerné ;
- conservation des données saisies en cas d'erreur serveur ou de coupure réseau courte (voir §9) — jamais de formulaire vidé après une erreur.

---

## 6. Ergonomie des tableaux

Toutes les listes partagent le même modèle : colonnes configurables (afficher/masquer, réorganiser), tri, recherche instantanée, filtres multiples, pagination, export PDF/Excel, impression.

---

## 7. Bibliothèque de composants (référence)

Composants réutilisables devant être développés une seule fois et partagés : bouton (primaire/secondaire/danger), champ de saisie, sélecteur, tableau de données, carte statistique, boîte de dialogue de confirmation, notification toast, graphique (Chart.js), badge de statut (coloré selon §2.1), indicateur de chargement squelette.

Chaque composant doit documenter ses états : par défaut, survol, focus, désactivé, chargement, erreur.

---

## 8. Navigation et accessibilité

- Fil d'Ariane sur les écrans à plus de deux niveaux de profondeur.
- Contraste de couleur conforme WCAG AA a minima pour le texte courant.
- Tout élément interactif atteignable au clavier.
- Libellés explicites sur les icônes seules (attribut `title`/`aria-label`).

---

## 9. Résilience réseau côté interface

Puisque Jangalekat est en ligne par conception (Volume 0 §0.8), l'interface doit gérer explicitement la qualité de connexion :

- Indicateur discret dans la barre d'état : connecté / connexion instable / hors ligne.
- En cas de coupure courte pendant une saisie : le formulaire reste rempli, une bannière indique « Connexion perdue — nouvel envoi automatique dès la reconnexion », et l'envoi est retenté automatiquement sans perte de saisie.
- En cas de coupure prolongée : message clair invitant à réessayer plus tard, sans donner l'impression d'une erreur applicative.

**Fin du Volume 5.**
