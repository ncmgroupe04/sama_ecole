# Références de design à reproduire fidèlement

**Règle absolue** : ces 3 documents/écrans ne sont **pas des exemples d'inspiration** — ce sont les modèles exacts à reproduire, champ par champ, disposition par disposition. Aucune réinterprétation créative n'est autorisée. Toute divergence entre ces fichiers et une description générique ailleurs dans la documentation (ex. Volume 5) doit être résolue **en faveur de ces références**.

Fichiers : `receipt-reference.png`, `bulletin-reference.png`, `dashboard-reference.jpg` (dans ce même dossier).

---

## 1. Reçu d'inscription — voir ticket JGK-E02

> **Cette section fait foi, PAS `receipt-reference.png`.** La maquette d'origine (A4 portrait, bloc unique) a été remplacée à la demande du client : format **A5 paysage**, corps en deux colonnes, mentions légales d'entreprise, et surtout un total qui n'affiche QUE l'encaissement réel. Le PNG est conservé à titre d'historique — en cas de divergence, c'est le texte ci-dessous qui s'applique.

**Format** : **A5 paysage (210 × 148 mm)**, une seule page, sans débordement — à l'écran comme à l'impression et à l'export PDF (`@page { size: A5 landscape }` côté web, `PageSizes.A5.Landscape()` côté QuestPDF).

**Disposition :**

1. **En-tête**, séparé du corps par un filet : à gauche le nom de l'école en gras et majuscules, puis une ligne de coordonnées (**adresse · téléphone · e-mail**) et une ligne de mentions légales (**NINEA · RCCM**) ; à droite l'emplacement du logo officiel. Chaque mention absente est simplement omise — jamais de séparateur orphelin ni de valeur inventée.
2. Titre centré, en gras et italique : **"REÇU D'INSCRIPTION n° [référence]"** (ex. `REC-2025-0002`).
3. **Corps sur deux colonnes** :
   - **Colonne gauche — identité** (étiquette / valeur, une ligne par champ) : Matricule, Nom complet, Classe d'affectation, Année scolaire, Type de mouvement, Date de l'opération, Tuteur, Téléphone du tuteur, **Mode de règlement**.
   - **Colonne droite — ventilation de l'encaissement** : tableau **"Désignation des frais" / "Montant (FCFA)"** avec **une ligne par frais réellement réglé ce jour-là** (droits d'inscription, tenue, carnet, mensualité…), une mensualité portant le nombre de mois couverts — « Mensualité (× 1 mois) ». Ligne finale en gras **"TOTAL ENCAISSÉ"**. Sous le tableau, en petits caractères : rappel des frais annuels et du reste à payer.
4. **Règle comptable, non négociable** : le reçu n'atteste que de **la somme réellement entrée en caisse le jour même** — jamais du dû annuel cumulé. Un dossier ouvert sans versement s'imprime avec « Aucun frais encaissé ce jour » et un total de 0.
5. **Texte obligatoire sous le corps, avant la signature** (à intégrer sur tous les reçus générés) :
   > *"Il est demandé aux parents de garder minutieusement leur reçu après le paiement."*
6. Bas de page, deux colonnes : à gauche "Fait à [ville], le [date]" (italique) au-dessus de l'emplacement du cachet officiel ; à droite "Signature du Directeur / Service Financier" (italique) au-dessus du trait de signature.

**Style** : noir et blanc, sobre, tableaux à bordures simples, aucune couleur — document strictement administratif/imprimable.

**Données à saisir en amont** : les mentions NINEA et RCCM, l'adresse et l'e-mail viennent de *Paramètres › Établissement* ; l'encaissement (frais cochés + mode de règlement) est saisi dans le formulaire d'inscription.

## 2. Bulletin de notes (`bulletin-reference.png`) — voir ticket JGK-G03

**Disposition, de haut en bas :**

1. En-tête à gauche : hiérarchie administrative sénégalaise sur 3 lignes (Inspection d'Académie, Inspection départementale, nom de l'établissement). En-tête à droite : année scolaire + semestre/trimestre.
2. Titre centré, gras, souligné : **"BULLETIN DE NOTES"**.
3. Ligne d'identité : Prénoms / Nom, puis Né(e) le / à, puis Classe.
4. Ligne : Matricule, Nombre d'élèves de la classe, case "Classe redoublée" (à cocher si applicable).
5. **Tableau des notes**, colonnes exactement dans cet ordre : **Disciplines | Devoir | Composition | Moyenne/20 | Coefficient | Moyenne × Coefficient | T.H (Titulaire/Heures) | Rang | Appréciation** — une ligne par matière, puis une ligne **TOTAL** (somme des coefficients, somme des moyennes pondérées) et une case Absences.
6. Ligne de synthèse : Moyenne générale /20, Rang, Retards, Absences totales.
7. Ligne de mentions à cocher : Blâme / Avertissement / Tableau d'honneur / Encouragements / Félicitations.
8. Deux blocs côte à côte : à gauche "Décision du Conseil" (Admis(e) en classe supérieure / Autorisé(e) à redoubler / Exclusion, à cocher) ; à droite le récapitulatif des moyennes par semestre + moyenne annuelle + rang annuel.
9. Bas de page : zone "Observations du conseil des professeurs" avec ligne de signature, et à droite "Le Chef d'établissement" avec emplacement de cachet officiel.

**Format d'impression** : orienté pour tenir sur une seule page, sans débordement, cohérent avec la règle déjà posée au Volume 1 §8 (bulletin A5, ajustement automatique des colonnes, suppression des décimales inutiles).

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
