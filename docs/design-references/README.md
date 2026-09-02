# Références de design à reproduire fidèlement

**Règle absolue** : ces 3 documents/écrans ne sont **pas des exemples d'inspiration** — ce sont les modèles exacts à reproduire, champ par champ, disposition par disposition. Aucune réinterprétation créative n'est autorisée. Toute divergence entre ces fichiers et une description générique ailleurs dans la documentation (ex. Volume 5) doit être résolue **en faveur de ces références**.

Fichiers : `receipt-reference.png`, `bulletin-reference.png`, `dashboard-reference.jpg` (dans ce même dossier).

---

## 1. Attestation d'inscription & d'admission — voir ticket JGK-E02

> **Cette section fait foi, PAS `receipt-reference.png`.** La maquette d'origine (A4 portrait, bloc unique, intitulée « Reçu d'inscription ») a été remplacée à la demande du client, en trois temps : d'abord un format **A5 paysage** en deux colonnes ; puis une séparation nette entre cette pièce ADMINISTRATIVE et le reçu de caisse, pièce COMPTABLE (§1bis) ; enfin, le **25/08/2026**, la refonte décrite ci-dessous. Le PNG est conservé à titre d'historique — en cas de divergence, c'est le texte ci-dessous qui s'applique.

> **Refonte du 25/08/2026 — pourquoi.** L'attestation affichait le **cumul annuel** (« Mensualité (× 9 mois) = 135 000 », total « 271 000 FCFA », « Reste à payer : 211 000 »). Ce chiffre effrayait les tuteurs sans les informer : il ne leur disait ni ce qu'ils devaient payer aujourd'hui, ni ce qu'ils devraient chaque mois. Le cumul annuel **ne doit plus jamais figurer sur une pièce remise au tuteur**. Il continue d'exister en base (`Enrollment.TotalDue`) et dans les écrans Finance. Maquette validée : artifact « Pièces de caisse repensées ».

> **Qui délivre quoi.** L'attestation est délivrée par le **SECRÉTARIAT, qui n'encaisse aucun fonds** : elle atteste d'une inscription et **annonce** ce qu'il y a à régler. Le règlement s'effectue auprès de la **COMPTABILITÉ**, qui délivre seule le reçu de caisse (§1bis). Aucun recouvrement entre les deux pièces : l'une annonce, l'autre constate.

> **Séparation appliquée au formulaire (30/08/2026).** L'écran `/inscriptions` n'expose plus AUCUNE sélection d'encaissement : plus de cases « ce frais est réglé », plus de mode de règlement, plus de « Encaissé ce jour ». Le panneau, renommé « Frais », est un **aide-calcul sans impact base** (barème lu de la Comptabilité + un simulateur de mensualités purement indicatif). `POST /api/v1/enrollments` n'accepte plus `collectedFees` / `paymentMethod` ; l'inscription fige le dû annuel et ne crée **jamais** de `Payment`. Tout règlement, y compris le premier versement, passe par la Caisse (`POST /finance/payments`). `CollectedLines` / `TotalCollected` / `PaymentMethod` restent au DTO du reçu mais valent toujours vide / 0 / null pour une inscription.

**Format** : **A5 paysage (210 × 148 mm)**, marge de **10 mm** (zone utile 190 × 128 mm), une seule page, sans débordement — à l'écran comme à l'impression et à l'export PDF (`@page { size: A5 landscape; margin: 10mm }` côté web, `PageSizes.A5.Landscape()` + `Margin(10, Unit.Millimetre)` côté QuestPDF). Un test de non-régression vérifie la tenue sur une page avec 8 lignes de frais (`ReceiptPdfGeneratorTests`).

**Disposition :**

1. **En-tête**, séparé du corps par un filet clair : à gauche le logo (omis s'il n'y en a pas, sans cadre témoin) puis le nom de l'école en gras — **plus en majuscules depuis la refonte** —, une ligne de coordonnées (**adresse · téléphone · e-mail**) et une ligne de mentions légales (**NINEA · RCCM**) ; à droite une pile de **badges** : « INSCRIPTION ENREGISTRÉE » (indigo), « N° [référence] », « ANNÉE [année scolaire] ». Chaque mention absente est simplement omise — jamais de séparateur orphelin ni de valeur inventée.
2. **Cartouche d'identité** : panneau gris clair, grille de 2 colonnes, étiquette en capitales au-dessus de sa valeur — Élève, Matricule, Classe & niveau, Tuteur (cellule omise si aucun tuteur n'est renseigné).
3. **Déclaration officielle**, filet vertical à gauche, texte justifié : *« L'administration de l'établissement atteste que l'élève [Nom complet] (matricule [Matricule]) est régulièrement inscrit(e) au sein de notre établissement en classe de [Classe] pour l'année scolaire [Année scolaire]. »*
4. **Corps sur deux colonnes** — deux blocs de nature volontairement différente :
   - **Colonne gauche — « RÈGLEMENT À EFFECTUER »**, précisé *« auprès du service de la comptabilité, ce jour »*. Tableau « Poste à régler / Montant » : pour chaque ligne de frais, son **montant UNITAIRE** (`EnrollmentFeeLine.UnitAmount`) — le frais ponctuel entier (inscription, tenue) ou **UNE SEULE mensualité**, marquée « (1 mois) ». Ligne finale **« TOTAL À RÉGLER »**, à l'encre noire.
   - **Colonne droite — « ÉCHÉANCIER MENSUEL »**, précisé *« à titre indicatif »*. Liste des seuls frais **récurrents**, chacun à son tarif mensuel unitaire (« 15 000 FCFA / mois »), puis un panneau indigo **« TOTAL À PRÉVOIR CHAQUE MOIS »** portant la somme des mensualités unitaires et la mention *« Règlement du 1er au 5 de chaque mois »*. Les frais ponctuels en sont **exclus** : ils ne se répètent pas, les faire figurer dans un échéancier serait un faux.
5. **Bas de page**, séparé par un filet : « Fait à [ville], le [date] » en italique au-dessus de l'emplacement du cachet officiel, puis deux traits de signature — **« LE SECRÉTARIAT »** et « LE DIRECTEUR ». *(Pas « Le Caissier » : le secrétariat n'encaisse pas.)*
6. **Note de pied**, la plus petite ligne du document : *« Le secrétariat n'encaisse aucun fonds : tout règlement s'effectue auprès de la comptabilité, seule habilitée à délivrer un reçu de caisse valant validation définitive. »*

**Interdits, non négociables :**

- **Jamais le cumul annuel** : ni `TotalDue`, ni « × N mois », ni un total multiplié. C'est l'objet même de la refonte.
- **Jamais le « Reste à payer »** annuel. Supprimé, remplacé par le total mensuel.
- **Jamais de vert nulle part.** Le vert signifie « acquitté » (voir la palette §1ter) et cette pièce n'acquitte rien : le total à régler reste **à l'encre noire**. Seul l'échéancier — prospectif — porte l'indigo.
- **Jamais de badge d'état de paiement** (« payé », « en attente »). Réimprimée trois mois plus tard, l'attestation afficherait un état faux : le badge ne qualifie que l'acte administratif, vrai à toute date.
- `CollectedLines` / `TotalCollected` ne sont **plus lus** par ce document : le secrétariat n'encaissant pas, ils valent zéro à l'instant où la pièce est délivrée.

**Règle métier — engagement initial** : le bloc gauche suppose **un mois d'avance**. Une école qui exigerait deux mois, ou seulement les frais d'inscription à la signature, a besoin d'un réglage d'établissement : rien dans le modèle ne porte cette information aujourd'hui. Idem pour « du 1er au 5 de chaque mois », aujourd'hui un texte constant qui devrait rejoindre *Paramètres › Établissement*, comme le NINEA et le RCCM.

**Données à saisir en amont** : les mentions NINEA et RCCM, l'adresse et l'e-mail viennent de *Paramètres › Établissement*.

## 1bis. Reçu de caisse — voir ticket JGK-F02

**Format** : **A5 paysage (210 × 148 mm)**, marge de **10 mm**, une seule page, même en-tête que l'attestation (§1.1) aux badges près. Test de non-régression sur 8 lignes ventilées (`PaymentReceiptPdfGeneratorTests`).

**Philosophie** : document épuré, axé UNIQUEMENT sur le flux de trésorerie de l'instant t — jamais l'état du dossier de l'élève (dû annuel, reste à payer), qui n'a pas sa place ici. C'est la **seule pièce qui atteste d'un encaissement**.

**Disposition :**

1. **En-tête** identique à §1.1, badges : **« PAYÉ » (vert)**, « REÇU N° [référence] », « [date] ».
2. **Cartouche d'identité** (même gabarit qu'en §1.2) : Élève, Matricule, Classe & année, Mode de règlement (Espèces, Chèque, Virement, Mobile Money / Wave / Orange Money).
3. **Bandeau de section « DÉTAIL DU RÈGLEMENT »**, précisé de la période couverte par le versement entier quand elle est renseignée (*« Période de référence : Septembre 2026 »*).
4. **Tableau de règlement VENTILÉ**, trois colonnes **« Désignation / Service » · « Période / Note » · « Montant réglé »** : une ligne par poste imputé (`Payment.Breakdowns`), puis une ligne finale en gras **« TOTAL VERSÉ »**, seul montant en vert du document.
   - **Colonne « Période / Note » — cascade de repli, dans cet ordre** : le libellé propre à la ligne (`PaymentBreakdown.Label`, facultatif, ≤ 60 caractères — « Unique », « 2 jeux ») ; à défaut la période du versement entier (`Payment.ReferencePeriod`) ; à défaut **la cellule reste VIDE**. Jamais un tiret, jamais une période devinée : un reçu n'invente pas la période qu'il atteste.
   - **Garde-fou comptable** : si la ventilation est absente **ou si la somme des lignes ne fait pas exactement le montant encaissé**, le document se replie sur sa forme historique à ligne unique « Versement reçu ». Un tableau dont le détail contredit le total est un faux — mieux vaut moins de détail qu'un document faux.
5. **Texte obligatoire sous le tableau, avant la signature** (à intégrer sur tous les reçus de caisse générés) :
   > *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*
6. **Bas de page** : « Fait à [ville], le [date] » et cachet à gauche, puis deux traits de signature — **« LE CAISSIER »** et « LE DIRECTEUR ».
7. **Note de pied** : *« Ce reçu atteste uniquement des sommes encaissées le [date]. Il ne constitue pas un relevé de compte. »*

**Règle comptable, non négociable** : le reçu n'atteste que de **la somme réellement entrée en caisse le jour même** — jamais du dû annuel cumulé. `TotalDue`, `AlreadyPaid` et `RemainingBalance` restent portés par le DTO pour un usage interne Finance, mais **ne sont pas imprimés**.

## 1ter. Palette des deux pièces A5 — voir `ReceiptTheme.cs`

> **Cette section annule le « noir et blanc, aucune couleur » qui figurait aux §1 et §1bis avant le 25/08/2026.** La couleur a été demandée par le client et validée sur maquette. Elle est **sémantique**, jamais décorative — trois familles, jamais mélangées. Source de vérité unique des valeurs : `src/SamaEcole.Infrastructure/Documents/ReceiptTheme.cs`. Ne jamais régler une couleur « à l'œil » dans un document isolé.

| Rôle | Valeurs | Usage |
|---|---|---|
| **Neutres** | encre `#111827`, texte doux `#374151`, gris `#6B7280`, gris clair `#9CA3AF` | textes, étiquettes |
| **Filets & fonds** | filet `#E5E7EB`, filet appuyé `#D1D5DB`, en-tête de tableau `#F3F4F6`, cartouche `#F9FAFB` | tableaux, panneaux |
| **Acquitté (vert)** | `#1E8E3E` sur `#EAF7EE`, filet `#BFE3CB` | **EXCLUSIVEMENT** ce qui est encaissé : badge « PAYÉ » et « TOTAL VERSÉ » du reçu de caisse. **Jamais sur l'attestation.** |
| **Prospectif (indigo)** | `#4338CA` / `#6366F1` sur `#EEF2FF`, filet `#C7D2FE` | échéancier mensuel, badge d'état administratif |

**Typographie** : la maquette HTML est composée en **Inter** (token `fontFamily.sans` du projet). Le pipeline PDF n'enregistre **aucune police custom** : les documents QuestPDF utilisent sa police par défaut (**Lato**), une grotesque humaniste de proportions voisines. Passer réellement à Inter suppose d'embarquer le fichier de police et de l'enregistrer au démarrage — ce n'est pas fait.

**Écart assumé maquette → PDF** : les badges ont des **coins droits** en PDF, arrondis en HTML (`CornerRadius` n'existe pas dans QuestPDF 2024.10.3), et les traits de signature sont **continus** en PDF, pointillés en HTML. Aucun autre écart.

## 2. Bulletin de notes (`bulletin-reference.png`) — voir ticket JGK-G03

**Disposition, de haut en bas :**

1. En-tête à gauche : hiérarchie administrative sénégalaise sur 3 lignes (Inspection d'Académie, Inspection départementale, nom de l'établissement). En-tête à droite : année scolaire + semestre/trimestre.
2. Titre centré, gras, souligné : **"BULLETIN DE NOTES"**.
3. Ligne d'identité : Prénoms / Nom, puis Né(e) le / à, puis Classe.
4. Ligne : Matricule, Nombre d'élèves de la classe, case "Classe redoublée" (à cocher si applicable).
5. **Tableau des notes**, colonnes exactement dans cet ordre : **Disciplines | Devoir | Composition | Moyenne/20 | Coefficient | Moyenne × Coefficient | T.H | Rang | Appréciation** — une ligne par matière, puis une ligne **TOTAL** (somme des coefficients, somme des moyennes pondérées) et une case Absences.
6. Ligne de synthèse : Moyenne générale /20, Rang, Retards, Absences totales.
7. Ligne de mentions à cocher : Blâme / Avertissement / Tableau d'honneur / Encouragements / Félicitations.

> **Colonne « T.H » — écart ASSUMÉ avec `bulletin-reference.png`.** Le PNG porte « TH » sur *toutes* les lignes, y compris une moyenne de 7,75/20 : un marquage constant ne distingue rien et la colonne y perd son sens. Décision produit (02/09/2026) : « T.H » = **Tableau d'Honneur par matière**, généré automatiquement — « TH » dès que **Moyenne/20 ≥ 14** (transposé au barème du cycle : ≥ 7/10 au primaire), case **vide** sinon (jamais « Non » ni « - », qui feraient lire un échec). Seuil et logique dans `SamaEcole.Application/Grades/SubjectAppreciationScale.cs` — c'est exactement le seuil de l'appréciation « Bon Travail », les deux colonnes ne pouvant pas se contredire.

> **Colonne « Appréciation » — barème DÉDIÉ à 6 niveaux, distinct des mentions de l'école.** Vocabulaire fixe des bulletins sénégalais, reproduit du PNG (« Bon Travail », « A. Bien », « Moyen », « Insuffisant », « Faible ») et généré depuis la Moyenne/20 de chaque matière : **Faible** (< 8) · **Insuffisant** (8–10) · **Moyen** (10–12) · **Assez Bien** (12–14) · **Bon Travail** (14–16) · **Très Bien** (≥ 16). Seuils sur /20, transposés au barème du cycle. Toute matière notée reçoit une appréciation — le barème a un plancher, contrairement à l'échelle de mentions **configurable** de l'école (Excellent…Passable), qui ne qualifie plus que la **moyenne générale** et les lignes des grilles APC. Source : `SubjectAppreciationScale`.

> **Encart du bas (ligne 7) — pré-cochage AUTOMATIQUE, saisie du conseil prioritaire.** À défaut de saisie du conseil des professeurs (`ReportCardRemark.DisciplinaryMention`), la distinction est proposée d'après la **moyenne générale** : **≥ 16 Félicitations**, **≥ 14 Tableau d'honneur**, **≥ 12 Encouragements** (seuils transposés au barème du cycle). **Aucune sanction n'est jamais automatique** : Blâme et Avertissement restent exclusivement la décision du conseil — une division ne prononce pas une sanction disciplinaire. Toute saisie du conseil l'emporte, y compris une sanction sur un excellent bulletin. Logique dans `SamaEcole.Application/ReportCards/DisciplinaryMentionPolicy.cs`.
8. Deux blocs côte à côte : à gauche "Décision du Conseil" (Admis(e) en classe supérieure / Autorisé(e) à redoubler / Exclusion, à cocher) ; à droite le récapitulatif des moyennes par semestre + moyenne annuelle + rang annuel.
9. Bas de page : zone "Observations du conseil des professeurs" avec ligne de signature, et à droite "Le Chef d'établissement" avec emplacement de cachet officiel.

**Format d'impression** : **A5 portrait, une seule page, sans débordement — contrainte stricte, sous test de non-régression** (`ReportCardDocumentTests` : un bulletin à 12 disciplines, une grille APC à 18 lignes, un bulletin primaire /10, une classe passerelle, tous en une page). Cohérent avec la règle déjà posée au Volume 1 §8 (ajustement automatique de l'interligne puis du corps entre 3 et 20 lignes, suppression des décimales inutiles). Corps du tableau des disciplines : **8,5 pt** dans le cas courant (≤ 12 disciplines), plancher à 88 % (~7,5 pt) au-delà.

## 3. Dashboard (`dashboard-reference.jpg`) — écran "Vue d'ensemble des élèves"

**Structure générale :**

- **Barre latérale gauche** : logo + nom de la plateforme en haut, puis navigation verticale par modules (Dashboard, Élèves avec sous-menu Aperçu/Tuteurs/Documents/Dossier santé, Classes, Présences, Pédagogie, Enseignants, Rapports, Finance, Anciens élèves, Paramètres). Profil utilisateur en bas de la barre latérale.
- **Barre supérieure** : fil d'ariane (ex. "Élèves > Aperçu"), barre de recherche globale, icône de notifications.
- **Titre de page** + sous-titre descriptif juste en dessous.
- **4 cartes d'indicateurs clés** en ligne, chacune avec : icône, badge de variation coloré (vert = hausse, rouge = baisse), valeur principale en grand, libellé de comparaison (ex. "vs l'an dernier").
- **Barre de filtres** : recherche, filtres déroulants (Classe, Genre, Statut), bouton "Réinitialiser les filtres", puis à droite les actions principales (bouton d'action primaire coloré "+ Ajouter un élève", boutons secondaires "Importer CSV", "Exporter").
- **Tableau de données** : case à cocher, colonnes triables, avatar + nom complet, badges colorés pour le genre et le statut, menu d'actions (trois points) par ligne avec options : Voir la fiche, Modifier, Assigner une classe, Changer le statut, Archiver, Supprimer (en rouge).
- **Pagination** en bas : nombre total d'éléments affichés, sélecteur de lignes par page, numéros de page, flèches précédent/suivant.

**Couleur principale du thème** : violet/indigo (et non le bleu initialement défini au Volume 5 §2.1 — voir mise à jour de la palette dans Volume_5_UIUX_Design.md §2.1, cette référence fait foi). Style général : cartes aux coins arrondis, ombres légères, badges de statut colorés, police sans-serif moderne.

**Application à tous les modules** : cette structure de dashboard (barre latérale + barre supérieure + cartes KPI + filtres + tableau + pagination) est le **gabarit générique** à réutiliser pour toutes les vues de listes de la plateforme (Enseignants, Classes, Paiements, Inscriptions...), pas seulement pour les élèves — adapter les colonnes et les cartes KPI à chaque module, en conservant la structure et le style identiques.
