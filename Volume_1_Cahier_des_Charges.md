# JANGALEKAT

# VOLUME 1 — Cahier des Charges Fonctionnel

**Version :** 2.0
**Statut :** Validé — remplace intégralement les versions 1.0 à 1.4
**Changements de cette version :**
- Suppression de toute référence au mode local / LAN / hors connexion (voir Volume 0 v2.0 — l'application est en ligne dès le lancement).
- Fusion et déduplication des différentes « mises à jour » (1.1 à 1.4) en un seul document organisé par domaine fonctionnel, et non plus par ordre chronologique de rédaction.
- Séparation explicite entre exigences **MVP** et exigences **Post-MVP** (renvoi au Volume 1.5, Chapitre 7, pour l'arbitrage définitif).

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

À la soumission, une **demande d'inscription** est créée (statut `Pending`) et une **référence de suivi** est communiquée au Directeur par email. **Aucun compte, aucun établissement, aucun abonnement n'existe encore** à ce stade — le Directeur n'a accès qu'à une page de suivi de sa demande (via la référence + email), rien d'autre.

**Étape 2 — Revue par le Super Admin**
Le Super Admin consulte la liste des demandes en attente, et peut : **Approuver**, **Rejeter** (avec motif), ou demander des précisions par email en dehors de la plateforme.

**Étape 3 — Activation après approbation**
Dès l'approbation, le système crée automatiquement, dans la même transaction : l'établissement (`School`, actif), le compte Directeur (`User`, actif, mot de passe déjà défini à l'étape 1), et l'abonnement (`Subscription`, statut `AwaitingPayment`, aucune date d'expiration tant que le premier paiement n'est pas confirmé). Le Directeur reçoit un email de confirmation et peut se connecter.

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

À l'issue de ce cahier des charges, Jangalekat couvre : administration, scolarité, inscriptions, bulletins, gestion pédagogique, gestion financière, gestion des abonnements multi-écoles, contrôle des accès, statistiques académiques et financières — adapté aux écoles maternelles, primaires, collèges, lycées et centres de formation du Sénégal, opérable entièrement en ligne.

**Fin du Volume 1.**
