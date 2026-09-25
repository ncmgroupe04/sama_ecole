# Guide fonctionnel des modules — Sama Ecole (Unikol)

**Date de rédaction :** 20/09/2026
**Statut :** synthèse de référence, à relire à chaque évolution significative d'un module.

Ce guide documente **chaque écran de la navigation principale**, un par un — pas de regroupement
éditorial : un utilisateur qui se trouve sur une page précise de la plateforme doit pouvoir chercher
cette page par son nom exact et trouver directement sa réponse.

**Sources.** Ce document ne fait que synthétiser ce qui est déjà spécifié ailleurs — il n'introduit
aucune règle nouvelle. En cas de divergence, les documents sources font foi :
- Règle métier / exigence fonctionnelle exacte → `docs/Volume_1_Cahier_des_Charges.md`
- Route API, format de requête/réponse → `docs/Volume_4_API_Design.md`
- Permission, rôle, sécurité → `docs/Volume_7_Security.md`
- Ce qui est livré / hors périmètre V1 → `ACTIVE_CONTEXT.md`

**Convention de lecture.** Chaque module suit la même structure en 5 points : nom et route, objectif,
utilisateurs cibles, fonctionnalités clés & règles métier, flux d'utilisation typique.

---

## Table des matières

**Tableau de bord**
1. [Tableau de bord](#1-tableau-de-bord)

**Gestion Scolaire**
2. [Élèves](#2-élèves)
3. [Inscriptions](#3-inscriptions)
4. [Classes](#4-classes)
5. [Infrastructures](#5-infrastructures)
6. [Inventaire](#6-inventaire)
7. [Internat](#7-internat)
8. [Matières](#8-matières)
9. [Enseignants](#9-enseignants)
10. [Notes et bulletins](#10-notes-et-bulletins)
11. [Cahier de texte](#11-cahier-de-texte)
12. [Examens officiels](#12-examens-officiels)
13. [Intégration étatique](#13-intégration-étatique)
14. [Rapports d'assiduité](#14-rapports-dassiduité)

**Surveillance**
15. [Présences](#15-présences)
16. [Billets](#16-billets)
17. [Discipline](#17-discipline)
18. [Convocations](#18-convocations)
19. [Pointage des enseignants](#19-pointage-des-enseignants)

**Comptabilité**
20. [Caisse](#20-caisse)
21. [Frais](#21-frais)
22. [Paie](#22-paie)
23. [Fiscalité](#23-fiscalité)
24. [Trésorerie](#24-trésorerie)
25. [Rapports financiers](#25-rapports-financiers)

**Administration**
26. [Paramètres](#26-paramètres)
27. [Console Super Admin](#27-console-super-admin)
28. [Aide & Documentation](#28-aide--documentation)

---

## 1. Tableau de bord

**Route :** `/tableau-de-bord`

### Objectif / Rôle principal
Vue de pilotage d'ensemble de l'établissement à la connexion : effectifs, indicateurs RH, présence,
état de l'abonnement — un coup d'œil qui n'appartient à aucun module en particulier.

### Utilisateurs cibles
Directeur et Super Admin toujours ; Finance également, sauf pour un établissement public (dont le
volet Finance est en général vide, §7.1bis).

### Fonctionnalités clés & règles métier
- Agrège plusieurs modules en une seule vue : effectifs par classe/niveau, indicateurs Paie/RH,
  synthèse de présence, statut de l'abonnement (alertes d'expiration, §11.2).
- Réservé au Directeur (+ Super Admin) : c'est une vue de **pilotage** de l'établissement, plus large
  que le tableau de bord financier (§7.5, réservé Directeur+Finance) — les deux coexistent
  délibérément, sans se remplacer.
- Purement en lecture : aucune saisie ne se fait depuis cet écran.

### Flux d'utilisation typique
1. Le Directeur se connecte et atterrit sur ce tableau de bord.
2. Il repère en un coup d'œil une alerte (abonnement proche de l'expiration, effectif en baisse).
3. Il navigue vers le module concerné pour agir — le tableau de bord lui-même ne modifie rien.

---

## 2. Élèves

**Route :** `/eleves`

### Objectif / Rôle principal
Référentiel central des élèves de l'établissement : fiche d'identité, parcours scolaire, notes,
paiements, présence — le point d'entrée vers tout ce qui concerne un élève donné.

### Utilisateurs cibles
Super Admin, Directeur, Secrétariat, Finance, Enseignant (tous en lecture sur la fiche ; l'écriture
suit la matrice de rôles du module d'origine de chaque champ).

### Fonctionnalités clés & règles métier
- **Matricule généré uniquement à l'enregistrement**, jamais à l'ouverture du formulaire, dans la même
  transaction que la création de l'élève (règle #3 d'`AGENTS.md`, §2.1) — format
  `ELEV-{AnnéeScolaire}-{Séquence sur 4 chiffres}`, personnalisable par établissement.
- Fiche détaillée (« Voir le profil ») : photo, matricule, informations personnelles, classe actuelle
  **et** historique des classes, notes et bulletins, historique des paiements et solde, absences (§4.1).
- Import massif depuis Excel/CSV : aperçu avant écriture, détection de doublons (nom + prénom + date de
  naissance), signalement des lignes en erreur, réimport ciblé, rapport d'import téléchargeable (§3).
- Aucune suppression physique : un élève retiré l'est par suppression logique (règle #6).

### Flux d'utilisation typique
1. Le Secrétariat crée ou importe une fiche élève.
2. Le matricule est attribué à l'enregistrement.
3. La fiche s'enrichit au fil de l'année (classe, notes, paiements, absences) au gré des autres
   modules — elle en devient le miroir de synthèse.
4. N'importe quel rôle habilité consulte la fiche pour répondre à une question (« quel est le solde de
   cet élève ? », « dans quelle classe est-il ? »).

---

## 3. Inscriptions

**Route :** `/inscriptions`

### Objectif / Rôle principal
Constituer le dossier d'un élève pour une année scolaire (pré-inscription, inscription définitive,
réinscription) et figer le montant dû transmis à la Finance — **jamais** encaisser (l'encaissement se
fait exclusivement depuis Caisse, §20).

### Utilisateurs cibles
Directeur, Secrétariat.

### Fonctionnalités clés & règles métier
- **Pré-inscription** : dossier provisoire avec réservation de place, converti en inscription
  définitive une fois le dossier complet (§6.1).
- **Réinscription annuelle** : reconduit un élève existant vers l'année suivante en conservant
  l'intégralité de son historique scolaire (§6.2).
- **Contrôle des places** : capacité, effectif inscrit et places restantes affichés par classe ; une
  classe complète bascule automatiquement l'inscription en **liste d'attente**, avec notification dès
  qu'une place se libère (§6.3).
- **Gestion documentaire** : suivi des pièces (extrait de naissance, certificat de scolarité, certificat
  de transfert, photos, autorisation parentale), statut de dossier Complet/Incomplet/En attente (§6.4).
- **Documents générés** : fiche d'inscription et reçu d'inscription au format exact de
  `docs/design-references/receipt-reference.png` (§6.5, §7.3bis).
- **Inscription = calcul seul, jamais d'encaissement** (arbitrage du 30/08/2026) : l'écran fige le dû
  annuel et ne crée jamais de `Payment` — un simulateur de mensualités purement indicatif est proposé,
  sans appel réseau. Toute la case « ce frais est réglé » a été retirée : le premier encaissement, comme
  les suivants, se fait exclusivement sur `/caisse`.

### Flux d'utilisation typique
1. Le Secrétariat ouvre une pré-inscription et réserve une place dans une classe.
2. Il complète le dossier (pièces, informations) jusqu'au statut Complet.
3. Il convertit le dossier en inscription définitive : le montant dû annuel est figé et transmis à la
   Finance.
4. La fiche/reçu d'inscription est imprimée.
5. L'encaissement effectif du premier versement se fait ensuite sur `/caisse` — jamais depuis cet écran.

---

## 4. Classes

**Route :** `/classes`

### Objectif / Rôle principal
Référentiel des classes et niveaux de l'établissement, structure dont dépendent Finance, Bulletins et
statistiques.

### Utilisateurs cibles
Directeur, Secrétariat, Enseignant (lecture pour ses classes assignées).

### Fonctionnalités clés & règles métier
- **Aucun niveau ni classe codé en dur** : chaque établissement crée librement sa nomenclature (TPS,
  PS… ou 3ème S, Terminale L2…) — toute fonctionnalité qui en dépend s'y réfère dynamiquement (§5.1).
- Recherche par classe, niveau, enseignant responsable ; affichage de l'effectif total, garçons, filles ;
  impression de listes de classe (§5.2).
- **Classes passerelles / accélérées** (option désactivée par défaut) : une classe peut valider deux
  niveaux en une année (« CI-CP », « 6e-5e ») pour les parcours d'intégration — `TargetLevel` reste un
  libellé, jamais un identifiant de niveau codé en dur. La délibération d'un élève admis y valide les
  deux niveaux.

### Flux d'utilisation typique
1. Le Directeur crée les niveaux et classes de son établissement en début d'année.
2. Le Secrétariat y rattache les élèves via les Inscriptions.
3. Les autres modules (Finance, Notes, Emploi du temps) se réfèrent à cette même liste de classes.

---

## 5. Infrastructures

**Route :** `/infrastructures`

### Objectif / Rôle principal
Référentiel des bâtiments et salles de l'établissement — support descriptif pour l'Emploi du temps
(§19) et l'Internat (§7 de ce guide).

### Utilisateurs cibles
Lecture ouverte à tous les rôles de l'établissement (un enseignant doit connaître les salles pour lire
son emploi du temps) ; écriture réservée à Directeur et Secrétariat (§19.2).

### Fonctionnalités clés & règles métier
- Bâtiments et salles qu'ils contiennent (nom, capacité) — suppression **logique** uniquement (§19.1).
- Module facultatif : un établissement qui n'a rien renseigné voit une liste vide, jamais une erreur
  (§19.2).
- **La capacité d'une salle est purement descriptive** : saisie et affichée, mais jamais confrontée à un
  effectif, jamais source d'alerte automatique. Le contrôle des places reste au niveau de la classe
  (§6.3), pas de la salle.

### Flux d'utilisation typique
1. Le Directeur ou le Secrétariat déclare les bâtiments, puis les salles de chacun.
2. Ces salles deviennent sélectionnables dans l'Emploi du temps et, si l'Internat est activé, dans les
   dortoirs.
3. Tout rôle de l'établissement peut ensuite consulter cette liste en lecture seule.

---

## 6. Inventaire

**Route :** `/inventaire`

### Objectif / Rôle principal
Suivi du patrimoine de l'établissement — mobilier et matériel des écoles publiques comme privées —, du
journal de mouvements de stock, et des prêts de matériel avec décharge.

### Utilisateurs cibles
Directeur, Secrétariat, Surveillant, Enseignant.

### Fonctionnalités clés & règles métier
- Quatre onglets : **Catalogue**, **Catégories**, **Mouvements de stock**, **Prêts & Décharges**.
- **Un bien est un LOT, pas une unité** — le suivi à l'unité est un lot de quantité 1 ; `Condition` est
  l'état **dominant** du lot (un état par unité obligerait à scinder en deux lots).
- **`QuantityAvailable` n'est jamais écrit directement par un endpoint** : elle ne varie que dans la
  transaction d'un mouvement de stock, sous verrouillage optimiste (`xmin`, règle #5 d'`AGENTS.md`).
- **`stock_movements` est append-only** : le rôle applicatif n'y a que `SELECT, INSERT` — une erreur de
  saisie se corrige par un mouvement inverse, jamais par une modification de l'historique.
- **Module gratuit** : aucune formule d'abonnement n'en restreint l'accès.
- `Code` est une saisie **libre et facultative**, jamais générée : les écoles publiques y reportent le
  numéro d'immatriculation déjà posé par la mairie ou l'État.
- Deux documents PDF : fiche d'inventaire global (A4 paysage) et fiche de décharge de prêt (A5 paysage).

### Flux d'utilisation typique
1. Le Directeur ou le Secrétariat crée les catégories, puis enregistre les biens (lots) qui y
   appartiennent.
2. Tout mouvement (entrée, sortie, ajustement) passe par le journal de stock, jamais par une écriture
   directe de la quantité disponible.
3. Un prêt de matériel se formalise par une fiche de décharge imprimable, remise à l'emprunteur.

---

## 7. Internat

**Route :** `/internat`

### Objectif / Rôle principal
Gestion du régime d'hébergement d'un élève (Externe / Demi-pensionnaire / Interne) et de son
affectation en chambre — module désactivable par école, **désactivé par défaut**.

### Utilisateurs cibles
Directeur, Secrétariat, Surveillant (même trio que le module Convocations).

### Fonctionnalités clés & règles métier
- **Aucune entité « Lit » distincte** : l'hébergement réutilise les chambres du module Infrastructures
  (`RoomType.Dortoir`) ; la capacité se compte au niveau de la chambre, jamais lit par lit.
- **Le régime d'hébergement est porté par l'inscription, pas par l'élève** — portée annuelle : une
  réinscription reconfirme ou change le régime, jamais un report automatique d'une année sur l'autre.
- Comptage de capacité simple : occupants actifs (inscription non annulée, année active) comparés à la
  capacité de la chambre.
- **La pension n'est jamais retirée à la libération** d'un élève de l'Internat : sortir un élève
  (retour à Externe, changement de chambre) ne retranche pas la ligne de pension déjà facturée — toute
  correction suit la règle Finance habituelle (règle #4 d'`AGENTS.md`, correction tracée par
  Secrétariat/Admin).
- Le masquage du menu quand le module est désactivé n'est qu'un confort d'affichage : la garde réelle
  est portée par le serveur (`[RequireModule(SchoolModule.Internat)]`, refus 403), revérifiée
  indépendamment à l'inscription et à l'affectation de chambre.

### Flux d'utilisation typique
1. Le Directeur active le module Internat dans Paramètres, puis déclare des dortoirs via
   Infrastructures.
2. À l'inscription d'un élève, la Secrétariat choisit son régime d'hébergement (section « Régime &
   Hébergement » du formulaire) — une pension est ajoutée à son compte financier si Interne.
3. Depuis `/internat`, la Secrétariat ou la Surveillance affecte, transfère ou libère une chambre via
   la modale d'affectation rapide, avec recherche d'élève.
4. Le tableau de bord par chambre reflète l'occupation en temps réel.

---

## 8. Matières

**Route :** `/matieres`

### Objectif / Rôle principal
Référentiel des matières enseignées, leur coefficient, et — pour les écoles qui le souhaitent —
la structure d'évaluation détaillée (grilles APC) qui pilote le bulletin.

### Utilisateurs cibles
Directeur, Secrétariat, Enseignant.

### Fonctionnalités clés & règles métier
- Chaque matière porte un coefficient, utilisé dans le calcul des moyennes (§8.3).
- **Structure d'évaluation modulable / grilles APC** (option désactivée par défaut, onglet dédié) :
  le Directeur configure lui-même la grille d'un niveau — **deux niveaux de hiérarchie au plus** (un
  domaine porte des activités), une troisième profondeur n'a aucune colonne où s'afficher sur le
  bulletin et est refusée.
- `Subject.MaxScore` à `NULL` signifie « suit le barème du cycle », jamais « pas de barème » — le
  barème effectif est toujours résolu par une fonction unique, réutilisée par la saisie, la correction,
  l'import Excel et le bulletin, pour ne jamais diverger.
- Une matière ne peut porter le même nom qu'une autre matière du même niveau (index unique).

### Flux d'utilisation typique
1. Le Directeur ou le Secrétariat crée les matières du niveau, avec leur coefficient.
2. Pour un niveau en grille APC, le Directeur bâtit la hiérarchie domaine → activités dans l'onglet
   dédié.
3. Notes et bulletins se réfèrent ensuite à cette même structure, sans jamais la redéfinir ailleurs.

---

## 9. Enseignants

**Route :** `/enseignants`

### Objectif / Rôle principal
Fiche RH de chaque enseignant : état civil, matières qualifiées, affectations, rattachement optionnel
à un compte de connexion, informations réglementaires pour le rapport STATEDUC.

### Utilisateurs cibles
Super Admin, Directeur, Secrétariat.

### Fonctionnalités clés & règles métier
- **Matricule généré uniquement à l'enregistrement**, comme pour un élève — format
  `ENS-{AnnéeScolaire}-{Séquence sur 3 chiffres}` (§2).
- Fiche détaillée (« Voir le profil ») : informations personnelles, classes et matières assignées,
  historique des affectations, statistiques de performance — moyennes des classes suivies, taux de
  remise des notes/bulletins dans les délais (§4.2).
- **Rattachement d'un compte de connexion** (Directeur uniquement, optionnel) : lie la fiche RH à un
  compte de rôle Enseignant existant, pour que ce dernier puisse faire l'appel de ses classes assignées
  — c'est ce lien qui borne la saisie de présence et d'emploi du temps à ses propres classes/matières.
  Possible **à la création** de la fiche, mais aussi **après coup** depuis la fiche détaillée
  (`PUT /teachers/{id}/user-account`, ticket JGK-D06bis, 20/09/2026) : le flux réel de l'école est
  souvent l'inverse — la fiche RH existe depuis longtemps, le compte de connexion s'ouvre plus tard.
  Le rattachement vérifie que le compte appartient à la même école, porte le rôle Enseignant et n'est
  pas déjà lié à une autre fiche ; il peut aussi être changé ou retiré, jamais seulement posé une fois.
- Retirer une matière à un enseignant est une opération physique sur la table de liaison (aucune valeur
  d'audit à conserver), contrairement aux tables à donnée métier historisée.
- Champs STATEDUC repliés et facultatifs (genre, diplôme académique, diplôme professionnel, statut
  administratif, matricule de solde, date de première prise de service) — alimentent le rapport
  annuel STATEDUC (§13 de ce guide) sans jamais bloquer la création d'une fiche.

### Flux d'utilisation typique
1. Le Secrétariat ou le Directeur crée la fiche enseignant, sélectionne ses matières qualifiées.
2. Le Directeur rattache un compte « Enseignant » existant — dès la création, ou plus tard depuis la
   fiche détaillée si le compte n'existait pas encore à ce moment-là.
3. L'enseignant ainsi rattaché peut se connecter et n'agit que sur ses propres classes/matières/créneaux
   (Notes, Présences, Cahier de texte, Emploi du temps).

---

## 10. Notes et bulletins

**Route :** `/notes`

### Objectif / Rôle principal
Saisie des notes par matière et génération du bulletin de notes officiel.

### Utilisateurs cibles
Directeur, Secrétariat, Enseignant (borné à ses classes/matières assignées).

### Fonctionnalités clés & règles métier
- **Format du bulletin fixé au gabarit exact** de `docs/design-references/bulletin-reference.png` —
  reproduction fidèle, pas d'interprétation créative (§8.1, règle #12 d'`AGENTS.md`). Format A5
  portrait, largeurs de colonnes ajustées automatiquement pour ne jamais déborder sur une seconde page.
- **Système de notation sur 20 ou sur 10**, défini par établissement ou par classe — en pratique dérivé
  du **cycle** de la classe (Primaire /10, Collège & Lycée /20), et non un réglage libre (§8.2).
- Totaux automatiques sous le tableau : total des coefficients, total des points, moyenne générale
  (§8.3).
- **Mentions personnalisables** par l'établissement, avec seuils configurables (§8.4) — gérées dans
  Paramètres.
- **Affichage intelligent des notes** : `17.0` s'affiche `17`, mais `15.5` reste `15.5` (§8.5).
- Pour un niveau en grille APC (Matières, §8 de ce guide), le bulletin imprime la grille **entière**,
  cases vides comprises, colonnes de regroupement calculées automatiquement.

### Flux d'utilisation typique
1. L'Enseignant saisit les notes de ses classes et matières assignées, ou les importe via Excel.
2. Les totaux et la moyenne se calculent automatiquement.
3. Le Directeur ou le Secrétariat édite le bulletin au format officiel, prêt à l'impression ou à la
   remise en main propre.

---

## 11. Cahier de texte

**Route :** `/cahier-de-texte`

### Objectif / Rôle principal
Journal de classe : une entrée par séance réellement tenue (classe, matière, date, sujet, contenu,
devoirs), pour la traçabilité pédagogique et la continuité en cas de remplacement.

### Utilisateurs cibles
Écriture réservée à l'Enseignant, pour sa **propre** séance uniquement ; lecture ouverte à Directeur,
Secrétariat, Surveillant, Enseignant — document pédagogique partagé, pas un carnet privé.

### Fonctionnalités clés & règles métier
- **La garde d'écriture combine deux signaux, jamais un seul** : l'enseignant doit à la fois être
  assigné à cette classe/matière **cette année** (`TeacherAssignment`) **et** avoir un créneau
  planifié ce jour-là (`ScheduleSlot`) — un refus 409 explicite distingue les deux causes, jamais une
  simple 403 de rôle.
- **Fenêtre de correction de 15 jours**, symétrique création/suppression : passé ce délai, seuls le
  Directeur et le Secrétariat peuvent corriger une entrée — et toute correction, qu'elle vienne de
  l'auteur ou d'un rôle élevé, est systématiquement historisée.
- Écran filtrable par classe, matière et période ; modales de création, détail, édition et suppression.

### Flux d'utilisation typique
1. Un Enseignant, après une séance qu'il vient d'assurer, journalise ce qui a été traité et les devoirs
   éventuels.
2. Il peut corriger sa propre entrée dans les 15 jours qui suivent.
3. Passé ce délai, seuls Directeur ou Secrétariat peuvent encore la corriger, de façon tracée.
4. Tout le personnel habilité peut consulter le journal — utile en cas de remplacement d'un enseignant
   absent.

---

## 12. Examens officiels

**Route :** `/examens`

### Objectif / Rôle principal
Préparation et suivi des examens officiels (CFEE, BFEM, BAC) : dossiers candidats, contrôle d'état
civil, attribution de centre et de table, documents et exports réglementaires, résultats.

### Utilisateurs cibles
Directeur, Secrétariat, Enseignant (lecture seule, bornée à ses classes assignées).

### Fonctionnalités clés & règles métier
- Quatre onglets : **Sessions**, **Dossiers**, **Audit**, **Statistiques**.
- Une **session** regroupe, pour une année scolaire, un type d'examen et — pour BFEM/BAC — une
  série/option (le CFEE n'a pas de série). Un élève n'a qu'un seul **dossier** par session ; sa classe
  y est **figée** à l'ouverture, jamais réécrite par un transfert ultérieur (§22.1).
- **Contrôle d'état civil obligatoire** : tant que l'extrait de naissance n'est pas déclaré présent, un
  dossier ne peut pas passer à l'état `Complet` (§22.2).
- **L'audit est le seul filtre** qui autorise une transmission ou une impression par lot — jamais une
  case cochée manuellement (§22.3).
- **Numéro de table généré dans la transaction d'attribution**, jamais à l'ouverture du dossier — même
  principe que le matricule (§22.4, règle #3 d'`AGENTS.md`).
- Fiche de candidature individuelle ou par lot (uniquement dossiers `Complet`/`Transmis`), export
  ministériel Excel/CSV, carte de convocation dispatchable par SMS/WhatsApp (formule Premium) une fois
  centre et table attribués (§22.5).
- Les statistiques de réussite ne portent que sur les dossiers `Transmis` ou `Valide` d'une session
  close — jamais sur un dossier encore en préparation (§22.6).

### Flux d'utilisation typique
1. Le Directeur ou le Secrétariat ouvre une session d'examen pour l'année en cours.
2. Un dossier s'ouvre pour chaque élève de classe d'examen ; son état civil est vérifié.
3. L'audit relève les dossiers incomplets et ce qui leur manque.
4. Une fois complets, centre d'examen et numéro de table sont attribués, en lot si besoin.
5. Fiches de candidature et convocations sont éditées/dispatchées.
6. Après délibération, les résultats sont saisis sur chaque dossier ; les statistiques se lisent ensuite
   par session.

---

## 13. Intégration étatique

**Route :** `/integration-etatique`

### Objectif / Rôle principal
Produire les pièces et fichiers réglementaires dus au ministère de l'Éducation (SIMEN / Planète /
STATEDUC) — **aucune API publique du SIMEN n'existe à ce jour** : le module prépare des fichiers que
l'école transmet par la voie habituelle, il ne dialogue avec aucun système ministériel.

### Utilisateurs cibles
Directeur (3 onglets complets) ; Secrétariat (onglet Certificats de mutation uniquement).

### Fonctionnalités clés & règles métier
- Trois onglets : **Export Planète** (matrice élèves CSV/JSON par année/classe), **Rapport STATEDUC**
  (agrégats annuels + PDF/Excel), **Certificats de mutation** (registre + délivrance + révocation).
- **Identifiant National de l'Élève (IEN)** : facultatif, attribué par l'administration, jamais par
  l'école — distinct du matricule interne. Un **IEN provisoire** peut être généré en secours
  (préfixe `P` + clé de contrôle), marqué comme tel partout, et **écrasé sans retour possible** par
  l'arrivée du numéro officiel (l'inverse est interdit).
- **Export Planète** : part des **inscriptions** non annulées de l'exercice, pas des élèves en base ;
  aucune valeur inventée pour un champ non saisi ; aucune donnée financière ; le code établissement
  national est obligatoire, sinon l'export refuse de s'exécuter.
- **Rapport STATEDUC** : quatre tableaux réglementaires (effectifs, pyramide des âges, qualifications,
  statuts administratifs) — l'âge est calculé à la **date d'observation**, jamais « aujourd'hui »,
  pour que deux tirages du même rapport ne divergent pas.
- **Certificat de mutation** : numéro séquentiel généré à la délivrance, QR de vérification publique
  (statut authentique/révoqué/introuvable, jamais de donnée d'élève), append-only — une erreur se
  corrige par révocation puis nouvelle délivrance, jamais par modification. **Un solde impayé
  n'empêche jamais la délivrance** ; le certificat n'affiche aucun montant.
- **Livret de compétences** : parcours complet par compétence de la grille APC du niveau, échelle
  NA/ECA/A/E, disponible en téléchargement autonome.

### Flux d'utilisation typique
1. Le Directeur renseigne le code établissement national dans Paramètres — prérequis à tout export.
2. Il génère l'export Planète ou le rapport STATEDUC pour l'année scolaire souhaitée.
3. À la mutation d'un élève, le Secrétariat ou le Directeur délivre un certificat de mutation (recherche
   de l'élève, motif, destination) — téléchargé immédiatement en PDF avec QR.
4. L'école d'accueil scanne le QR sur une page publique pour vérifier l'authenticité du certificat.

---

## 14. Rapports d'assiduité

**Route :** `/rapports/assiduite`

### Objectif / Rôle principal
Bilan consolidé des absences et retards sur une période, avec un pont direct vers une convocation de
parent — plutôt que deux écrans qui s'ignorent.

### Utilisateurs cibles
Super Admin, Directeur, Secrétariat.

### Fonctionnalités clés & règles métier
- Le rapport agrège les absences et retards enregistrés par le module Présences.
- **Colonne « Action » — convoquer depuis le bilan** : sur toute ligne présentant au moins un retard ou
  une absence, un bouton ouvre une convocation dont le **motif est pré-rempli** avec les chiffres
  réellement comptés sur la période affichée, et reste modifiable. L'avis PDF s'imprime dans la foulée.
- **Aucun seuil ne convoque automatiquement à la place du Directeur** : le bilan propose, l'humain
  décide (§18.2).
- La colonne « Convoquer » n'apparaît qu'à l'intersection des deux permissions (lecture du rapport +
  droit sur les convocations) — le Secrétariat voit le rapport, mais pas le bouton, puisqu'il n'a pas
  accès au module Convocations.
- Export des présences disponible depuis cet écran, l'un des exports par domaine qui couvre le besoin
  de reporting en l'absence d'un export global (§11 API Paramètres).

### Flux d'utilisation typique
1. Le Directeur consulte le bilan d'assiduité sur la période de son choix.
2. Il repère un élève cumulant plusieurs absences ou retards.
3. Il clique sur « Convoquer » : la convocation s'ouvre avec le motif déjà rempli des chiffres constatés.
4. Il ajuste si besoin, valide, et l'avis de convocation s'imprime.

---

## 15. Présences

**Route :** `/presences`

### Objectif / Rôle principal
Saisie de l'appel (absences et retards) par classe et par matière.

### Utilisateurs cibles
**Saisie** : Enseignant (borné à ses propres classes/matières assignées), Directeur, Secrétariat,
Surveillant. **Consultation** d'une fiche déjà soumise : Directeur, Secrétariat, Surveillant, Super
Admin — la Finance en est explicitement exclue, la présence ne relevant pas de son périmètre.

### Fonctionnalités clés & règles métier
- L'appel se soumet par classe, matière, date et période — un enseignant ne peut soumettre que pour ses
  propres classes/matières assignées (même idiome de portée que le Cahier de texte, §11).
- Chaque absence ou retard enregistré peut déclencher, selon la configuration de l'école, une
  notification SMS et/ou WhatsApp au tuteur (§28.7 de ce guide, canal sortant uniquement).
- Alimente directement le Rapport d'assiduité (§14) et la génération de Billets (§16) pour un retard.

### Flux d'utilisation typique
1. L'Enseignant ouvre l'appel de sa classe/matière du jour.
2. Il coche les absences et retards, puis soumet la feuille.
3. Le tuteur d'un élève absent ou en retard reçoit, si activé, une alerte SMS/WhatsApp.
4. La donnée alimente le Rapport d'assiduité et, pour un retard, peut donner lieu à un Billet d'entrée.

---

## 16. Billets

**Route :** `/billets`

### Objectif / Rôle principal
Impression officielle A5 d'un billet d'entrée (retard) ou de sortie (départ anticipé) d'un élève.

### Utilisateurs cibles
Super Admin, Directeur, Secrétariat (délivrance à l'accueil), Surveillant.

### Fonctionnalités clés & règles métier
- Un **billet d'entrée** est l'impression d'un retard déjà enregistré via Présences — il ne crée pas
  la donnée, il la met en forme pour la remise en main propre à l'élève.
- Un **billet de sortie** documente une sortie anticipée de l'élève dans la journée.
- Génération PDF côté serveur, mise en page à points fixes — aucun débordement de page possible.

### Flux d'utilisation typique
1. Un élève se présente en retard : la Surveillance ou le Secrétariat génère son billet d'entrée à
   partir du retard déjà saisi dans Présences.
2. Un élève doit sortir avant l'heure : un billet de sortie est édité et remis pour justifier son
   absence auprès du professeur suivant.

---

## 17. Discipline

**Route :** `/discipline`

### Objectif / Rôle principal
Registre disciplinaire de l'établissement : faits, sanctions, suites.

### Utilisateurs cibles
Directeur, Surveillant.

### Fonctionnalités clés & règles métier
- Le Surveillant général enregistre chaque **fait disciplinaire** (date, élève, nature, sanction
  éventuelle, suites données) — **un fait n'est jamais effacé** ; une erreur de saisie se corrige par
  une mention rectificative tracée, le registre valant par son intégrité (§18.1).
- Un **procès-verbal de discipline** (PDF officiel, en-tête M.E.N.) s'édite à partir de chaque fait.

### Flux d'utilisation typique
1. Le Surveillant général consigne un fait disciplinaire au moment où il se produit.
2. Il édite, si nécessaire, le procès-verbal officiel correspondant.
3. Une correction ultérieure s'ajoute au registre sous forme de mention rectificative, sans jamais
   réécrire l'entrée d'origine.

---

## 18. Convocations

**Route :** `/convocations`

### Objectif / Rôle principal
Registre des convocations de parents/tuteurs — un document interne de la Vie scolaire, **pas** un
portail parent : aucune convocation n'ouvre de compte ni d'accès en ligne.

### Utilisateurs cibles
Directeur, Surveillant.

### Fonctionnalités clés & règles métier
- Une convocation porte motif, date et heure de rendez-vous, élève concerné ; un **avis de
  convocation** (PDF officiel) est édité pour remise en main propre ou envoi (§18.2).
- Peut être créée directement depuis le Rapport d'assiduité (§14), motif pré-rempli des chiffres
  constatés.
- **Suite obligatoire à consigner** : *honorée*, *non honorée* ou *reportée* — tant qu'elle n'est pas
  renseignée, la convocation reste *planifiée* et figure en tête du registre. Un **compte rendu est
  obligatoire** si l'entretien n'a pas eu lieu comme prévu (non honorée ou reportée), facultatif sinon
  (§18.3).
- La suite se consigne **une seule fois** : une erreur se corrige en émettant une **nouvelle**
  convocation, jamais en réécrivant la précédente — même exigence d'intégrité que le registre
  disciplinaire.
- L'avis PDF, document remis **avant** l'entretien, ne porte jamais la suite : elle n'existe pas encore
  au moment de son impression.

### Flux d'utilisation typique
1. Le Directeur ou le Surveillant crée une convocation (manuellement, ou depuis le Rapport d'assiduité).
2. L'avis PDF est imprimé et remis à l'élève ou envoyé à la famille.
3. Après l'entretien, la suite (honorée/non honorée/reportée) est consignée, avec compte rendu si
   nécessaire.
4. En cas d'erreur, une nouvelle convocation est émise plutôt que de corriger la précédente.

---

## 19. Pointage des enseignants

**Route :** `/pointage-profs`

### Objectif / Rôle principal
Enregistrement de la présence quotidienne des enseignants — alimente le suivi d'assiduité du personnel
et, pour les vacataires, recoupe la fiche de suivi des heures sans s'y substituer.

### Utilisateurs cibles
Directeur, Finance (également accessible à la Surveillance générale selon le rôle habilité au
pointage).

### Fonctionnalités clés & règles métier
- Le pointage se fait **par date**, pour chaque enseignant (§21.3).
- Pour un vacataire, le pointage recoupe la Fiche de suivi des heures (§22 Paie) utilisée dans le calcul
  de sa rémunération, sans jamais s'y substituer : c'est la fiche signée, pas le pointage seul, qui fait
  foi en cas d'écart.
- Alimente la **suggestion automatique** d'heures pré-remplie côté Paie, rapprochée de l'emploi du temps
  planifié — un écart entre heures déclarées et heures planifiées est signalé à titre indicatif, sans
  jamais bloquer la génération de la fiche de paie.

### Flux d'utilisation typique
1. Chaque jour, le rôle habilité pointe la présence des enseignants.
2. Pour un vacataire, ce pointage vient en recoupement de sa fiche de suivi des heures.
3. En fin de mois, la Direction ou la Finance s'appuie sur ces données — à titre de suggestion
   seulement — pour valider les heures retenues en Paie.

---

## 20. Caisse

**Route :** `/caisse`

### Objectif / Rôle principal
Point d'encaissement unique de l'établissement : c'est le **seul** endroit où un paiement est réellement
enregistré (Inscriptions ne fait que calculer le dû, §3).

### Utilisateurs cibles
Secrétariat, Directeur, Finance.

### Fonctionnalités clés & règles métier
- **Session de caisse obligatoire** : la journée d'encaissement s'ouvre par une session avec un fonds
  initial déclaré ; tant qu'aucune session n'est ouverte, la recherche d'élève et le formulaire
  d'encaissement restent masqués (§15.1).
- Encaissement, historique, **annulation avec motif**, remboursement, impression de reçu ; moyens de
  paiement : espèces, chèque, virement, Mobile Money (Wave, Orange Money) enregistré manuellement
  en V1 (§7.3).
- **Reçu au format exact** de `docs/design-references/receipt-reference.png`, avec la mention fixe et
  non désactivable *« Il est demandé aux parents de garder minutieusement leur reçu après le
  paiement. »* (§7.3bis, règle #12 d'`AGENTS.md`).
- **Contrôle du comptage physique à la clôture** (JGK-F09) : le montant en espèces réellement compté est
  un paramètre **requis** de la clôture, comparé aux espèces attendues (fonds initial + encaissements en
  espèces uniquement — jamais toutes méthodes confondues). Un écart non nul exige un motif avant de
  pouvoir clôturer.
- Une session close **ne peut pas être re-clôturée** ; tout redressement se fait par une écriture
  nouvelle et tracée (§15.2). Le **rapport de clôture** (PDF) détaille fonds d'ouverture, total encaissé,
  ventilation par mode et catégorie, écart et motif.
- Le **journal de caisse du jour** se recalcule à partir des paiements de la session — jamais figé dans
  un document opaque (§15.3).

### Flux d'utilisation typique
1. En début de journée, l'utilisateur ouvre une session de caisse et déclare le fonds initial.
2. Il recherche un élève et enregistre son versement (mode de paiement, montant), qui génère un reçu.
3. En fin de journée, il clôture la session : il saisit le montant réellement compté en espèces, motive
   un éventuel écart, puis télécharge le rapport de clôture.

---

## 21. Frais

**Route :** `/frais`

### Objectif / Rôle principal
Paramétrage du barème des frais scolaires par catégorie, niveau et classe — la donnée que consomment
Inscriptions et Caisse, sans jamais l'un ou l'autre ne la modifie directement.

### Utilisateurs cibles
Directeur, Finance (modification/suppression conditionnées par une délégation explicite du Directeur,
`allowFinanceToModifyFees` / `allowFinanceToDeleteFees`, Paramètres).

### Fonctionnalités clés & règles métier
- Catégories paramétrables : inscription, réinscription, mensualités, examens, uniformes, transport,
  cantine, autres — un établissement public peut régler une catégorie à **0** sans logique conditionnelle
  particulière (§7.1, §7.1bis).
- **Le service Finance ne modifie jamais directement un montant issu d'une inscription** (règle #4
  d'`AGENTS.md`) : toute correction passe par Secrétariat/Directeur et est historisée (§7.2).
- **Assistant de configuration** en deux temps : un montant standard appliqué en un clic à toutes les
  classes, puis personnalisation des seules exceptions par niveau/classe (§7.4). Tout changement de
  montant est historisé (ancien montant, nouveau, date, utilisateur).

### Flux d'utilisation typique
1. Le Directeur définit un montant standard par catégorie de frais, appliqué à toutes les classes.
2. Il ajuste ensuite les exceptions (une classe à un tarif différent) si nécessaire.
3. Ce barème est ensuite ce que Inscriptions fige au dossier de chaque élève, et ce que Caisse encaisse.

---

## 22. Paie

**Route :** `/paie`

### Objectif / Rôle principal
Gestion des contrats du personnel et édition des fiches de paie et attestations.

### Utilisateurs cibles
Directeur, Finance.

### Fonctionnalités clés & règles métier
- Un **contrat** par membre du personnel (enseignant titulaire, vacataire, personnel administratif ou de
  service), porteur d'un type de rémunération : salaire mensuel fixe ou taux horaire. Jamais supprimé
  physiquement — **clôturé** à une date, et reste consultable pour l'historique (§14.1).
- La **fiche de paie** se génère par employé et par mois, **à la demande**, jamais automatiquement à
  date fixe. Elle calcule brut, retenues salariales (IPRES, BRS), charges patronales (IPRES employeur,
  CSS, CFCE) et net à payer. Une fiche générée est un **instantané** : une modification ultérieure du
  contrat ne la réécrit pas (§14.2).
- Le nombre d'heures d'un vacataire est **pré-rempli par suggestion**, calculé depuis le registre
  d'heures et rapproché de l'emploi du temps planifié — un écart est signalé, jamais bloquant ; la
  Direction seule valide le nombre retenu avant génération.
- Une **attestation de travail** (PDF officiel) est éditable à tout moment, y compris pour un contrat
  clôturé (§14.4).

> **Limite connue** : les barèmes sociaux (taux IPRES/CSS/CFCE/BRS, plafonds) sont des **constantes du
> code**, non paramétrables avec date d'effet — un changement de barème impose une livraison logicielle
> et s'applique rétroactivement à tout recalcul (§17, note de limite).

### Flux d'utilisation typique
1. Le Directeur ou la Finance enregistre le contrat d'un nouvel employé.
2. Pour un vacataire, les heures effectuées sont déclarées au fil de l'eau (Pointage, §19).
3. En fin de mois, la fiche de paie est générée à la demande — le nombre d'heures suggéré est validé ou
   ajusté avant génération.
4. Le bulletin de salaire PDF est édité ; une attestation de travail peut être éditée à tout moment.

---

## 23. Fiscalité

**Route :** `/fiscalite`

### Objectif / Rôle principal
Génération de la déclaration fiscale et sociale mensuelle (TVA, charges sociales) prête à déposer.

### Utilisateurs cibles
Directeur, Finance.

### Fonctionnalités clés & règles métier
- Consolide la **TVA collectée** (portée par les encaissements du mois, saisie transaction par
  transaction), la **TVA déductible** (portée par les décaissements) et les **charges sociales**
  (agrégées depuis les fiches de paie du mois) (§17.1).
- Un encaissement **annulé** n'entre jamais dans la TVA collectée ; un encaissement **partiel** compte
  bien, puisqu'il représente de l'argent réellement perçu.
- **Une seule déclaration par mois** : une seconde génération pour la même période est **refusée**, elle
  ne remplace jamais silencieusement la première (§17.2).
- Une déclaration est un **instantané daté** : si les données du mois changent après coup (paiement
  régularisé, fiche de paie corrigée), elle n'est pas rejouable en l'état.

### Flux d'utilisation typique
1. En fin de mois, le Directeur ou la Finance génère la déclaration fiscale et sociale du mois écoulé.
2. Le PDF officiel consolidé (TVA collectée, TVA déductible, charges sociales) est téléchargé, prêt à
   déposer.
3. Toute correction nécessaire après génération se traite en dehors de cette déclaration déjà figée.

---

## 24. Trésorerie

**Route :** `/tresorerie`

### Objectif / Rôle principal
Vision consolidée des encaissements et décaissements de l'établissement, et enregistrement des sorties
de caisse (achats, charges, salaires versés).

### Utilisateurs cibles
Directeur, Finance.

### Fonctionnalités clés & règles métier
- Un **décaissement** porte catégorie, date, bénéficiaire et pièce justificative ; son annulation est
  une **suppression logique** tracée, jamais un effacement (§16.1).
- Le tableau de bord Trésorerie présente encaissements et décaissements consolidés sur une période, et
  le solde qui en résulte (§16.2).
- **Un décaissement ne modifie jamais le solde d'une inscription** : les deux flux sont indépendants et
  ne se compensent pas — un impayé élève reste un impayé même si l'école a par ailleurs dépensé la
  somme correspondante (règle §7.2).

### Flux d'utilisation typique
1. Le Directeur ou la Finance enregistre une dépense (décaissement), avec sa pièce justificative.
2. Le tableau de bord Trésorerie affiche la vision consolidée encaissements/décaissements sur la
   période choisie.
3. Une annulation éventuelle se fait par suppression logique, jamais par effacement de l'écriture.

---

## 25. Rapports financiers

**Route :** `/rapports/financiers`

### Objectif / Rôle principal
Rapport de consolidation des revenus sur une période choisie, exportable pour le comptable.

### Utilisateurs cibles
Directeur, Finance.

### Fonctionnalités clés & règles métier
- Indicateurs : encaissé aujourd'hui/ce mois/cette année, solde restant dû, taux de recouvrement
  (`Montant encaissé ÷ Montant attendu × 100`), détail par classe et par niveau, liste des élèves
  débiteurs (montant dû, jours de retard), simulation de revenus prévisionnels, graphiques d'évolution
  (§7.5).
- **Export `.xlsx`** sur exactement les mêmes bornes de période que ce qui est affiché à l'écran —
  l'export est le même rapport dans un autre format, jamais un second calcul (§16.3).
- **Réservé aux formules Standard et Premium** (`Feature.AdvancedFinancialReports`) : en formule
  Primaire, le bouton d'export est **désactivé et signalé**, jamais masqué.

### Flux d'utilisation typique
1. Le Directeur ou la Finance choisit une période sur `/rapports/financiers`.
2. Il consulte les indicateurs consolidés (recouvrement, débiteurs, prévisionnel).
3. Il exporte le rapport en `.xlsx` pour le transmettre au comptable, si sa formule le permet.

---

## 26. Paramètres

**Route :** `/parametres`

### Objectif / Rôle principal
Configuration de l'identité de l'établissement, de ses réglages transverses, de ses années scolaires,
de ses comptes et de sa sécurité — le seul écran qui pilote tous les autres modules sans en être un
lui-même.

### Utilisateurs cibles
Lecture : Directeur et Secrétariat (valeurs affichées en lecture seule pour ce dernier, dont le format
de date pilote tous les écrans). Écriture : Directeur uniquement, sauf délégation explicite qu'il pose
lui-même (voir ci-dessous).

### Fonctionnalités clés & règles métier
- Onglets : **Profil**, **Modules & fonctionnalités**, **Intégration étatique (SIMEN)**, **Formats &
  signatures officielles**, **Années scolaires**, **Mensualités & autorisations de caisse**,
  **Facturation & historique**, **Paramètres système (Sécurité & Administration)**, **Utilisateurs &
  rôles**, **Journal d'audit**, **Notifications SMS**, **Mentions**.
- Format des dates, format et personnalisation des matricules, système de notation par défaut, logo,
  cachet, signature du directeur, durée d'inactivité avant déconnexion automatique (§10, §12.1) — **un
  seul paramètre global**, appliqué sans exception à tous les écrans.
- **Délégations facultatives, à la guise du Directeur** : autoriser le Secrétariat à gérer la
  configuration de notation (`allowSecretaryToManageGrading`), autoriser la Finance à modifier/supprimer
  un frais (`allowFinanceToModifyFees` / `allowFinanceToDeleteFees`) — deux cases indépendantes, jamais
  un rôle codé en dur.
- **Zone de danger** (onglet Sécurité) : réinitialisation des données de test, **seule exception du
  produit** à l'interdiction de suppression physique — bornée au Directeur, confirmée par un mot de
  passage, journalisée. **Mode et verrouillage définitif sont deux décisions indépendantes** depuis le
  19/09/2026 : passer en mode réel est pleinement réversible et ne pose plus aucun verrou par effet de
  bord ; seul un « Verrouiller définitivement » explicite, non rejouable, ferme la purge pour toujours.
- **Utilisateurs & rôles** : création de comptes du personnel, blocage/suspension/réactivation (motif
  obligatoire), réinitialisation de mot de passe, historique des changements de statut — réservé au
  Directeur. **Correction de profil** (`PATCH /users/{id}/profile`, 20/09/2026) : le Directeur peut
  aussi corriger le nom complet et/ou l'e-mail d'un compte géré — typiquement une faute de frappe
  repérée après la création — sans passer par le changement d'e-mail en libre-service. Sur **sa propre
  ligne** (badge « Vous », crayon seul affiché), le Directeur ne peut corriger que son nom : son e-mail
  reste changeable uniquement via « Changer mon e-mail » (mot de passe requis, sessions révoquées) ;
  blocage, suspension, mot de passe et historique restent réservés aux autres comptes.
- **Changer mon e-mail** (menu de session, libre-service, `POST /auth/change-email`) est réservé au
  **Directeur et au Super Admin** depuis le 20/09/2026 — les comptes que le Directeur crée
  (Secrétariat, Finance, Enseignant, Surveillant) n'ont plus cette option, disparue de leur menu, et
  gardent uniquement « Changer mon mot de passe » : la correction de leur e-mail passe désormais par le
  Directeur (point précédent), pour ne pas le solliciter à chaque demande évitable.

### Flux d'utilisation typique
1. À la création de l'école, le Directeur renseigne l'identité de l'établissement, ses formats et son
   année scolaire active.
2. Il crée les comptes de son personnel et active, le cas échéant, les modules optionnels (Internat,
   grilles APC, classes passerelles).
3. Au fil de l'exploitation, il ajuste ses réglages (mentions, barème, délégations) depuis ce même écran
   central.

---

## 27. Console Super Admin

**Route :** `/admin`

### Objectif / Rôle principal
Administration technique de la plateforme elle-même : établissements, abonnements, tarification,
sécurité inter-écoles et traitement des demandes d'inscription self-service — **jamais** la pédagogie
ou la finance d'une école en particulier.

### Utilisateurs cibles
Super Admin exclusivement — le seul rôle sans `SchoolId` propre, et le seul autorisé à voir des données
agrégées inter-écoles.

### Fonctionnalités clés & règles métier
- Cinq sous-écrans : **Tableau de bord** plateforme (`/admin`), **Établissements**
  (`/admin/etablissements`), **Facturation** (`/admin/facturation`), **Tarification**
  (`/admin/tarification`), **Sécurité** (`/admin/securite`), **Demandes d'inscription**
  (`/admin/inscriptions`).
- **Inscription self-service en 4 étapes** (§11.5) : (1) un Directeur intéressé remplit un formulaire
  public sans authentification — aucun compte, établissement ni abonnement n'existe encore ; (2) le
  Super Admin approuve ou rejette la demande (avec motif) ; (3) à l'approbation, école + compte
  Directeur + abonnement (`AwaitingPayment`) sont créés **dans la même transaction** ; (4) tant que le
  premier paiement n'est pas confirmé, le Directeur n'a accès qu'à l'écran de paiement.
- **Un abonnement n'est jamais confirmé que par un webhook signé HMAC** de l'agrégateur de paiement —
  aucune route accessible au client ne positionne un abonnement `Active` (règle #11 d'`AGENTS.md`).
- **Alertes d'expiration** à 30, 15 et 7 jours, visibles au Directeur concerné et à la plateforme
  (§11.2). À expiration, passage en **mode restreint** (lecture seule, export toujours possible), jamais
  un blocage total par défaut (§11.3).
- **Isolation totale entre écoles** : une école n'accède jamais aux données d'une autre, garantie au
  niveau de la base (RLS), pas seulement dans le code (§11.4).
- Actions Super Admin : impersonation d'une école (support), rappel de paiement, octroi d'accès
  complémentaire, recharge de crédits SMS — toutes journalisées.

### Flux d'utilisation typique
1. Un Directeur soumet une demande d'inscription self-service depuis le site public.
2. Le Super Admin la retrouve dans `/admin/inscriptions`, vérifie, puis approuve ou rejette.
3. À l'approbation, l'école est créée automatiquement ; le Directeur règle son premier paiement, confirmé
   par webhook signé.
4. Le Super Admin suit ensuite les abonnements de toutes les écoles depuis `/admin/facturation` et
   `/admin/etablissements`, et intervient (rappel, impersonation) en cas de besoin.

---

## 28. Aide & Documentation

**Route :** `/aide`

### Objectif / Rôle principal
Centre d'aide intégré à l'application, module par module — pour répondre à « comment je fais X ? » sans
sortir de la plateforme.

### Utilisateurs cibles
Tous les rôles connectés.

### Fonctionnalités clés & règles métier
- Contenu organisé par module, reflétant la navigation elle-même.
- Les fonctionnalités hors périmètre V1 (portail Parents/Élèves, export global de données) y sont
  documentées comme telles, jamais présentées comme disponibles.

### Flux d'utilisation typique
1. Un utilisateur bloqué sur un écran ouvre l'Aide.
2. Il retrouve la section correspondant au module où il se trouvait.
3. Il applique la procédure décrite, ou comprend qu'une fonctionnalité attendue est hors périmètre V1.

---

**Fin du guide.** Pour toute question sur un point non couvert ici, se référer aux documents sources
listés en introduction — ce guide est une synthèse de lecture rapide, pas une nouvelle source de vérité.
