# SAMA ECOLE

# VOLUME 5 — UI/UX Design Specification (UDS)

**Version :** 2.0
**Statut :** Document de référence — remplace la version 1.0
**Changement principal :** le frontend cible est **exclusivement le web responsive** (ASP.NET Core MVC + Razor, Volume 0/2). Toute mention antérieure de WPF, .NET MAUI, Avalonia ou Blazor Hybrid — qui présupposait une application desktop installée — est retirée : Sama Ecole est consulté depuis un navigateur, sur ordinateur, tablette ou smartphone.

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

**Implémentation technique : Tailwind CSS** (décision D-13, Volume 0 §0.13). La palette, la typographie et les points de rupture ci-dessous doivent être déclarés une seule fois dans `tailwind.config.js` (thème étendu) et jamais recopiés en valeurs brutes dans les vues Razor — un agent qui a besoin de la couleur "Bleu institutionnel" utilise la classe utilitaire correspondante (ex. `bg-primary`), jamais un code hexadécimal en dur. Voir `docs/REPO_STRUCTURE.md` pour l'emplacement du fichier de configuration dans `SamaEcole.Web`.

### 2.1 Palette de couleurs

**Mise à jour** : la couleur principale passe du bleu institutionnel au **violet/indigo**, pour correspondre exactement à `docs/design-references/dashboard-reference.jpg` (référence qui fait foi, voir `docs/design-references/README.md`).

| Couleur | Usage |
|---|---|
| Violet/Indigo `#6366F1` (principale) | Menus, boutons principaux, liens, en-têtes de tableau, logo |
| Vert | Validation, succès, paiement effectué, abonnement actif |
| Orange | Avertissements, échéances proches, paiement partiel |
| Rouge | Erreurs, suppression, abonnement expiré/restreint, impayés |
| Gris | Textes secondaires, séparateurs, arrière-plans |

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

Logo, nom de l'école active, année scolaire active, utilisateur connecté, notifications, recherche globale, bouton de déconnexion.

### 3.2 Menu latéral

Modules affichés selon les permissions de l'utilisateur (jamais tous les modules pour tout le monde) : Tableau de bord, Élèves, Inscriptions, Enseignants, Classes, Matières, Présences, Notes, Bulletins, Finance, Rapports, Paramètres, Administration (Super Admin uniquement).

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

Puisque Sama Ecole est en ligne par conception (Volume 0 §0.8), l'interface doit gérer explicitement la qualité de connexion :

- Indicateur discret dans la barre d'état : connecté / connexion instable / hors ligne.
- En cas de coupure courte pendant une saisie : le formulaire reste rempli, une bannière indique « Connexion perdue — nouvel envoi automatique dès la reconnexion », et l'envoi est retenté automatiquement sans perte de saisie.
- En cas de coupure prolongée : message clair invitant à réessayer plus tard, sans donner l'impression d'une erreur applicative.

**Fin du Volume 5.**
