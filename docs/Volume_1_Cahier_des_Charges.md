# SAMA ECOLE

# VOLUME 1 — Cahier des Charges Fonctionnel

**Version :** 2.1
**Statut :** Validé — remplace intégralement les versions 1.0 à 1.4
**Changements de la v2.0 :**
- Suppression de toute référence au mode local / LAN / hors connexion (voir Volume 0 v2.0 — l'application est en ligne dès le lancement).
- Fusion et déduplication des différentes « mises à jour » (1.1 à 1.4) en un seul document organisé par domaine fonctionnel, et non plus par ordre chronologique de rédaction.
- Séparation explicite entre exigences **MVP** et exigences **Post-MVP** (renvoi au Volume 1.5, Chapitre 7, pour l'arbitrage définitif).

**Changements de la v2.1 (27/07/2026) — rattrapage documentaire :**
- Ajout des chapitres **14 à 21**, qui spécifient des modules **déjà livrés en production** et jusqu'ici absents de ce cahier des charges : Paie, Caisse, Trésorerie, Fiscalité, Discipline & Convocations, Infrastructures, Documents administratifs, Emploi du temps & Pointage. Ces chapitres décrivent l'existant, pas un reste à faire.
- Le chapitre **§13 (Portails Parents et Élèves & Messagerie) est reporté en V3** et sort du périmètre V1 — voir l'encadré en tête de ce chapitre et le Volume 1.5 §8.
- Reprise du nom de produit **Sama Ecole** (le document portait encore l'ancien nom Jangalekat).

---

## Table des matières

1. Gestion des utilisateurs et sécurité des comptes
2. Identifiants automatiques (matricules)
3. Importation massive de données
4. Fiches détaillées (élèves, enseignants)
5. Gestion des classes
6. Gestion des inscriptions et réinscriptions
7. Module financier
8. Bulletins scolaires
9. Rôles et permissions
10. Paramétrage général de l'établissement
11. Gestion des abonnements (multi-écoles)
12. Uniformisation transverse (dates, affichage des notes)
13. Portails Parents et Élèves & Messagerie — **reporté en V3, hors périmètre V1**
14. Paie & Bulletins de salaire
15. Caisse & Journal de caisse
16. Trésorerie & Décaissements
17. Fiscalité & Gestion de la TVA
18. Discipline & Convocations des parents
19. Infrastructures (Bâtiments & Salles)
20. Documents administratifs officiels
21. Emploi du temps & Pointage des enseignants

---

## 1. Gestion des utilisateurs et sécurité des comptes

### 1.1 Statuts utilisateur

Chaque utilisateur possède un statut :

- **Actif**
- **Suspendu** (temporaire, avec motif et date de fin optionnelle)
- **Bloqué** (définitif)

Motifs de suspension/blocage à consigner : départ de l'employé, fin de contrat, faute administrative, suspension disciplinaire, autre (texte libre).

L'administrateur (Directeur ou Super Admin selon portée) doit pouvoir :
- suspendre temporairement un utilisateur ;
- bloquer définitivement un utilisateur ;
- réactiver un utilisateur suspendu ;
- consulter l'historique complet des changements de statut (qui, quand, motif).

### 1.2 Mots de passe

- Chaque utilisateur peut modifier son propre mot de passe (avec confirmation de l'ancien).
- Un administrateur peut déclencher la réinitialisation du mot de passe d'un utilisateur de son périmètre (envoi d'un lien de réinitialisation par email — plus de reset « en dur » côté serveur puisque l'application est en ligne).
- Politique de mot de passe minimale : 8 caractères, au moins une majuscule, un chiffre. Détails complets au Volume 7 (Sécurité).

### 1.3 Session et inactivité

- Déconnexion automatique après une période d'inactivité configurable (valeur par défaut : 15 minutes, ajustable par le Directeur dans les paramètres de l'établissement).
- Retour automatique à l'écran de connexion, sans perte de la saisie en cours dans les formulaires ouverts (les brouillons sont conservés côté client pendant quelques minutes — voir Volume 0 §0.8 « Résilience réseau côté client »).

---

## 2. Identifiants automatiques (matricules)

### 2.1 Principe général — génération à l'enregistrement uniquement

**Règle impérative :** le matricule n'est **jamais** généré à l'ouverture d'un formulaire. Il est généré **uniquement au moment de l'enregistrement définitif**, dans une transaction atomique avec la création de l'enregistrement.

Processus :
1. Ouverture du formulaire (élève ou enseignant).
2. Remplissage des informations.
3. Clic sur « Enregistrer ».
4. Le serveur génère le matricule et crée l'enregistrement **dans la même transaction de base de données**.
5. Le matricule est retourné et affiché.

> Cette règle corrige un défaut identifié dans une version antérieure du cahier des charges, où le matricule était réservé à l'ouverture du formulaire et perdu si l'utilisateur annulait — créant des trous de numérotation. La génération transactionnelle au moment de l'enregistrement élimine ce problème par construction ; aucune « réservation temporaire à libérer » n'est nécessaire.

### 2.2 Format

- Élèves : `ELEV-{AnnéeScolaire}-{Séquence sur 4 chiffres}` — ex. `ELEV-2026-0001`.
- Enseignants : `ENS-{AnnéeScolaire}-{Séquence sur 3 chiffres}` — ex. `ENS-2026-001`.
- La séquence est **par établissement** (chaque école a sa propre numérotation, cohérente avec l'isolation multi-tenant).
- Le préfixe, le format et le nombre de chiffres sont personnalisables par établissement dans les paramètres.

---

## 3. Importation massive de données

Applicable aux élèves et aux enseignants, depuis des fichiers **Excel (.xlsx)** ou **CSV (.csv)**.

Fonctionnalités requises :
- Aperçu des données avant importation définitive (aucune écriture en base tant que l'aperçu n'est pas validé).
- Détection automatique des doublons (sur nom + prénom + date de naissance pour les élèves ; nom + prénom + numéro de pièce pour les enseignants).
- Détection et signalement des lignes en erreur (champ obligatoire manquant, format de date invalide, etc.), avec possibilité de corriger et réimporter uniquement les lignes en erreur.
- Import réalisé en tâche asynchrone côté serveur pour les fichiers volumineux, avec notification de fin d'import (l'utilisateur n'attend pas bloqué sur l'écran).
- Rapport d'import téléchargeable (lignes importées, lignes rejetées, motif de rejet).

---

## 4. Fiches détaillées (élèves, enseignants)

### 4.1 Fiche élève — « Voir le profil »

Contenu affiché :
- Photo, matricule, informations personnelles.
- Classe actuelle et historique des classes (parcours scolaire).
- Notes et bulletins.
- Historique des paiements et solde.
- Absences.

### 4.2 Fiche enseignant — « Voir le profil »

Contenu affiché :
- Informations personnelles.
- Classes et matières assignées, historique des affectations.
- Statistiques de performance (moyennes des classes suivies, taux de remise des notes/bulletins dans les délais).

---

## 5. Gestion des classes

### 5.1 Classes non codées en dur

Aucune liste de classes ou de niveaux ne doit être codée en dur dans l'application. Chaque établissement crée librement ses niveaux et classes (ex. TPS, PS, MS, GS pour une maternelle ; 3ème S, Terminale L2 pour un lycée). Toute fonctionnalité qui dépend des classes (Finance, Bulletins, Statistiques) doit se référencer dynamiquement à la liste créée par l'établissement — jamais à une liste figée dans le code.

### 5.2 Filtres et affichage

- Recherche par classe, niveau, enseignant responsable.
- Affichage de l'effectif total, garçons, filles.
- Impression de listes de classe.

---

## 6. Gestion des inscriptions et réinscriptions

### 6.1 Pré-inscription

- Création d'un dossier provisoire avec réservation de place.
- Conversion en inscription définitive après complétude du dossier.

### 6.2 Réinscription annuelle

- Réinscription d'un élève existant vers l'année scolaire suivante, avec conservation intégrale de l'historique scolaire antérieur.

### 6.3 Contrôle des places disponibles

Affichage automatique par classe : capacité maximale, effectif inscrit, places restantes. Lorsqu'une classe est complète, l'inscription bascule automatiquement en **liste d'attente**, avec notification dès qu'une place se libère (désistement, transfert).

### 6.4 Gestion documentaire

Documents suivis par dossier : extrait de naissance, certificat de scolarité, certificat de transfert, photos d'identité, autorisation parentale. Statut de dossier : Complet / Incomplet / En attente.

### 6.5 Documents générés

- Fiche d'inscription (PDF).
- Reçu d'inscription (PDF) — format exact : voir §7.3bis et `docs/design-references/receipt-reference.png`.

### 6.6 Historique

Chaque inscription conserve : date, classe, année scolaire, utilisateur ayant effectué l'opération.

---

## 7. Module financier

### 7.1 Frais scolaires

Catégories paramétrables par l'établissement : inscription, réinscription, mensualités, examens, uniformes, transport, cantine, autres.

### 7.1bis Établissement public vs privé — clarification (sans impact code)

> **Note ajoutée en cours de développement (plateforme à ~70%)** : cette précision ne modifie **aucune** règle technique déjà codée — elle documente un comportement déjà couvert par le paramétrage existant (§7.4), pour lever toute ambiguïté future.

Sama Ecole s'adresse aussi bien aux établissements **privés** qu'**publics** (école publique, collège, lycée) :

- Un établissement **privé** utilise typiquement des frais de scolarité mensuels sur l'année scolaire (~9 mois), configurés via §7.4.
- Un établissement **public** sénégalais ne facture en général pas de scolarité mensuelle (enseignement gratuit/subventionné) — il peut alors régler chaque catégorie de frais à **0** ou la laisser simplement inutilisée (ex. seule la coopérative scolaire ou la cantine sont configurées), sans qu'aucun mode ou champ "établissement public" distinct ne soit nécessaire dans le modèle de données.
- Le module Finance reste donc **strictement le même** pour les deux cas — la différence est uniquement dans les valeurs saisies par l'établissement lors de son propre paramétrage, jamais dans une logique conditionnelle du code.
- L'abonnement Sama Ecole lui-même (Volume 1 §11.1) reste indépendant de cette distinction : un établissement public paie son abonnement plateforme comme tout autre établissement, que ses frais de scolarité internes soient à zéro ou non.

**Vérification recommandée (pas une reprise de développement)** : demander simplement à l'agent de code de confirmer que le ticket JGK-F01 (paramétrage des frais) accepte bien un montant à 0 pour une catégorie sans erreur de validation — si c'est déjà le cas, aucune action supplémentaire n'est nécessaire.

### 7.2 Synchronisation Inscription ↔ Finance

Lorsqu'un membre du secrétariat crée une inscription, le montant dû (inscription + frais scolaires + frais annexes) est **transmis automatiquement au module Finance**. Le service financier **ne peut pas modifier ces montants directement** : toute correction doit passer par le secrétariat ou un administrateur, et est historisée (ancien montant, nouveau montant, date, utilisateur).

Ceci évite les écarts comptables, les doubles saisies et réduit le risque de fraude.

### 7.3 Paiements

- Encaissement, historique, annulation (avec motif), remboursement.
- Impression de reçu.
- Moyens de paiement pris en charge en V1 : espèces, chèque, virement, **Mobile Money (Wave, Orange Money)** — enregistré manuellement en V1 (l'intégration API directe avec les opérateurs est prévue en V2, voir Volume 0 §0.11).

### 7.3bis Format du reçu — référence exacte

Le reçu (inscription, paiement, etc.) suit **exactement** le modèle fourni dans `docs/design-references/receipt-reference.png` (description détaillée dans `docs/design-references/README.md` §1) : en-tête établissement, titre numéroté, bloc d'informations, tableau des frais, puis obligatoirement la mention suivante avant la signature :

> *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*

Cette mention est fixe, imprimée sur tous les reçus générés par le système, sans exception ni configuration désactivable par l'établissement.

### 7.4 Paramétrage des frais — assistant de configuration

Pour éviter la saisie manuelle répétitive :
- **Option 1 — Définition globale :** un montant standard peut être appliqué en un clic à toutes les classes.
- **Option 2 — Personnalisation par niveau/classe :** l'établissement ajuste ensuite uniquement les exceptions.

Tout changement de montant est historisé (ancien montant, nouveau montant, date, utilisateur).

### 7.5 Tableau de bord et statistiques financières

Indicateurs : encaissé aujourd'hui / ce mois / cette année, solde restant dû, taux de recouvrement (`Montant encaissé ÷ Montant attendu × 100`).

Détail par classe et par niveau : total payé, solde restant, pourcentage de paiement.

Liste automatique des élèves débiteurs : montant dû, nombre de jours de retard.

Simulation de revenus prévisionnels : `Nombre d'élèves × frais configurés`, avec comparaison revenus attendus / encaissés / restants.

Graphiques : évolution mensuelle des recettes, répartition par classe, répartition des dépenses par catégorie, comparaison annuelle, taux de recouvrement mensuel.

### 7.6 Rapports financiers

Journal de caisse, rapport mensuel, rapport annuel — export PDF et Excel.

### 7.7 Dépenses

Suivi des dépenses par catégorie, avec justificatif attaché (upload de document), et calcul du solde disponible (recettes − dépenses).

---

## 8. Bulletins scolaires

### 8.1 Format

- **Référence exacte à reproduire** : `docs/design-references/bulletin-reference.png` (description détaillée dans `docs/design-references/README.md` §2) — colonnes, en-têtes, blocs de synthèse et mentions dans l'ordre exact de cette référence.
- Format **A5 Portrait**, avec ajustement automatique des largeurs de colonnes pour éviter tout débordement sur une seconde page.
- Colonnes Matière / Moyenne / Mention optimisées pour l'impression.
- **Périodes d'évaluation paramétrables** (Évolution N°2) : le Directeur choisit dans Paramètres › Pédagogie
  le découpage de l'année — trimestriel (défaut, 3 trimestres), semestriel (2 semestres) ou personnalisé
  (2 à 6 périodes réparties à parts égales). Il s'applique aux années créées ensuite ; sur l'année en cours
  il se rejoue à la demande, et **seulement tant qu'aucune note ni appréciation de bulletin n'y est saisie**.
  Les sélecteurs de période (saisie des notes, moyennes, bulletins) et les documents PDF suivent ce découpage ;
  le titre du bulletin en découle (« BULLETIN DU 1ER SEMESTRE », « BULLETIN DU 2E TRIMESTRE », « BULLETIN DE LA
  1RE PÉRIODE »).

### 8.2 Système de notation

- Notation sur 20 (seuil de réussite 10/20) ou sur 10 (seuil de réussite 5/10).
- Choix défini par établissement ou par classe.

### 8.3 Totaux automatiques

Calcul automatique, sous le tableau de notes : total des coefficients, total des points, moyenne générale.

### 8.4 Mentions personnalisables

Mentions définies par l'établissement (ex. Excellent, Très Bien, Bien, Assez Bien, Passable, Encouragements, Félicitations), avec seuils configurables.

### 8.5 Affichage intelligent des notes

Suppression des décimales inutiles : `17.0 → 17`, `15.0 → 15`, mais `15.5` reste affiché tel quel.

### 8.6 Format de date

Affichage de la date de naissance selon le format choisi par l'établissement (voir §12).

### 8.7 Coefficients par série et surcharge du Directeur (Évolution N°4)

Au lycée, une même matière ne pèse pas pareil selon la série. Le coefficient utilisé dans les moyennes, les
rangs, la fiche élève et les bulletins (colonne « Coefficient » du §8.1, mise en page inchangée) est, dans
l'ordre de priorité :

1. la **surcharge de la classe**, si le Directeur en a posé une ;
2. sinon la **surcharge de la série** de la classe ;
3. sinon le **coefficient de la matière** (comportement historique, inchangé).

- **Séries.** Catalogue fermé : `L1`, `L2`, `S1`, `S2`, `TECH` (séries techniques). La série se renseigne sur la
  **classe** (Paramètres › Classes, réservée au cycle Lycée) ; une classe sans série (Seconde commune, collège…)
  garde les coefficients de ses matières.
- **Modèles nationaux.** « Appliquer le modèle » (onglet Coefficients de l'écran Matières) matérialise en
  surcharges de série les coefficients nationaux de L1, L2, S1 et S2 ; elles sont ensuite visibles et
  modifiables. `TECH` n'a pas de modèle (aucune valeur officielle fournie). Le modèle ne modifie jamais le
  coefficient d'une matière et n'écrase pas une valeur déjà posée sans demande explicite.
- **Surcharge.** Écriture réservée au **Directeur** ; le Secrétariat consulte la grille. Portée : une série
  (lycée) ou une classe précise (collège et lycée). Primaire et Maternelle : coefficient toujours égal à 1,
  non surchargeable. Chaque écriture est journalisée et protégée contre l'écrasement concurrent (409).
- **Par année scolaire.** Les surcharges appartiennent à l'année ; une nouvelle année démarre sans surcharge et
  le Directeur les reprend explicitement (« Reprendre l'année précédente », sans écraser l'existant).
- **Effet rétroactif.** Modifier un coefficient recalcule les moyennes et les bulletins de l'année en cours pour
  les classes concernées ; l'écran l'annonce dès que des notes existent.

### 8.8 Matières optionnelles et dispenses

Un élève peut ne pas suivre toutes les matières de sa classe, de deux façons :

- **Option non suivie** — certaines matières sont **au choix** (LV2 : Espagnol, Arabe, Allemand ; option
  scientifique : PC ou SVT). Dans Matières, la case « Matière optionnelle / au choix » les marque, et un
  **groupe d'options** (« LV2 ») indique lesquelles s'excluent : un élève suit **au plus une matière par
  groupe** ; une option sans groupe est cumulable.
- **Dispense** — l'élève est exempté d'une matière **obligatoire** (ex. EPS pour raison médicale). Un
  **motif** est obligatoire ; il n'est jamais imprimé.

- **Saisie.** À l'inscription (bloc « Langues & options ») ou sur la fiche élève (onglet « Options &
  dispenses »), le Secrétariat ou le Directeur enregistre les options suivies ; les autres options du niveau
  deviennent des dispenses de l'inscription, donc de l'année. Les dispenses de matières obligatoires se saisissent
  sur la fiche élève. Les options proposées sont celles du niveau de la **classe courante** de l'élève.
- **Tant qu'aucun choix n'est enregistré**, l'élève suit toutes les options : rien ne change au déploiement ni
  à l'activation d'une option.
- **Saisie des notes.** L'élève dispensé (des deux sortes) ne figure ni dans la feuille de saisie, ni dans
  l'export Excel, ni sur la fiche imprimée de la matière ; une note ne peut pas lui être saisie (rejet 422, et
  ligne rejetée à l'import Excel).
- **Moyennes.** La matière n'entre plus dans les moyennes ; le total des coefficients s'adapte (ex. 22 au lieu
  de 25). Les notes déjà saisies sont **conservées** mais masquées.
- **Bulletin.** Une option non suivie **n'apparaît pas**. Une matière obligatoire dispensée **reste**, à sa place :
  la zone des notes porte « Dispensé(e) », le coefficient est barré, aucune moyenne ni point, et les totaux
  l'ignorent.
- **Portée.** Réservé aux matières autonomes (ni domaine d'évaluation, ni activité). Repasser une matière en
  « obligatoire » rend ses options non suivies à tous. Une réinscription repart sans dispense.
- **Rang.** Le classement compare les moyennes générales ; deux élèves aux options différentes sont comparés
  sur la moyenne, non sur le total de points.

---

## 9. Rôles et permissions

| Rôle | Accès | Sans accès |
|---|---|---|
| **Secrétariat** | Élèves, Inscriptions, Classes, Documents administratifs | Comptabilité, Notes, Bulletins |
| **Finance** | Finance, Paiements, Rapports financiers, Reçus | Notes, Bulletins |
| **Enseignant** | Notes, Bulletins, Classes attribuées | Finance, Administration |
| **Directeur** | Accès complet en consultation sur son établissement, configuration | — |
| **Super Admin** | Administration technique de la plateforme (licences, abonnements, écoles) | Pédagogie et finance de chaque école |

La matrice complète, exhaustive par action (lecture/écriture/suppression/export), est spécifiée au Volume 7 (Sécurité), Annexe « Matrice des rôles et permissions ».

---

## 10. Paramétrage général de l'établissement

Chaque établissement peut définir dans ses paramètres :

- Système de notation (/10 ou /20) et seuil de réussite.
- Format et personnalisation des matricules.
- Format des dates.
- Logo, cachet officiel, signature du directeur (utilisés dans les documents générés).
- Mentions du bulletin.
- Durée d'inactivité avant déconnexion automatique.
- Paramètres financiers (frais, catégories, moyens de paiement acceptés).

---

## 11. Gestion des abonnements (multi-écoles)

### 11.1 Licences et abonnements

Chaque établissement dispose d'un abonnement avec date de début, date d'expiration, type de plan.

Plans et tarification indicative (montants en FCFA à valider par vous selon votre étude de marché — structure ci-dessous prête à l'emploi) :

| Plan | Effectif couvert | Prix mensuel | Prix annuel |
|---|---|---|---|
| **Pack École Primaire** | jusqu'à 500 élèves | 10 000 FCFA | 100 000 FCFA |
| **Pack Standard** | jusqu'à 2 000 élèves | 25 000 FCFA | 250 000 FCFA |
| **Pack Premium** | élèves illimités | 45 000 FCFA | 450 000 FCFA |

### 11.2 Alertes d'expiration

Notifications automatiques à 30, 15 et 7 jours avant expiration, envoyées au Directeur et visibles en bannière dans l'application.

### 11.3 Comportement à expiration ou en attente de paiement

À expiration, passage automatique en **mode restreint** (lecture seule, export des données toujours possible) plutôt qu'un blocage total — pour ne jamais empêcher un établissement de récupérer ses données. Le blocage total reste une option configurable par le Super Admin pour les cas de non-paiement prolongé. **Ce même mode restreint s'applique aussi tant que le premier paiement d'un établissement nouvellement approuvé n'a pas été confirmé** (voir §11.6) — un établissement en attente de paiement n'a accès qu'à l'écran de paiement, à l'exclusion de tout autre module.

### 11.4 Isolation des données entre écoles

Chaque établissement n'accède qu'à ses propres données. Le mécanisme technique (Row-Level Security PostgreSQL) est décrit au Volume 3. Une école ne peut en aucun cas consulter les données d'une autre école, y compris par une faille de filtrage côté application — l'isolation est garantie au niveau de la base de données elle-même, pas seulement dans le code métier.

### 11.5 Inscription self-service avec validation (remplace le formulaire de simple contact)

**Décision mise à jour** : le formulaire de simple prise de contact (ancienne version de cette section) est remplacé par un **formulaire d'inscription self-service complet**, combinant l'auto-service et le contrôle qualité du Super Admin.

**Étape 1 — Formulaire public (sans authentification)**
Un Directeur intéressé accède, via le site public, à un formulaire détaillé qui recueille :
- Ses informations personnelles : nom complet, email, téléphone, mot de passe choisi (stocké haché immédiatement, jamais en clair).
- Les informations de son établissement : nom, adresse, ville/région, type d'établissement, effectif approximatif d'élèves.
- Le plan souhaité (§11.1).
- Le moyen de paiement prévu (§11.6) — simple préférence déclarée à ce stade, aucun paiement n'est demandé avant validation.

À la soumission, une **demande d'inscription** est créée (statut `Pending`) et une **référence de suivi** est affichée au Directeur à l'écran **et lui est envoyée par e-mail** (pour qu'il ne la perde pas). Le **Super Admin** est alerté par un e-mail distinct (adresse `Registration__AdminNotificationEmail`, avec le détail de la demande et un lien vers le tableau de bord de revue). Aucun e-mail n'est jamais adressé au personnel d'un établissement existant. **Aucun compte, aucun établissement, aucun abonnement n'existe encore** à ce stade — le Directeur n'a accès qu'à une page de suivi de sa demande (via la référence + email), rien d'autre.

**Étape 2 — Revue par le Super Admin**
Le Super Admin consulte la liste des demandes en attente, et peut : **Approuver**, **Rejeter** (avec motif), ou demander des précisions par email en dehors de la plateforme.

**Étape 3 — Activation après approbation**
Dès l'approbation, le système crée automatiquement, dans la même transaction : l'établissement (`School`, actif), le compte Directeur (`User`, actif, mot de passe déjà défini à l'étape 1), et l'abonnement (`Subscription`, statut `AwaitingPayment`, aucune date d'expiration tant que le premier paiement n'est pas confirmé). Le Directeur reçoit alors un e-mail de confirmation avec le lien de connexion et peut se connecter (en cas de rejet, un e-mail lui transmet le motif).

**Étape 4 — Premier accès du Directeur**
Tant que l'abonnement est en statut `AwaitingPayment`, le Directeur n'a accès qu'à l'écran de paiement (§11.6, mode restreint défini en §11.3) — aucun autre module (élèves, classes, notes...) n'est accessible avant confirmation du premier paiement.

**Justification** : cette combinaison évite le principal défaut du self-service pur (écoles fictives ou non qualifiées, sans aucun filtre humain) tout en évitant la lenteur d'un traitement 100 % manuel par appel téléphonique — le Super Admin ne fait que valider une demande déjà entièrement renseignée et structurée.

### 11.6 Paiement des abonnements

**Moyens de paiement acceptés** : Mobile Money (Wave, Orange Money), Virement bancaire, Carte bancaire (Visa/Mastercard).

**Mécanisme technique** : intégration via un agrégateur de paiement couvrant nativement ces trois moyens en une seule API — recommandation : **PayDunya** (solution d'origine sénégalaise, support local) ou **CinetPay**, tous deux couvrant Mobile Money + carte + virement pour le Sénégal. Le choix définitif de l'agrégateur reste à valider par vous (négociation tarifaire, qualité de support).

**Règle de sécurité non négociable** : un paiement n'est confirmé **que** par la réception et la vérification (signature HMAC) d'un webhook envoyé par l'agrégateur — jamais par une simple redirection ou déclaration du navigateur du Directeur. Voir Volume 7 (Sécurité) pour le détail.

**Cycle de vie d'un paiement** :
1. Le Directeur choisit son moyen de paiement et le montant (mensuel ou annuel, selon son plan).
2. Le système initie une session de paiement auprès de l'agrégateur et redirige le Directeur.
3. Le Directeur complète le paiement (Mobile Money, carte, ou instructions de virement) sur la page de l'agrégateur.
4. L'agrégateur notifie la plateforme via webhook signé.
5. Sur confirmation, l'abonnement passe en statut `Active`, avec une date d'expiration calculée à partir de la période choisie (mensuel/annuel) ; le mode restreint est levé.
6. En cas d'échec ou d'absence de confirmation, l'abonnement reste `AwaitingPayment` et le Directeur peut retenter.

**Renouvellement** : suit exactement le même mécanisme de paiement, déclenché par les alertes d'expiration (§11.2) plutôt que par une approbation Super Admin (déjà acquise depuis la première activation).

---

## 12. Uniformisation transverse

### 12.1 Format des dates

Un seul paramètre global (`Paramètres → Format des dates`), appliqué à toute l'application : `01/01/2010` ou `01 janvier 2010`. S'applique sans exception à : élèves, enseignants, finance, bulletins, inscriptions, rapports et exports.

### 12.2 Résultat attendu

À l'issue de ce cahier des charges, Sama Ecole couvre : administration, scolarité, inscriptions, bulletins, gestion pédagogique, gestion financière, paie du personnel, caisse et trésorerie, fiscalité, vie scolaire (discipline, convocations, pointage), infrastructures, documents administratifs officiels, gestion des abonnements multi-écoles, contrôle des accès, statistiques académiques et financières — adapté aux écoles maternelles, primaires, collèges, lycées et centres de formation du Sénégal, opérable entièrement en ligne.

---

## 13. Portails Parents et Élèves & Messagerie

> ## ⛔ CHAPITRE HORS PÉRIMÈTRE V1 — REPORTÉ EN V3
>
> **Statut (27/07/2026) : spécifié, non implémenté, non planifié en V1.** L'intégralité des sous-sections 13.1 à 13.6 ci-dessous est reportée à la **version V3** (Volume 1.5 §8). Le niveau de détail de ce chapitre ne vaut **pas** engagement de livraison — c'est précisément l'avertissement du Volume 1.5 §7 : « toute fonctionnalité listée en dehors du MVP est Post-MVP par défaut, quel que soit son niveau de détail dans le Volume 1 ».
>
> **Ce que la V1 livre à la place.** La communication de l'établissement vers les familles est **sortante uniquement**, sans compte ni portail pour le destinataire :
> - **Notifications SMS** vers les parents (absences, retards, relances d'impayés) — voir §13.7 ci-dessous ;
> - **Notifications WhatsApp** sur le même canal sortant ;
> - **E-mail transactionnel** ;
> - **Documents remis en main propre ou imprimés** : convocation de parent (§18), avis d'échéance (§20), bulletin de notes, reçus.
>
> **Conséquences opérationnelles, à respecter tant que la V3 n'est pas ouverte :**
> - Les rôles `Parent` et `Eleve` **ne doivent pas** être ajoutés à l'énumération `Role`, ni apparaître dans un `[Authorize]`.
> - Aucun endpoint de consultation ouvert à un tiers qui n'est pas personnel de l'établissement.
> - Une convocation de parent (§18) n'est **pas** un portail : c'est un document interne, sans compte ni accès en ligne.
>
> Ce report ne remet en cause ni le modèle de données ni la sécurité : le jour où le portail s'ouvrira, l'isolation Parent→ses enfants devra respecter à la lettre le double verrou Global Query Filter EF Core **et** policy RLS PostgreSQL (Volume 3 §2), appliqué cette fois *à l'intérieur* d'un même établissement.

**Exigence initiale (conservée pour la V3)** : Sama Ecole ne se limite pas à un outil de gestion interne à l'établissement — la plateforme met en relation les **enseignants** avec les **parents d'élèves** (tous niveaux) et avec les **élèves eux-mêmes** (Collège et Lycée uniquement).

### 13.1 Rattachement Parent ↔ Élève

- Un Parent (Tuteur) peut être rattaché à **plusieurs enfants**, y compris dans des classes ou établissements différents s'il utilise Sama Ecole pour plusieurs de ses enfants.
- Un Élève peut avoir **plusieurs Parents/Tuteurs** rattachés (père, mère, tuteur légal), chacun avec son propre compte.
- Le rattachement est saisi par le Secrétariat au moment de l'inscription (Volume 1 §6) et peut être complété/corrigé ultérieurement par le Directeur.
- Chaque Parent ne voit **que** les enfants qui lui sont explicitement rattachés — jamais les autres élèves de la classe, même de la même fratrie si non rattachée.

### 13.2 Compte Élève — restriction de cycle

- **Aucun compte de connexion n'est créé pour un élève de Maternelle ou de Primaire** — à cet âge, seuls les Parents ont accès aux informations.
- Un compte Élève n'est proposé qu'à partir du **Collège**, et jusqu'au **Lycée**.
- La création du compte Élève est une action volontaire du Secrétariat ou du Directeur (pas de création automatique à l'inscription), avec un mot de passe initial à changer à la première connexion.

### 13.3 Ce que le Parent peut consulter (lecture seule)

- Les notes et bulletins de chacun de ses enfants rattachés.
- Les absences et retards.
- Les paiements dus, effectués, et l'historique des reçus (Volume 1 §7).
- Les annonces de classe et messages des enseignants concernant son enfant (§13.5).
- Un Parent ne peut **rien modifier** : aucune saisie de note, aucune action financière, aucune gestion de compte.

### 13.4 Ce que l'Élève (Collège/Lycée) peut consulter (lecture seule)

- Ses propres notes et bulletins.
- Son emploi du temps et les annonces de ses classes.
- Les messages des enseignants qui lui sont adressés.
- Un Élève ne voit jamais les notes ou informations d'un autre élève, ni les informations financières de sa famille (réservées au Parent).

### 13.5 Messagerie et annonces

**Deux canaux, volontairement simples pour la V1** (pas de messagerie instantanée temps réel, réévaluable en V2 si le besoin se confirme — Volume 0, Décision D-14) :

1. **Annonces de classe** : un Enseignant publie une annonce visible par tous les Parents et Élèves (Collège/Lycée) de la classe concernée (ex. « Contrôle de mathématiques déplacé au 15 »).
2. **Messages individuels** : un Enseignant échange avec un Parent (ou un Élève de Collège/Lycée) au sujet d'un élève précis, sous forme de fil de discussion simple.

**Règles** :
- Toute communication passe **exclusivement** par l'Enseignant ou l'établissement — aucun canal Parent-Parent ni Élève-Élève.
- Chaque nouveau message ou annonce déclenche une **notification par email** (réutilise l'infrastructure de notification existante, Volume 3 §4.6). Les canaux SMS/WhatsApp restent en roadmap V2 (Volume 0 §0.12).
- Un Parent ne reçoit une notification que pour les messages/annonces concernant ses propres enfants rattachés.

### 13.6 Isolation et sécurité (rappel)

Le détail technique de l'isolation Parent→ses enfants et Élève→lui-même est spécifié au Volume 7 (Sécurité), §Portails Parents/Élèves — cette isolation est une extension du même principe déjà appliqué à l'isolation entre établissements (Row-Level Security), appliquée cette fois à l'intérieur d'un même établissement.

### 13.7 Ce que la V1 livre réellement — notifications sortantes (LIVRÉ)

Seule sous-section de ce chapitre effectivement en production. Elle remplace le portail : l'établissement **pousse** l'information vers la famille, la famille ne vient pas la consulter.

**Canaux :**

| Canal | Déclencheur | Statut |
|---|---|---|
| SMS | Absence ou retard d'un élève (à l'enregistrement de l'appel) | Livré |
| SMS | Confirmation d'encaissement d'un paiement | Livré |
| SMS | Relance d'impayés (envoi groupé déclenché par le Directeur/Finance) | Livré |
| WhatsApp | Absence ou retard d'un élève | Livré |
| E-mail | Réinitialisation de mot de passe, cycle de vie de l'abonnement, demandes d'inscription | Livré |

**Règles :**

- Les notifications SMS sont réservées à la formule **Premium** (`Feature.SmsNotifications`). Une école en formule **Primaire** ou **Standard** voit l'option signalée comme premium, et le serveur refuse l'envoi — le contrôle n'est jamais porté par le seul badge de l'interface.
- Tout SMS sortant passe par un **point de passage unique** (`SmsDispatcher`), qui vérifie dans l'ordre : numéro du tuteur renseigné, formule de l'établissement, **activation de ce type d'alerte** dans les paramètres de l'école, puis solde de crédits. Aucun envoi ne contourne ce passage.
- Chaque **type d'alerte** (absence, retard, paiement, relance d'impayés) s'active ou se désactive indépendamment : une école peut vouloir prévenir des absences sans relancer les impayés par SMS.
- Le **débit du solde de crédits est atomique** : la vérification du solde et sa décrémentation sont une seule opération en base. Deux alertes simultanées pour la même école ne peuvent pas consommer deux fois le même crédit.
- Chaque message est **historisé** avec son statut (envoyé, échec, solde insuffisant) et son coût en segments. L'historique est consultable par le Directeur et le rôle Finance.
- Le message ne contient jamais de note ni de donnée médicale : il signale un fait (absence, retard, paiement, échéance) et invite à contacter l'établissement.
- **La relance d'impayés en masse est déclenchée à la main** par le Directeur ou la Finance — jamais automatiquement : écrire à toutes les familles d'un coup est une décision, pas un effet de bord. Elle est cadrable à une seule classe.
- La relance ne vise que les inscriptions **confirmées** présentant un reliquat : une inscription annulée ou transférée n'est jamais relancée.
- Un envoi en masse **n'échoue pas en bloc** si un message ne part pas : le résultat rapporte le nombre d'envois réussis, le nombre d'écartés et le premier motif d'écart, à charge pour l'utilisateur de décider de la suite.

---

## 14. Paie & Bulletins de salaire

**Statut : livré.** Écran `/paie`. Rôles : Directeur, Finance.

### 14.1 Contrats du personnel

- L'établissement enregistre un **contrat** par membre du personnel : enseignant titulaire, enseignant vacataire, personnel administratif ou de service.
- Un contrat porte le type de rémunération : **salaire mensuel fixe** ou **taux horaire** (vacataires).
- Un contrat n'est jamais supprimé physiquement (règle transverse de suppression logique) : il est **clôturé** à une date, et reste consultable pour l'historique de paie et les attestations.

### 14.2 Calcul et édition de la paie

- La fiche de paie est générée **par employé et par mois**, à la demande, jamais automatiquement à date fixe.
- Elle calcule : le **brut** (salaire de base, ou heures × taux horaire pour un vacataire), les **retenues salariales** (IPRES sur base plafonnée, BRS), les **charges patronales** (IPRES employeur, CSS sur base plafonnée, CFCE), et le **net à payer**.
- Le bulletin de salaire est édité en **PDF A4** à l'en-tête de l'établissement.
- Une fiche de paie générée est un **instantané** : une modification ultérieure du contrat ne réécrit pas une fiche déjà produite.

### 14.3 Suivi des heures des vacataires

- Les heures effectuées par un vacataire sont déclarées au fil de l'eau et consolidées dans une **fiche de suivi** imprimable (PDF), destinée à être signée. C'est cette fiche, signée après coup, qui reste la pièce justificative qui fait foi.
- **Suggestion automatique, amendée le 26/08/2026 (ticket JGK-K01).** Le nombre d'heures à saisir en §14.2 est désormais **pré-rempli par suggestion**, calculée depuis ce même registre et rapproché de l'emploi du temps planifié (§21) : un écart entre heures déclarées et heures planifiées un jour donné est signalé à titre indicatif, sans jamais bloquer la génération. La suggestion reste intégralement modifiable et **la Direction seule valide** le nombre d'heures retenu avant de générer la fiche de paie — la génération automatique intégrale, elle, n'est volontairement pas construite : c'est un choix de contrôle assumé, pas une limitation technique.

### 14.4 Attestation de travail

- Une **attestation de travail** (PDF officiel) est éditable à tout moment à partir d'un contrat, y compris clôturé.

---

## 15. Caisse & Journal de caisse

**Statut : livré.** Écran `/caisse`. Rôles : Directeur, Finance.

### 15.1 Session de caisse

- La journée d'encaissement s'ouvre par une **session de caisse**, avec un fonds de caisse initial déclaré.
- Tous les encaissements de guichet de la journée (§7) sont rattachés à la session ouverte.
- La session se **clôture** explicitement, ce qui fige son périmètre.

### 15.2 Rapport de clôture

- À la clôture, le système calcule le **solde de fermeture théorique** : fonds d'ouverture + somme des encaissements de la session. Les paiements annulés en sont exclus, sans être effacés.
- Une session close **ne peut pas être re-clôturée**. Tout redressement se fait par une écriture nouvelle et tracée — le service Finance ne réécrit jamais l'historique (règle §7.2).
- Le **rapport de clôture** (PDF) présente : fonds d'ouverture, total encaissé, ventilation par **mode de paiement** et par **catégorie de frais**, et le **détail des transactions** (heure, numéro de reçu, élève, catégorie, mode, montant).

> **Limite connue — le rapprochement de caisse n'est pas implémenté.** Le système ne demande pas au caissier le **montant physiquement compté** en fin de journée, et ne calcule donc **aucun écart de caisse**. Le solde affiché est théorique : il dit ce qui *devrait* être en caisse, pas ce qui y est.
>
> C'est une lacune fonctionnelle assumée à ce stade, pas un oubli de rédaction : le rapprochement — confronter l'espèce comptée au calcul, faire apparaître l'écart, le motiver et le tracer — est précisément le contrôle qui donne sa valeur à une caisse. Il reste à spécifier et à construire (saisie du montant compté à la clôture, persistance de l'écart, motif obligatoire au-delà d'un seuil) **avant** de présenter le module Caisse comme complet à un établissement.

### 15.3 Journal de caisse

- Le **journal de caisse du jour** (bordereau) est éditable en PDF : liste chronologique des encaissements, totaux par mode de paiement (espèces, Mobile Money, virement, chèque).
- Le rapport se **recalcule** à partir des paiements de la session — il n'est pas figé dans un document opaque, et reste donc reproductible à l'identique après coup.

---

## 16. Trésorerie & Décaissements

**Statut : livré.** Écran `/tresorerie`. Rôles : Directeur, Finance.

### 16.1 Décaissements

- L'établissement enregistre ses **sorties de caisse** : achats, charges, salaires versés, frais divers, avec catégorie, date, bénéficiaire et pièce justificative.
- L'annulation d'un décaissement est une **suppression logique** tracée, jamais un effacement.

### 16.2 Vision consolidée

- Le tableau de bord Trésorerie présente **encaissements et décaissements consolidés** sur une période, et le **solde** qui en résulte.
- Un décaissement ne modifie **jamais** le solde d'une inscription : les deux flux sont indépendants et ne se compensent pas. Un impayé élève reste un impayé même si l'école a par ailleurs dépensé la somme (règle §7.2).

### 16.3 Rapport financier consolidé et export comptable

- Le **rapport de consolidation des revenus** est consultable sur une période choisie, depuis l'écran `/rapports/financiers`.
- Ce rapport est **exportable au format `.xlsx`** pour transmission au comptable, sur exactement les mêmes bornes de période que celles affichées à l'écran : l'export est le même rapport dans un autre format, jamais un second calcul.
- Les rapports financiers consolidés et les exports comptables sont réservés aux formules **Standard** et **Premium** (`Feature.AdvancedFinancialReports`) : en formule **Primaire**, le bouton d'export est **désactivé et signalé**, jamais masqué — l'utilisateur doit comprendre ce que la formule supérieure lui apporterait.

---

## 17. Fiscalité & Gestion de la TVA

**Statut : livré.** Écran `/fiscalite`. Rôles : Directeur, Finance.

### 17.1 Déclarations

- L'établissement génère une **déclaration fiscale et sociale mensuelle** (mois + année), éditée en **PDF officiel** prêt à être déposé.
- La déclaration consolide :
  - la **TVA collectée** — somme de la TVA portée par les encaissements du mois (le taux et le montant de TVA sont renseignés **transaction par transaction** à l'encaissement, pas déduits après coup) ;
  - la **TVA déductible** — somme de la TVA portée par les décaissements du mois ;
  - les **charges sociales** — agrégées à partir des fiches de paie du mois (§14).
- Un encaissement **annulé** n'entre pas dans la TVA collectée : il n'a jamais représenté d'argent réellement perçu. Un encaissement **partiel**, lui, est bien un encaissement réel et compte.

### 17.2 Règles

- Une déclaration est un **instantané daté** : elle fige les montants du mois au moment de sa génération.
- **Une seule déclaration par mois.** Une seconde génération pour la même période est **refusée** — elle ne remplace pas silencieusement la première, et ne crée pas de doublon dont on ne saurait plus lequel a été déposé.
- **Conséquence à connaître :** si les données du mois changent après la génération (paiement régularisé, fiche de paie corrigée), la déclaration déjà produite **n'est pas rejouable en l'état**. Le circuit de correction — annuler puis regénérer, ou produire une déclaration rectificative distincte — n'est **pas implémenté** et reste à arbitrer.

> **Limite connue — les barèmes sont figés dans le code.** Les taux et plafonds sociaux appliqués par le calcul de paie (§14.2) — IPRES salarié 5,6 % et employeur 8,4 %, CSS 7 %, CFCE 3 %, BRS 5 %, plafonds mensuels IPRES 360 000 FCFA et CSS 63 000 FCFA — sont des **constantes du code**, et non des paramètres de l'établissement.
>
> Deux conséquences à connaître : un changement de barème par l'administration sénégalaise impose **une livraison logicielle**, et le nouveau taux s'applique **rétroactivement** à tout recalcul d'un mois antérieur, ce qui est faux comptablement. Rendre ces barèmes paramétrables **avec date d'effet** — pour qu'un recalcul de mars conserve le barème de mars — est le prérequis avant tout déploiement à grande échelle du module Paie.

---

## 18. Discipline & Convocations des parents

**Statut : livré.** Rôles : Directeur, Surveillant.

### 18.1 Registre disciplinaire

- Le Surveillant général enregistre les **faits disciplinaires** : date, élève, nature du fait, sanction éventuelle, suites données.
- Un fait enregistré n'est **jamais effacé**. Une erreur de saisie se corrige par une mention rectificative tracée — le registre disciplinaire est une pièce à charge comme à décharge, sa valeur tient à son intégrité.
- Un **procès-verbal de discipline** (PDF officiel, en-tête M.E.N.) est éditable à partir de chaque fait.

### 18.2 Convocations des parents

- L'établissement crée une **convocation** du parent ou du tuteur : motif, date et heure du rendez-vous, élève concerné.
- Un **avis de convocation** (PDF officiel) est édité pour remise en main propre à l'élève, ou envoi.
- **Une convocation n'est pas un portail parent** (§13) : elle ne crée aucun compte, n'ouvre aucun accès en consultation, et ne constitue pas une amorce du chapitre 13. C'est un registre interne de la Vie scolaire.
- La convocation se crée aussi **depuis le bilan d'assiduité** (`/reports/attendance`, §11) : le motif y est pré-rempli avec les retards et les absences réellement comptés sur la période affichée, et reste modifiable. **Aucun seuil ne convoque à la place du Directeur** — le bilan propose, l'humain décide.

### 18.3 Suite donnée à une convocation

- Une convocation porte une **suite** : *honorée* (le parent s'est présenté), *non honorée*, ou *reportée*. Tant qu'elle n'est pas consignée, la convocation reste *planifiée* et figure en tête du registre.
- Un **compte rendu** est obligatoire lorsque l'entretien n'a pas eu lieu comme prévu — une absence ou un report appellent une suite écrite. Il reste facultatif quand l'entretien s'est tenu.
- La suite se consigne **une seule fois**. Le registre de la Vie scolaire vaut par son intégrité, au même titre que le registre disciplinaire (§18.1) : une erreur se corrige en émettant une **nouvelle** convocation, qui laisse les deux traces, jamais en réécrivant la précédente.
- L'avis PDF est le document remis **avant** l'entretien : il ne porte pas la suite, qui n'existe pas encore au moment de son impression.

---

## 19. Infrastructures (Bâtiments & Salles)

**Statut : livré.** Écran `/infrastructures`. Écriture : Directeur, Secrétariat.

### 19.1 Référentiel des locaux

- L'établissement décrit ses **bâtiments**, et les **salles** que chacun contient (nom, capacité).
- La suppression d'un bâtiment ou d'une salle est **logique**.

### 19.2 Règles

- La **lecture est ouverte à tous les rôles** de l'établissement : un enseignant a besoin de connaître les salles pour lire son emploi du temps (§21). Seule l'écriture est restreinte.
- Un établissement qui n'a pas encore renseigné ses locaux voit une **liste vide**, jamais une erreur : le module est facultatif et ne bloque aucun autre.
- La capacité d'une salle est **purement descriptive** : elle est saisie et affichée, mais n'est confrontée à aucun effectif et ne déclenche aucune alerte. Le contrôle des places se fait au niveau de la **classe** (§6.3), pas de la salle. Brancher la capacité de salle sur l'affectation d'un créneau (§21) reste à arbitrer — et devrait alerter plutôt qu'interdire, la décision revenant au Directeur.

---

## 20. Documents administratifs officiels

**Statut : livré.** Huit documents PDF, en complément de ceux déjà spécifiés aux chapitres 6, 7 et 8 (reçu d'inscription, reçu de paiement, certificat de scolarité, bulletin de notes, carte scolaire, billet d'entrée).

### 20.1 Les huit documents

| # | Document | Émis depuis | Famille |
|---|---|---|---|
| 1 | Certificat d'exéat | Inscription | Vie scolaire |
| 2 | Procès-verbal de discipline | Fait disciplinaire (§18) | Vie scolaire |
| 3 | Avis de convocation parent | Convocation (§18) | Vie scolaire |
| 4 | Billet de sortie | Sortie anticipée d'un élève | Vie scolaire |
| 5 | Avis d'échéance / sommation pour impayés | Inscription (§7) | Finance |
| 6 | Engagement financier (reconnaissance de dette) | Inscription (§7) | Finance |
| 7 | Attestation de travail | Contrat employé (§14) | Paie |
| 8 | Fiche de suivi des heures | Contrat employé (§14) | Paie |

### 20.2 Règles de forme

- **Génération côté serveur, mise en page à points fixes A4/A5.** Aucun débordement de page n'est possible, contrairement à une impression pilotée par le navigateur dont le rendu varie d'un poste à l'autre.
- Les documents de la famille **Vie scolaire** portent l'**en-tête officiel M.E.N.** (République du Sénégal / Ministère / Inspection d'Académie / IEF) et un **QR code anti-fraude**. Ceux des familles **Finance** et **Paie** portent l'en-tête de l'établissement.
- Le QR code encode un **identifiant de vérification**, jamais des données personnelles.
- Chaque document est **prévisualisable à l'écran** avant impression.
- Le reçu d'inscription et le bulletin de notes suivent **exactement** les gabarits de `docs/design-references/` — reproduction fidèle, pas d'interprétation créative.

### 20.3 Règles de fond

- Un **engagement financier** ne modifie jamais, à lui seul, le montant dû d'une inscription : c'est une reconnaissance de dette et un échéancier, pas une écriture financière (règle §7.2).
- Un **avis d'échéance** ne peut être émis que s'il existe effectivement un impayé — une sommation pour une dette inexistante est refusée par le système, pas seulement déconseillée.
- Tout document est émis **dans le périmètre de l'établissement courant**. Une fuite d'en-tête ou de données entre établissements est traitée comme un défaut de sécurité, pas comme un défaut d'affichage.

---

## 21. Emploi du temps & Pointage des enseignants

**Statut : livré.**

### 21.1 Emploi du temps

- L'emploi du temps est construit par **créneaux** : jour, heure de début, heure de fin, enseignant, classe, matière, salle.
- Il se consulte **par enseignant** et **par classe**.
- **Détection de chevauchement** à la création comme à la modification : un créneau est refusé s'il recouvre un créneau existant pour le même enseignant ou pour la même classe. Le message précise laquelle des deux contraintes est violée.
- **Jours ouvrés configurables** (Évolution N°3) : le Directeur définit dans Paramètres › Notation & mentions les jours ouvrés de l'établissement (par défaut du lundi au samedi ; par exemple du samedi au mercredi pour une école franco-arabe ou un daara au repos le jeudi et le vendredi). La grille n'affiche que ces jours, dans l'ordre de la semaine de l'école, et un créneau ne peut être créé ni déplacé sur un jour de repos. Un créneau déjà posé sur un jour devenu repos reste visible (colonne marquée « repos ») et peut être supprimé, mais plus modifié.

### 21.2 Qui peut faire quoi

- Le **Directeur**, le **Secrétariat** et le **Super Admin** construisent l'emploi du temps de tout l'établissement.
- Un **Enseignant** ne peut créer, modifier ou supprimer un créneau que **pour lui-même**. Les trois verbes sont couverts : un contrôle qui interdirait de créer un créneau au nom d'un collègue tout en laissant le modifier ou le supprimer ne protégerait rien.
- Un enseignant ne peut pas non plus **réattribuer** son créneau à un collègue, ni s'approprier celui d'un autre.
- Les créneaux proposés par un enseignant restent **distinguables** de ceux posés par l'administration, qui garde ainsi la main sur l'emploi du temps officiel.
- Un compte Enseignant non rattaché à une fiche enseignant est refusé avec un message qui **indique l'action corrective** (demander le rattachement au Directeur), plutôt qu'un refus muet.

### 21.3 Pointage des enseignants

- Le Surveillant général ou le Directeur enregistre la **présence des enseignants** par date.
- Le pointage alimente le suivi d'assiduité du personnel et, pour les vacataires, recoupe la fiche de suivi des heures (§14.3) — sans s'y substituer.
- **Jours de repos** (Évolution N°3) : ni le pointage des enseignants, ni l'**appel des élèves** (ouverture de la feuille comme soumission) ne s'enregistrent un jour de repos de l'établissement. Le verrou ne joue que sur les saisies **nouvelles** : les appels déjà enregistrés un jour devenu repos restent comptés. Aucun calcul de taux de présence n'a changé — ils portent sur les appels réellement saisis, jamais sur des jours calendaires — donc un jour de repos sans appel n'entre dans aucun dénominateur. Les billets d'entrée et de sortie, les justificatifs d'absence, le journal de classe et les heures de paie ne sont pas verrouillés.

---

## 22. Examens officiels (CFEE/BFEM/BAC)

**Statut : livré (Module J du backlog).** Concerne les classes d'examen : CM2 (CFEE), 3ème (BFEM), Terminale et ses séries (BAC).

### 22.1 Sessions et dossiers

- Une **session d'examen** regroupe, pour une année scolaire, un type d'examen (CFEE/BFEM/BAC) et, pour BFEM/BAC, une série/option. Le CFEE n'a pas de série.
- Un **dossier** est ouvert par élève de classe d'examen, rattaché à une session. Un élève n'a qu'un seul dossier par session — un redoublant ouvre un nouveau dossier l'année suivante, l'ancien reste consultable pour l'historique.
- La classe de l'élève est **figée** sur le dossier au moment de son ouverture : un transfert de classe en cours d'année ne doit jamais réécrire un dossier déjà transmis.

### 22.2 Contrôle d'état civil

- Chaque dossier porte la vérification de l'**extrait de naissance** : présence, numéro d'enregistrement, et conformité du nom, prénom, date et lieu de naissance avec l'état civil de l'élève.
- Tant que l'extrait n'est pas déclaré présent, le dossier ne peut pas passer à l'état `Complet`.
- Une non-conformité détectée (ex. orthographe différente entre l'extrait et la fiche élève) se déclare explicitement avec un motif — elle ne bloque pas la saisie mais bloque la transmission tant qu'elle n'est pas résolue.

### 22.3 Audit automatique

- Une vue d'audit relève, pour une session donnée, tous les dossiers `Incomplet` et le détail de ce qui manque : pièce absente, champ non conforme, centre ou numéro de table non attribué.
- Cet audit est le **seul** filtre qui autorise une transmission ou une impression par lot — pas une case cochée manuellement par le Secrétariat.

### 22.4 Centre d'examen et numéro de table

- Le centre d'examen et le **numéro de table** sont attribués dossier par dossier, ou en lot pour toute une session.
- Le numéro de table est **généré dans la transaction d'attribution**, jamais à l'ouverture du dossier — même règle que le matricule (§2).
- Il est unique au sein d'une même session.

### 22.5 Documents et exports

- **Fiche de candidature individuelle** (PDF, prête à signer), imprimable à l'unité ou **par lot** pour une classe ou une session entière. Un lot ne contient que des dossiers `Complet` ou `Transmis`.
- **Export ministériel** (Excel/CSV) au format attendu par l'IEF/l'Inspection d'Académie, filtrable par session.
- **Carte de convocation** (centre, numéro de table, date), individuelle ou dispatchée en lot par SMS/WhatsApp (formule Premium, `Feature.SmsNotifications` — §13) : le canal existant est réutilisé, aucun canal de communication nouveau n'est créé pour ce module.
- Le dispatch de convocations n'est possible qu'une fois le centre et le numéro de table attribués (§22.4).

### 22.6 Résultats et statistiques

- Après délibération, le résultat (admis/non admis), la mention le cas échéant (BFEM/BAC — le CFEE n'attribue pas de mention) et, si transmise, la moyenne obtenue sont saisis sur le dossier.
- Les statistiques (taux de réussite par série, par classe, comparaison interannuelle) ne portent que sur les dossiers `Transmis` ou `Valide` d'une session close — un dossier encore en préparation ne doit jamais fausser un taux de réussite affiché en cours d'année.

---

## 23. Intégration étatique (SIMEN / Planète / STATEDUC)

> **Ce que ce module est, et ce qu'il n'est pas.** Il produit les **pièces et fichiers réglementaires**
> que l'établissement doit à l'administration sénégalaise, dans les formats qu'elle attend. Il ne
> dialogue avec **aucun système du ministère** : à la date de rédaction, **aucune API publique du SIMEN
> n'existe**. Le module prépare des fichiers que l'école transmet par la voie habituelle (dépôt,
> courriel, remise à l'IEF). Le relais API est **spécifié** (§23.2) pour que le jour où il ouvrira, le
> câblage n'oblige pas à réécrire les exports — il n'est pas *implémenté*, et rien dans l'interface ne
> laisse croire le contraire.

### 23.1 Identifiant National de l'Élève (IEN)

L'IEN suit l'élève d'un établissement à l'autre sur tout son parcours. C'est la **clé de rapprochement
de tous les échanges** avec le ministère : sans lui, un élève transféré est un nouvel élève pour
l'administration, et son parcours antérieur est perdu.

- L'IEN est **attribué par l'administration centrale**, jamais par l'école ni par la plateforme.
- Le champ est **facultatif** sur la fiche élève (`Students.IenNumber`). Le rendre obligatoire
  bloquerait l'inscription d'un élève dont le numéro n'est pas encore délivré — ce que le terrain ne
  peut pas se permettre à la rentrée.
- Il est **distinct du matricule**, qui est interne à l'établissement : deux écoles peuvent porter le
  même matricule pour deux élèves différents, jamais le même IEN.
- Unicité garantie **par établissement** (index unique partiel). L'unicité *nationale* ne peut pas
  être vérifiée sans le SIMEN, et la promettre serait un mensonge technique.

**IEN provisoire de secours.** Une école qui n'a reçu aucun numéro peut en faire générer un
algorithmiquement (`P` + code établissement + millésime + séquence + clé de contrôle Luhn).

> **Un IEN provisoire n'a aucune valeur officielle.** Il est marqué comme tel
> (`Students.IsIenProvisional`), signalé ligne par ligne dans l'export Planète, imprimé avec la mention
> « (provisoire) » sur le certificat de mutation, et compté séparément au STATEDUC. Le préfixe `P` le
> rend reconnaissable **à l'œil, sur papier**, par un agent qui n'a accès à aucune base. L'arrivée du
> numéro officiel l'**écrase** sans le conserver : garder deux identifiants pour un même élève, c'est
> garantir qu'un traitement finira par utiliser le mauvais. **L'inverse est interdit** — aucun numéro
> provisoire ne remplace un IEN officiel déjà enregistré.

### 23.2 Export « Planète Ready » et relais SIMEN

Matrice élèves au format d'échange du ministère, exportable en **CSV** (point-virgule, BOM UTF-8) ou
en **JSON**, pour une année scolaire, éventuellement restreinte à une classe.

**Périmètre : l'inscription, pas l'élève.** L'export part des inscriptions non annulées de l'exercice.
Un élève parti en janvier a bien été scolarisé cette année-là et figure au fichier ; un élève créé pour
l'année suivante n'y a pas sa place. Partir des « élèves actuellement en base » ferait les deux erreurs
à la fois. La classe retenue est celle de **l'inscription**, figée — pas la classe courante de l'élève.

**Règles de remplissage, non négociables :**

- **Aucune valeur inventée.** Un champ non saisi part vide. Remplir un IEN manquant par le matricule
  interne, ou un lieu de naissance absent par la ville de l'école, produirait un fichier *plausible et
  faux* — le pire des deux.
- **Aucune donnée financière.** Le ministère reçoit un état civil scolaire, pas la situation de
  paiement d'une famille : la faire sortir de l'établissement ne relève d'aucune obligation légale.
- Le **code établissement national** est obligatoire : sans lui l'export **refuse de s'exécuter**,
  avec un message qui dit quoi corriger et où. Un lot transmis sans ce code est rejeté par le
  ministère, silencieusement et plusieurs jours plus tard — l'école croirait avoir transmis.
- Le fichier annonce ses **compteurs de qualité** (lignes provisoires, lignes sans IEN) **avant**
  téléchargement : une école doit pouvoir renoncer en sachant qu'elle s'apprête à transmettre
  214 identifiants fabriqués.

**Relais API (spécifié, non implémenté).** `ISimenBridgeService` porte deux opérations : transmettre un
lot, et rechercher les IEN officiels d'élèves déjà déclarés. L'implémentation livrée **refuse chaque
appel explicitement** et l'écran n'affiche pas l'action tant qu'aucun point d'accès n'est configuré.
Le jour de l'implémentation réelle : secret en configuration (jamais en base), **webhook entrant signé
HMAC vérifié avant toute écriture** (règle #11 — aucune route ouverte au client ne marque un lot
« Transmis »), et appel journalisé à l'audit.

> **Pourquoi une recherche d'IEN officiels ne s'applique jamais automatiquement.** Faute
> d'identifiant commun préexistant, le rapprochement se fait sur (nom, date de naissance, lieu de
> naissance). C'est fragile — les homonymes sont fréquents. Le résultat doit être **validé par un
> humain** avant d'écrire sur une fiche élève.

### 23.3 Rapport annuel STATEDUC

État statistique transmis en fin d'année. Quatre tableaux réglementaires : effectifs par niveau,
pyramide des âges, qualifications du personnel enseignant, statuts administratifs — plus une synthèse
et un état des infrastructures. Disponible en **PDF A4 paysage** (formulaire à signer et déposer) et en
**classeur `.xlsx`** (consolidation à l'IEF). Les deux sortent du **même calcul** : un second calcul
« pour l'Excel » finirait par diverger du PDF déposé.

**Trois principes gouvernent l'agrégation :**

1. **On compte des inscriptions, pas des élèves en base** (même raison qu'en §23.2).
2. **L'âge est calculé à la date d'observation**, jamais « aujourd'hui ». Sans cela, deux tirages du
   même rapport à six mois d'écart donneraient deux pyramides différentes pour la même année, et
   l'école serait incapable d'expliquer l'écart à l'IEF.
3. **Aucune case n'est devinée.** Un enseignant sans diplôme saisi va dans « non renseigné » ; une date
   de naissance aberrante va dans « âge non déterminé » ; un enseignant sans genre saisi va dans une
   **troisième colonne**, absente du formulaire officiel qui n'en prévoit que deux.

> **Pourquoi cette troisième colonne existe.** Le formulaire ventile tout le personnel en
> Hommes/Femmes. Les fiches enseignant antérieures à ce module ne portent pas le genre. Les imputer à
> l'une des deux colonnes serait faux et *indétectable* ; les déduire du prénom serait faux pour une
> part importante des prénoms sénégalais et faux *en silence*. Une colonne visible est la seule issue
> honnête — et le document imprime un **encadré « Données incomplètes »** qui la nomme, pour que le
> Directeur ne signe pas un formulaire sans voir qu'il est incomplet.

**Qualification.** Est « qualifié » au sens du ministère un enseignant porteur d'un **diplôme
professionnel** (CEAP, CAP, CAEM, CAES). Un titulaire d'un Master sans titre pédagogique n'est pas
qualifié : c'est la définition officielle, pas la nôtre, et l'inverser flatterait l'école au prix d'un
faux. Le taux de qualification est publié **à côté** du nombre d'enseignants dont la qualification
n'est pas saisie — sans quoi un taux de 40 % ne dirait pas s'il décrit l'école ou l'état de sa saisie.

**Nouveaux champs sur la fiche enseignant** : diplôme académique, diplôme professionnel, statut
administratif, matricule de solde (personnels de l'État uniquement), date de première prise de service
(ancienneté dans le métier, pas dans l'établissement), genre.

### 23.4 Examens — carte scolaire et état civil

Trois champs s'ajoutent au dossier d'examen (§22) :

- **Code du centre d'examen** (`ExamCenterCode`), distinct du *nom* du centre : c'est le code — jamais
  le nom — que le ministère utilise pour rapprocher les candidats. Deux centres peuvent porter des noms
  voisins (« Lycée de Mbour », « Lycée de Mbour 2 ») et un nom mal orthographié fait rejeter tout le lot.
- **Numéro de table** (`TableNumber`), la place physique en salle, communiquée par le centre. À ne pas
  confondre avec le **numéro de candidat**, qui est le numéro d'inscription : un candidat garde son
  numéro d'inscription et peut changer de table entre deux épreuves.
- **État du document d'état civil** (`CivilRegistryDocumentStatus`) : non fourni / fourni / conforme /
  non conforme / **en régularisation**. Ce dernier état est la raison d'être du champ : le couple
  booléen existant ne savait pas exprimer la situation la plus fréquente au Sénégal — « fourni, non
  conforme, jugement supplétif en cours ». Un dossier en régularisation était indiscernable d'un
  dossier définitivement non conforme, et l'IEF refusait les deux. Les deux anciens champs sont
  **conservés** : ils sont lus par l'audit de dossier existant.

**Champs réglementaires sur l'établissement** : code établissement national (SIMEN), numéro
d'autorisation ministérielle, code de circonscription scolaire, **coordonnées GPS**.

> Les coordonnées GPS sont stockées en **deux colonnes décimales** (latitude, longitude), pas dans une
> chaîne « lat,lon ». Une coordonnée en texte ne peut être ni validée, ni bornée, ni utilisée dans une
> requête géographique — et les fichiers de carte scolaire réels arrivent tantôt en
> « 14.6928, -17.4467 », tantôt en « 14°41'34"N ». La chaîne d'affichage est **calculée**, jamais
> stockée, pour qu'aucune dérive ne soit possible entre elle et le couple qui fait foi.

### 23.5 Certificat de mutation

Pièce délivrée à l'élève qui quitte l'établissement, exigée par l'école d'accueil avant toute
réinscription. **PDF A4 portrait, une page, avec QR code de vérification.**

**Pourquoi il est persisté** (contrairement à l'attestation d'inscription, générée à la volée) : un QR
qui n'est adossé à rien n'est qu'un ornement. Il faut une ligne en base pour qu'un scan obtienne une
réponse, et pour répondre à « ce certificat a-t-il été révoqué ? ».

- Numéro officiel séquentiel par établissement (`MUT-2026-0007`), généré **dans la transaction** de
  délivrance (règle #3).
- Le QR encode une **URL de vérification**, jamais l'état civil : un QR photographié sur un bureau est
  lisible par n'importe qui. Le point de vérification répond « valide / révoqué / inconnu », jamais par
  les données de l'élève. Le code est **32 caractères d'aléa cryptographique**, pas l'identifiant de la
  ligne — sans quoi la table serait énumérable.
- **Append-only de fait** : un certificat délivré n'est jamais modifié. Une erreur se corrige par
  **révocation** puis nouvelle délivrance. Réécrire une pièce déjà remise au tuteur produirait deux
  documents contradictoires portant le même numéro, dont la version papier ferait foi contre l'école.
- La classe quittée est **figée** à la délivrance, comme sur un dossier d'examen.

**Situation financière : une mention, jamais un montant.** Le certificat indique si l'élève était à
jour au jour de la délivrance — un instantané figé, pas un calcul refait à la lecture.

> **Un solde impayé n'empêche jamais la délivrance.** Refuser un certificat de mutation à un élève
> débiteur revient à le retenir de force dans l'établissement, ce que la réglementation interdit. Et
> le certificat n'affiche **aucun chiffre** : un « reste dû : 45 000 FCFA » deviendrait un instrument
> de pression sur une famille qui déménage, sur un papier qui circule entre des mains qui n'ont pas à
> connaître ses finances.

### 23.6 Livret de compétences

Document APC qui **retrace un parcours** là où le bulletin note une période : pour chaque compétence de
la grille du niveau, le niveau d'acquisition atteint, période par période. C'est la pièce que réclame
l'école d'accueil lors d'une mutation, aux côtés du certificat.

- **A4 portrait, plusieurs pages admises** — contrairement au bulletin. Une grille APC complète
  (40 à 60 compétences) ne tient pas sur une page, et la comprimer la rendrait illisible pour la
  famille, sa première destinataire.
- Le nombre de colonnes de périodes est **variable** (2 semestres ou 3 trimestres selon l'école), lu
  sur la configuration — rien n'est codé en dur.
- La grille vient de la **même configuration d'école** que le bulletin APC (`EvaluationStructure`),
  jamais d'une seconde définition des compétences qui divergerait au premier changement.
- Échelle : **NA** (non acquis) / **ECA** (en cours d'acquisition) / **A** (acquis) / **E** (expert),
  dérivée du pourcentage de réussite. Une **légende est obligatoire** : « ECA » ne veut rien dire pour
  un parent.

> **Une case vide signifie « non évaluée », et rien d'autre.** Imprimer « NA » à la place porterait un
> jugement d'échec que personne n'a formulé — sur le document qui suit l'élève d'école en école.

---

**Fin du Volume 1.**
