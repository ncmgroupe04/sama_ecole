# Dette technique — `TagHelperContext.Items` ne restitue pas le contenu d'un Tag Helper enfant à son parent

**Statut :** confirmé, reproduit, cause racine non corrigée — **1 écran sur 22 contourné localement** (`Views/ParentSummons/Index.cshtml`, commit `da8a3d8`, 03/09/2026 : voir « Progrès » ci-dessous). **Sévérité :** haute (défaut visuel silencieux sur les 21 écrans restants). **Trouvé le :** 02-03/09/2026, en construisant `ReceiptA5TagHelper` (factorisation du reçu A5 de `/caisse` et `/inscriptions`).

## Comportement observé

Le sous-titre d'une modale (`<modal-subtitle>…</modal-subtitle>`), le titre HTML libre (`<modal-title>`), le pied de modale (`<modal-footer>`) et l'indication d'une carte statistique (`<stat-hint>`) sont **silencieusement absents** du rendu final, alors que le code source les déclare correctement et qu'ils fonctionnaient visiblement lors de leur écriture initiale (chaque commit qui les introduit mentionne une vérification au navigateur).

Aucune erreur : ni côté serveur (le build réussit, `ProcessAsync` s'exécute sans exception), ni côté client (zéro erreur console, Alpine hydrate normalement le reste de la modale). Le titre principal (`title="…"`, un attribut simple, pas un slot) s'affiche correctement — seul le contenu transmis via un Tag Helper enfant disparaît.

Exemple minimal, sur n'importe quelle page utilisant `modal-shell` (ex. `/classes`, bouton « Nouvelle classe ») :

```html
<modal-shell open="isCreateOpen" title="Nouvelle classe">
    <modal-subtitle>Configurez une nouvelle classe et sa capacité.</modal-subtitle>
    ...
</modal-shell>
```

Rendu obtenu : le bandeau bleu de la modale affiche « Nouvelle classe » (le titre), mais aucune ligne de sous-titre en dessous — la balise `<modal-subtitle>` et son contenu ont disparu sans laisser de trace dans le HTML servi.

## Cause racine

Le patron utilisé par `ModalShellTagHelper` (et copié par `StatCardTagHelper`) fait communiquer un Tag Helper enfant vers son parent via `TagHelperContext.Items` :

```csharp
// Enfant (ex. ModalSubtitleTagHelper)
context.Items[ItemsKey] = (await output.GetChildContentAsync()).GetContent();
output.SuppressOutput();

// Parent (ex. ModalShellTagHelper), APRÈS avoir capturé son propre contenu enfant
var body = (await output.GetChildContentAsync()).GetContent();
var subtitle = context.Items.TryGetValue(ItemsKey, out var value) ? (string)value! : null;
```

L'hypothèse documentée dans le code (commentaire de `ModalShellTagHelper.ProcessAsync`, ligne ~83) est que `context.Items` est **partagé par référence** tout au long de l'arborescence des Tag Helpers d'une même vue — un patron par ailleurs documenté par Microsoft pour exactement ce cas d'usage (communication enfant → ancêtre).

**Vérifié par instrumentation directe** (traçage temporaire avec `Console.WriteLine`, retiré après diagnostic) que ce n'est **pas** le cas sur cet environnement : le Tag Helper enfant et son parent reçoivent chacun une **instance différente** du dictionnaire `Items`.

```
[DEBUG-PROBE] ModalShellTagHelper.ProcessAsync START: context.Items.hash=31364791
[DEBUG-PROBE] ModalSubtitleTagHelper.ProcessAsync: content='SOUS-TITRE-DE-TEST-12345', context.Items.hash=49642866
[DEBUG-PROBE] ModalSubtitleTagHelper: Items now has key=True, count=1
[DEBUG-PROBE] ModalShellTagHelper: after GetChildContentAsync, Items.hash=31364791, count=0, hasSubtitleKey=False
```

L'enfant écrit bien la clé dans **son** dictionnaire (`count=1` juste après l'écriture), mais le parent relit **le sien** (même hash avant et après l'appel à `GetChildContentAsync()`, `count=0`) — les deux ne sont jamais le même objet.

**Testé sur deux SDK .NET installés sur cette machine, le défaut se reproduit sur les deux :**
- SDK `10.0.302` (celui utilisé par défaut ici, en l'absence de `global.json` — le projet cible `net9.0` mais compile avec l'outillage .NET 10)
- SDK `9.0.315` (celui qui correspond exactement au TFM du projet, testé en épinglant temporairement un `global.json` puis un `dotnet build` propre après suppression de `bin/`/`obj/`)

**Ce n'est donc pas un défaut de désaccord entre le SDK installé et le TFM du projet** — hypothèse naturelle vu l'absence de `global.json`, mais écartée par ce test. La cause exacte au sein du pipeline de compilation Razor (génération de code, `TagHelperExecutionContext`, ou une couche entre les deux) n'a pas été creusée plus loin — c'est le sujet du chantier de résolution ci-dessous.

## Surface affectée

Recensée le 03/09/2026 par recherche de toutes les vues utilisant les Tag Helpers concernés. **Aucune de ces occurrences n'affiche son slot** — à vérifier au navigateur avant de commencer le chantier de résolution, l'inventaire ci-dessous pouvant avoir bougé depuis.

| Tag Helper enfant | Fichier(s) TagHelpers | Vues utilisatrices |
|---|---|---|
| `<modal-subtitle>` | `TagHelpers/ModalShellTagHelper.cs` | `Views/Absences/Billets.cshtml`, `Views/Admin/RegistrationRequests.cshtml`, `Views/Buildings/Index.cshtml`, `Views/Classrooms/Index.cshtml`, `Views/Discipline/Index.cshtml`, `Views/Exams/Index.cshtml`, `Views/Fees/Index.cshtml`, `Views/Grades/Index.cshtml`, `Views/Inventory/Index.cshtml`, ~~`Views/ParentSummons/Index.cshtml`~~ **corrigé, voir Progrès**, `Views/Payroll/Index.cshtml`, `Views/Reports/Attendance.cshtml`, `Views/Settings/Index.cshtml`, `Views/Shared/_Layout.cshtml`, `Views/Shared/_SchoolYearsPanel.cshtml`, `Views/Shared/_UsersPanel.cshtml`, `Views/StateIntegration/Index.cshtml`, `Views/Students/Index.cshtml`, `Views/Subjects/Index.cshtml`, `Views/Teachers/Index.cshtml` (19 restants sur 20 d'origine) |
| `<modal-title>` | idem | Sous-ensemble de la liste ci-dessus : `Admin/RegistrationRequests`, `Buildings`, `Classrooms`, `Exams`, `Fees`, `Inventory`, `Shared/_Layout`, `Shared/_UsersPanel`, `Students`, `Subjects`, `Teachers` (11 fichiers) |
| `<modal-footer>` | idem | `Views/Shared/_Layout.cshtml` — partagé par **toutes** les pages de l'application (modale universelle « Accès refusé », voir commit `b308263`) |
| `<stat-hint>` | `TagHelpers/StatCardTagHelper.cs` | `Views/Dashboard/Index.cshtml`, `Views/Taxes/Index.cshtml` (2 fichiers) |

**Non affecté**, pour référence — patron différent, ne dépend pas de `context.Items` : `<row-action>` / `<row-actions>` (`TagHelpers/RowActionsTagHelper.cs`) transmet tout par attributs C# simples (`Icon`, `Label`, `OnClick`…), jamais par contenu enfant capturé.

**Total unique restant : 21 fichiers de vue** (22 d'origine, 1 corrigé — voir Progrès). `_Layout.cshtml` étant partagé, le défaut touche potentiellement une modale sur chaque page de l'application, même celles qui n'apparaissent pas dans le tableau ci-dessus.

## Progrès

**`Views/ParentSummons/Index.cshtml` corrigé le 03/09/2026 (commit `da8a3d8`)**, en application de la Piste 1 ci-dessous mais SANS toucher `ModalShellTagHelper` : les deux `<modal-subtitle>` de cette vue (modale de création, modale « Consigner la suite ») ont été remplacés par un `<div class="mt-2 text-sm text-slate-500">` simple, portant les mêmes classes que le rendu que `ModalShellTagHelper` produit pour un sous-titre — capturé directement par le `GetChildContentAsync()` du parent, sans passer par un Tag Helper enfant. Vérifié au navigateur : les deux sous-titres s'affichent, zéro erreur console, 78 tests JS au vert.

C'est un contournement PAR VUE, pas une correction de la cause racine : `ModalShellTagHelper` reste inchangé, et les 21 autres vues du tableau ci-dessus restent affectées. Un modèle direct à suivre pour les corriger une par une en attendant le chantier de résolution complet (Piste 1).

## Pourquoi la cause racine n'est pas corrigée ici

Découvert en construisant `ReceiptA5TagHelper` (commit `0dae564`, factorisation du reçu A5), qui l'a **contourné localement** — voir sa documentation XML dans `TagHelpers/ReceiptA5TagHelper.cs` — en évitant tout Tag Helper enfant : un seul appel à `GetChildContentAsync()` (celui du composant sur ses propres enfants directs, prouvé fiable) capture tout le contenu, puis un découpage par marqueurs HTML (`<!--badges--> … <!--/badges-->`) en mémoire remplace la communication inter-Tag-Helpers.

Corriger la cause racine touche `ModalShellTagHelper` et `StatCardTagHelper`, utilisés par 21 écrans déjà livrés (22 avant le correctif ponctuel ci-dessus) : le risque de régression sur une refonte à cette échelle dépasse le périmètre d'un correctif ponctuel, et mérite son propre chantier avec revue navigateur systématique des écrans listés ci-dessus.

## Pistes de résolution pour le chantier ultérieur

1. **Découpage par marqueurs HTML**, comme `ReceiptA5TagHelper` : remplacer `<modal-subtitle>…</modal-subtitle>` par un marqueur (`<!--subtitle--> … <!--/subtitle-->`) dans les vues concernées, et réécrire `ModalShellTagHelper.ProcessAsync` pour découper son unique contenu enfant capturé plutôt que lire `context.Items`. Avantage : la technique est déjà écrite et vérifiée (voir `ReceiptA5TagHelper.ExtractMarked`, et le contournement par `<div>` simple déjà validé sur `Views/ParentSummons/Index.cshtml`). Inconvénient : touche les 21 fichiers de vue restants, syntaxe moins lisible qu'une balise nommée.
2. **Isoler et signaler le défaut à l'écosystème .NET** (issue sur `dotnet/aspnetcore`) avant de contourner à grande échelle — si c'est une régression connue et déjà corrigée dans une version de correctif du SDK, un simple bump (avec `global.json` pour fixer la version testée) suffirait, et éviterait de réécrire 22 vues pour un défaut potentiellement déjà résolu en amont.
3. **Repli côté attribut** pour les cas les plus simples (ex. `<stat-hint>`, contenu presque toujours un texte court sans liaison Alpine complexe) : ajouter une propriété `Hint` en texte simple sur `<stat-card hint="…">`, en gardant `<stat-hint>` comme forme HTML libre pour les cas qui en ont réellement besoin. Ne résout pas `modal-subtitle`/`modal-title`/`modal-footer`, dont le contenu porte quasi systématiquement des liaisons Alpine (`x-text`, interpolation), incompatibles avec un attribut de chaîne simple.

Avant de choisir : reproduire l'instrumentation ci-dessus sur une version de SDK plus récente que celles testées ici (`9.0.315` et `10.0.302`), pour vérifier si un correctif amont existe déjà.

## Comment vérifier qu'une correction fonctionne

Ouvrir `/classes`, cliquer sur « Nouvelle classe », et vérifier que le texte « Configurez une nouvelle classe et sa capacité. » apparaît sous le titre « Nouvelle classe ». C'est le cas le plus simple et le plus rapide à vérifier de toute la liste — s'il s'affiche, la correction touche correctement `ModalShellTagHelper`.
