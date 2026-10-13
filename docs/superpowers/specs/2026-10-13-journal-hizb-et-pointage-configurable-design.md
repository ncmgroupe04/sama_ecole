# Journal des évaluations Hizb et pointage par sessions configurables — conception

Date : 2026-10-13 · Module : Internat / Daara · Statut : **proposition à valider avant tout code**

Deux limites connues de l'interface sont levées par deux chantiers indépendants, chacun dans sa PR et avec sa migration :

1. **Historique Hizb** : aujourd'hui un seul état courant par (élève, Hizb) ; aucune courbe d'évolution.
2. **Pointage limité** : aujourd'hui un seul appel, celui de la nuit, pour les internes ayant un lit. Ni appel de journée, ni contrôle des repas, ni demi-pensionnaires, ni récapitulatif.

Principe directeur donné par la Direction : **tout est configurable par l'école** (« comme Photoshop ») — l'école active ce qui l'intéresse, rien n'est imposé.

---

## 1. Historique Hizb

### Modèle
Journal **append-only** `student_hizb_evaluations` (tenant + RLS), une ligne par saisie qui change quelque chose :

| Colonne | Rôle |
|---|---|
| `StudentId`, `HizbNumber` | l'élève et le Hizb évalué |
| `QuartersBefore`, `QuartersAfter` | 0 à 4 avant / après la saisie |
| `Rating` | note 1 à 5, nulle à 0 quart (mêmes règles que `StudentHizbStatus`) |
| `TotalQuartersAfter` | total de l'élève après la saisie (0 à 240) : la courbe se lit sans recalcul |
| `EvaluatedAt` | date du serveur (UTC), jamais envoyée par le client |
| `EvaluatedByUserId`, `InstructorId` | qui a saisi, et l'Oustaz de l'élève à ce moment-là |
| `IsBaseline` | ligne de départ créée par la migration |

- Écrit dans **la même transaction** que `UpdateHizbProgressCommand` ; aucune ligne si la saisie ne change ni les quarts ni la note.
- Le rôle applicatif n'a ni `UPDATE` ni `DELETE` sur cette table (journal fiable). Pas de suppression logique.
- **Reprise** : la migration crée une ligne `IsBaseline` par `StudentHizbStatus` existant avec au moins un quart (date = `LastEvaluatedAt`). **Le passé n'est pas reconstituable** : l'historique démarre au déploiement, avec l'état actuel pour point de départ.
- `reset_school_data` et `delete_school_year` (FK Restrict) sont à patcher pour la nouvelle table.

### Lectures (mêmes droits et mêmes portées que le suivi actuel)
- `GET /api/v1/internat/students/{id}/hizb-history?from=&to=` : points de la courbe (date, total de quarts, %) et dernières évaluations. Un Oustaz ne lit que SA Halqa (`HalqaScopeAuthorizer`).
- `GET /api/v1/internat/progress-history?from=&to=&instructorId=` (Directeur, Secrétariat, Surveillant) : progression moyenne par mois, globale ou par Halqa.

### Écrans
- **Ma Halqa** (arabe, RTL) : section « Évolution » sur la fiche d'un élève : courbe + liste des dernières évaluations. Période au choix : 3 mois, 6 mois, 12 mois, depuis le début.
- **Suivi coranique** : graphique « progression moyenne par mois », filtrable par Halqa, même sélecteur de période.
- **Bulletin coranique PDF** : courbe d'évolution et dix dernières évaluations.
- Les alertes de stagnation restent fondées sur la dernière évaluation (inchangées).

### Personnalisation
Période d'affichage choisie par l'utilisateur ; aucun réglage d'école nécessaire.

---

## 2. Pointage par sessions configurables

### Idée
Remplacer « l'appel de nuit » par des **sessions de pointage** que la Direction crée, nomme, active et ordonne. La nuit existante devient la première session.

### Table `attendance_sessions` (par école, configurée par le Directeur)

| Champ | Valeurs |
|---|---|
| `Name` | libre, 60 caractères (« Appel de nuit », « Déjeuner », « Appel de la Halqa »…) |
| `Audience` | `Interns` (internes avec lit, regroupés par pavillon) · `HalfBoarders` (demi-pensionnaires, par classe) · `AllBoarders` (tous les pensionnaires) · `Halqa` (élèves d'un Oustaz) |
| `Days` | jours concernés (défaut : tous) ; un jour hors sélection est signalé, **pas bloquant** |
| `TimeLabel` | repère libre affiché (« 19 h 30 »), purement informatif |
| `IsActive`, `SortOrder` | activation et ordre d'affichage |

- **Modèles proposés en un clic** (que la Direction peut modifier ou ignorer) : Appel de nuit, Appel du matin, Petit-déjeuner, Déjeuner, Dîner, Appel de la Halqa. L'école ne garde que ce qui lui sert, ou crée ses propres sessions.
- Aucune session n'est imposée, sauf **« Appel de nuit »** créée par la migration pour toute école ayant déjà des pointages (comportement actuel conservé à l'identique).

### Enregistrements
La table `boarding_attendances` est **étendue** (pas de nouvelle table) :
- `SessionId` (obligatoire) et `StudentId` ajoutés ; `BoardingEnrollmentId` devient **facultatif** (une session de Halqa ne passe pas par un séjour) ;
- unicité `(StudentId, SessionId, Date)` à la place de `(séjour, date)` ;
- reprise : toutes les lignes existantes → session « Appel de nuit », `StudentId` déduit du séjour.
- Présence = booléen + note (300 caractères), comme aujourd'hui. Un pensionnaire **en sortie** est pré-noté « En permission » (absent) sur toute session de pensionnaires.

### Qui pointe quoi
| Audience | Qui pointe |
|---|---|
| Interns, HalfBoarders, AllBoarders | Directeur, Secrétariat, Surveillant (inchangé) |
| Halqa | l'Oustaz pour SA Halqa (`HalqaScopeAuthorizer`), plus Directeur / Secrétariat / Surveillant |

### Écrans
- **Internat › Pointage** : on choisit la **session**, puis le groupe (pavillon, classe ou Oustaz) et la nuit/le jour ; feuille d'appel et registre PDF existants, par session.
- **Configuration** (Directeur) : liste des sessions, création depuis un modèle ou à la main, activation, ordre, jours, audience. Placée dans l'onglet Pointage de `/internat`.
- **Ma Halqa** : bouton « Faire l'appel » quand une session d'audience Halqa est active (arabe, RTL, tablette).
- Les anciennes routes `attendance` restent valides (sans `sessionId` = « Appel de nuit »).

### Récapitulatif mensuel imprimable
- `GET /api/v1/boarding/attendance/monthly-summary?month=YYYY-MM&sessionId=&groupId=` : par élève, **présents / absents / non pointés / en permission** ; jours pris en compte = ceux où la session a été pointée pour le groupe.
- **PDF** « Récapitulatif mensuel du pointage » (une ou plusieurs sessions au choix, un groupe à la fois), même gabarit que les registres existants ; aucune donnée de santé ni téléphone.
- Utile à la pension (nuits et repas effectifs).

### Personnalisation
Sessions, audiences, jours, ordre, repère horaire, modèles : tout est réglé par l'école. Aucune valeur codée en dur hors les modèles proposés.

---

## 3. Déploiement et risques

- **Deux PR, deux migrations**, dans cet ordre : Journal Hizb (simple, additif), puis Sessions de pointage (modifie une table existante).
- Chaque merge dans `main` **exécute la migration en production** : sauvegarde Cloud SQL avant, `Down()` testé (le retour arrière du pointage rend les lignes hors « Appel de nuit » inaccessibles : à documenter dans le runbook de la PR), échecs de migration relus avant déploiement.
- Tests : validateurs (unitaires), intégration sur PostgreSQL sous le rôle applicatif (RLS, reprise, unicité, portée de l'Oustaz), fonctionnels (rôles, garde de module), JS, recette navigateur Playwright.
- Compatibilité : une instance de l'ancienne version qui écrit pendant la migration ne connaît pas `SessionId` — prévoir une valeur par défaut côté base (session « Appel de nuit ») le temps du déploiement.
- Profil « Daara — Mémorisation » (sans Pédagogie) : l'appel de la Halqa y est le **seul** appel disponible ; il ne dépend pas du module Pédagogie.

## 4. Questions ouvertes (non bloquantes)
1. Faut-il un statut « excusé » en plus de présent / absent ? (proposé : non pour l'instant, la note suffit.)
2. Un récapitulatif par **élève** (fiche individuelle sur l'année) en plus du récapitulatif par groupe ?
3. Alerte aux familles en cas d'absence répétée : hors périmètre de cette conception.
