# Écran Coran/Franco-Arabe (Phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construire l'écran `/coran` — sélecteur de classe, deux onglets (Suivi de mémorisation / Évaluations orales), modale de saisie/correction avec verrou optimiste — sur l'API `/api/v1/quran` déjà livrée en Phases 1-2. Frontend pur : aucun changement d'API, aucune migration.

**Architecture:** Un seul composant Alpine `coranPage()` (`wwwroot/js/coran.js`) piloté par un sélecteur de classe ; le choix d'une classe déclenche deux requêtes indépendantes (`GET /quran/progress/classroom/{id}` et `GET /quran/evaluations/classroom/{id}`) affichées dans deux onglets. Chaque onglet a sa propre modale de saisie/correction (`POST`/`PUT`, verrou `rowVersion`), sur le patron déjà établi par `internat.js` (modale, 409) et `grades.js` (verrou optimiste, sélecteur de classe). Navigation gardée par un nouveau flag `coranEnabled` sur le store partagé `schoolConfig` (`auth.js`), à l'identique d'`internatEnabled`.

**Tech Stack:** ASP.NET Core 9 Razor Views, Alpine.js, Tailwind (classes utilitaires existantes, aucune nouvelle classe), `node --test` pour les tests JS (`tests/js/*.test.mjs`, harnais `tests/js/harness.mjs`).

**Spec:** `docs/superpowers/specs/2026-09-29-coran-screen-design.md`

## Global Constraints

- Aucun changement d'API/migration — `QuranController` (Phase 2) est consommé tel quel, routes sous `/api/v1/quran`.
- Rôles réutilisés tels quels : écriture (ajouter/corriger) = Directeur + Enseignant ; lecture = + Secrétariat. Aucune garde JS nouvelle au-delà du masquage des boutons — le serveur reste la seule autorité (403/422/409).
- Enums sérialisés en CHAÎNE côté API (`JsonStringEnumConverter` global, `Program.cs:185`) : `QuranMemorizationStatus` voyage en JSON comme `"InProcess"` / `"Memorized"` / `"Revised"`, jamais un entier.
- Verrou optimiste xmin : toute correction (`PUT`) transporte le `rowVersion` lu à l'ouverture de l'entrée ; un 409 est TOUJOURS affiché explicitement, jamais absorbé silencieusement.
- Juz/Hizb/Sourate/Élève (Progress) et Élève (Evaluation) sont IMMUABLES en correction — la modale les affiche en lecture seule en mode édition, jamais un champ éditable qui les enverrait modifiés.
- `SchoolId` ne transite jamais depuis le client — non concerné ici (aucune requête ne le porte, il vient du JWT côté serveur).
- Commits : Conventional Commits, un commit par tâche, chemins EXPLICITES (`git add <fichiers précis>`, jamais `git add -A`) — ce dépôt est un arbre de travail partagé entre plusieurs sessions concurrentes (voir `WORK_IN_PROGRESS.md`).
- Aucune icône nouvelle ajoutée au sprite : `book` (déjà présent, libre) sert d'icône de navigation.

---

### Task 1: `schoolConfig.coranEnabled` (auth.js)

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/auth.js`
- Test: `src/SamaEcole.Web/tests/js/coran.test.mjs` (nouveau fichier — section 1)

**Interfaces:**
- Produces: `Alpine.store('schoolConfig').coranEnabled` (bool, défaut `false`) ; `Alpine.data('sidebarNav').coranEnabled` (getter délégué) — consommés par Task 2 (`_Layout.cshtml`, garde du lien de navigation).

- [ ] **Step 1: Écrire le test de store (échoue — le champ n'existe pas encore)**

Créer `src/SamaEcole.Web/tests/js/coran.test.mjs` :

```javascript
/**
 * Écran Coran/Franco-Arabe (/coran) — spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Section 1 : store `schoolConfig.coranEnabled` (auth.js), sur le modèle exact d'`internatEnabled`
 * (même précaution "sûr par défaut = caché" : module réservé/inerte tant que non activé).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

/** JWT minimal — seul le payload compte, readClaims() ne vérifie jamais la signature. */
function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

/** Charge auth.js avec une session Directeur posée ; `settings` = réponse de GET /schools/current/settings. */
function bootAuth({ settings, failSettings = false } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        if (failSettings) throw new Error('réseau');
                        return settings ? settings() : {};
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (_endpoint, body) => body,
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            pdfPreview: { state: () => ({}) },
            location: { href: 'https://localhost/x', search: '' },
            history: { replaceState() {} }
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Directeur' }), expiresIn: 900 });
    return ctx;
}

test('coranEnabled suit isCoranModuleEnabled du serveur quand vrai', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, true);
});

test('coranEnabled reste false (masqué) tant que la réponse manque ou en cas d\'erreur réseau', async () => {
    const ctx = bootAuth({ failSettings: true });
    const store = ctx.store('schoolConfig');

    assert.equal(store.coranEnabled, false, 'avant la réponse');
    await store.init();
    assert.equal(store.coranEnabled, false, 'après une erreur réseau — sûr par défaut = caché');
});

test('coranEnabled reste false si le serveur omet le champ (ancienne réponse)', async () => {
    const ctx = bootAuth({ settings: () => ({}) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, false);
});

test('sidebarNav.coranEnabled délègue au store partagé', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    await ctx.store('schoolConfig').init();

    const nav = ctx.component('sidebarNav');

    assert.equal(nav.coranEnabled, true);
    assert.deepEqual(plain({ x: nav.coranEnabled }), { x: true });
});
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: FAIL — `store.coranEnabled` est `undefined`, pas `false`/`true` (le champ n'existe pas encore sur le store).

- [ ] **Step 3: Ajouter `coranEnabled` au store `schoolConfig`**

Dans `src/SamaEcole.Web/wwwroot/js/auth.js`, juste après la déclaration `internatEnabled: false,` (dans `Alpine.store('schoolConfig', { ... })`) :

```javascript
        /**
         * Module Coran/Franco-Arabe (Paramètres › Modules) — même précaution qu'internatEnabled
         * ci-dessus : masqué par défaut, sûr par défaut = caché (module réservé/inerte tant que le
         * Directeur ne l'a pas activé).
         */
        coranEnabled: false,
```

Dans la méthode `_load()`, juste après la ligne `this.internatEnabled = !!s && s.isInternatEnabled === true;` :

```javascript
                this.coranEnabled = !!s && s.isCoranModuleEnabled === true;
```

Dans le bloc `catch` de `_load()`, juste après la ligne `this.internatEnabled = false;` :

```javascript
                this.coranEnabled = false;
```

- [ ] **Step 4: Ajouter le getter délégué sur `sidebarNav`**

Dans `src/SamaEcole.Web/wwwroot/js/auth.js`, dans `Alpine.data('sidebarNav', () => ({ ... }))`, juste après `get internatEnabled() { return Alpine.store('schoolConfig').internatEnabled; },` :

```javascript
        get coranEnabled() { return Alpine.store('schoolConfig').coranEnabled; },
```

- [ ] **Step 5: Lancer les tests pour vérifier qu'ils passent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: PASS (4/4)

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/auth.js src/SamaEcole.Web/tests/js/coran.test.mjs
git commit -m "feat(coran): ajoute le flag coranEnabled au store schoolConfig"
```

---

### Task 2: Route, navigation et écran vide (classe → onglets)

**Files:**
- Modify: `src/SamaEcole.Web/Controllers/PagesController.cs`
- Modify: `src/SamaEcole.Web/Views/Shared/_Layout.cshtml`
- Create: `src/SamaEcole.Web/Views/Coran/Index.cshtml`
- Create: `src/SamaEcole.Web/wwwroot/js/coran.js`
- Test: `src/SamaEcole.Web/tests/js/coran.test.mjs` (section 2, ajoutée à la suite)

**Interfaces:**
- Consumes: `GET /classrooms` (déjà utilisé par `grades.js`) — `[{id, name, level, ...}]`.
- Produces: `Alpine.data('coranPage')` avec `classrooms`, `selectedClassroomId`, `activeTab`, `loadingClassrooms`, `error`, `init()`, `selectClassroom(id)` — consommé et étendu par Task 3 (chargement des données par onglet).

- [ ] **Step 1: Ajouter la route**

Dans `src/SamaEcole.Web/Controllers/PagesController.cs`, juste après le bloc de la route `/internat` (`[HttpGet("/internat")] public IActionResult Internat() => View("~/Views/Internat/Index.cshtml");`) :

```csharp
    // Module Coran/Franco-Arabe : suivi de mémorisation et évaluations orales. QuranController garde
    // l'accès ([RequireModule(SchoolModule.Coran)] + rôles) et la RLS isole.
    [HttpGet("/coran")]
    public IActionResult Coran() => View("~/Views/Coran/Index.cshtml");
```

- [ ] **Step 2: Ajouter le lien de navigation**

Dans `src/SamaEcole.Web/Views/Shared/_Layout.cshtml`, juste après le lien `/matieres` (bloc `<a href="/matieres" ...>`), avant sa fermeture ou juste après selon l'ordre déjà en place :

```html
                <a href="/coran" x-show="canView(['Directeur', 'Enseignant', 'Secretariat']) && coranEnabled" class="@(path.StartsWith("/coran") ? LinkActive : LinkIdle)">
                    <icon name="book" class="w-5 h-5" />
                    <span class="text-sm font-medium">Coran</span>
                </a>
```

- [ ] **Step 3: Écrire les tests JS (échouent — coran.js n'existe pas)**

Ajouter à la suite de `src/SamaEcole.Web/tests/js/coran.test.mjs` :

```javascript
// ---------------------------------------------------------------- 2. coranPage() — classe et onglets

const CLASSROOMS = [
    { id: 'c-cm2', name: 'CM2 A', level: 'Primaire' },
    { id: 'c-6a', name: '6e A', level: 'Collège' }
];

/**
 * Charge coran.js avec une API factice qui enregistre chaque appel. `progress`/`evaluations` sont des
 * fonctions : elles reçoivent l'URL demandée et renvoient les lignes de classe (ou lèvent) — un test
 * peut ainsi faire varier la réponse d'un appel à l'autre.
 */
function boot({ role = 'Directeur', progress, evaluations, failWith = {} } = {}) {
    const calls = [];
    const record = (method) => async (endpoint, body) => {
        calls.push({ method, endpoint, body });
        if (failWith[method]) throw failWith[method];
        return { rowVersion: 1 };
    };

    const ctx = loadScripts(['coran.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/classrooms') return CLASSROOMS;
                    if (endpoint.startsWith('/quran/progress/classroom/')) {
                        if (failWith.progress) throw failWith.progress;
                        return progress ? progress(endpoint) : [];
                    }
                    if (endpoint.startsWith('/quran/evaluations/classroom/')) {
                        if (failWith.evaluations) throw failWith.evaluations;
                        return evaluations ? evaluations(endpoint) : [];
                    }
                    return [];
                },
                post: record('POST'),
                put: record('PUT'),
                toMessage: (e, fallback) => (e && e.message) || fallback
            }
        }
    });
    const view = ctx.component('coranPage');
    return { view, calls };
}

test('au chargement : la liste des classes est récupérée', async () => {
    const { view, calls } = boot();
    await view.init();

    assert.equal(view.loadingClassrooms, false);
    assert.deepEqual(plain(view.classrooms.map(c => c.id)), ['c-cm2', 'c-6a']);
    assert.ok(calls.some(c => c.endpoint === '/classrooms'));
});

test('une erreur réseau au chargement des classes renseigne `error`', async () => {
    const { view } = boot();
    view.classrooms = [];
    // Deuxième instance, dont le GET échoue systématiquement.
    const { view: broken } = (() => {
        const ctx = loadScripts(['coran.js'], {
            preload: {
                auth: { role: 'Directeur' },
                api: {
                    get: async () => { throw new Error('réseau'); },
                    post: async () => ({}), put: async () => ({}),
                    toMessage: (_e, fallback) => fallback
                }
            }
        });
        return { view: ctx.component('coranPage') };
    })();
    await broken.init();

    assert.match(broken.error, /classes/i);
});

test('sélectionner une classe pose selectedClassroomId', async () => {
    const { view } = boot();
    await view.init();

    await view.selectClassroom('c-cm2');

    assert.equal(view.selectedClassroomId, 'c-cm2');
});

test('activeTab démarre sur "progress"', async () => {
    const { view } = boot();
    assert.equal(view.activeTab, 'progress');
});
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils échouent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: FAIL — `coran.js` introuvable (`ENOENT`).

- [ ] **Step 5: Créer `coran.js` (squelette)**

Créer `src/SamaEcole.Web/wwwroot/js/coran.js` :

```javascript
/**
 * Écran Coran/Franco-Arabe (/coran) — Phase 3, spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Un sélecteur de classe charge en parallèle GET /quran/progress/classroom/{id} et
 * GET /quran/evaluations/classroom/{id} (une ligne par élève, sa liste d'observations) —
 * QuranController, réservé par [RequireModule(SchoolModule.Coran)] côté serveur. Deux onglets
 * indépendants : l'échec de l'un n'empêche pas l'affichage de l'autre (Task 3).
 *
 * Écriture (ajout/correction) : Directeur + Enseignant (QuranController.WriteRoles). Lecture : +
 * Secrétariat — décision déjà actée en Phase 2, aucune garde JS nouvelle au-delà du masquage des
 * boutons (la vraie garde reste le serveur, 403/422/409).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('coranPage', () => ({
        canWrite: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant',

        classrooms: [],
        selectedClassroomId: '',
        activeTab: 'progress', // 'progress' | 'evaluations'

        loadingClassrooms: true,
        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : échec du chargement des classes.
        error: null,

        async init() {
            this.loadingClassrooms = true;
            this.error = null;
            try {
                this.classrooms = await window.api.get('/classrooms');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des classes.');
            } finally {
                this.loadingClassrooms = false;
            }
        },

        async selectClassroom(id) {
            this.selectedClassroomId = id;
            // Task 3 : déclenche ici le chargement des deux onglets (Promise.all, indépendants).
        }
    }));
});
```

- [ ] **Step 6: Créer `Views/Coran/Index.cshtml` (squelette)**

Créer `src/SamaEcole.Web/Views/Coran/Index.cshtml` :

```html
@{
    ViewData["Title"] = "Coran";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
    <script src="~/js/coran.js" asp-append-version="true"></script>
}

@* Pas de x-init="init()" : Alpine appelle DÉJÀ tout seul la méthode init() d'un composant x-data —
   l'ajouter déclencherait GET /classrooms deux fois à chaque ouverture (même précaution qu'Internat). *@
<div x-data="coranPage()">
    <div class="page-header">
        <div>
            <h1 class="page-title">Coran</h1>
            <p class="page-subtitle">Suivi de mémorisation et évaluations orales, par classe.</p>
        </div>
    </div>

    @await Html.PartialAsync("_ErrorBanner")

    <div class="card mt-4 p-4">
        <label for="coran-classroom" class="form-label">Classe</label>
        <select-field id="coran-classroom" model="selectedClassroomId" on-change="selectClassroom(selectedClassroomId)"
                      placeholder="Sélectionnez une classe…"
                      options-expr="classrooms.map(c => ({value: c.id, label: c.name + ' — ' + c.level}))" />
    </div>

    <div x-show="!selectedClassroomId" x-cloak class="card mt-4">
        <empty-state icon="book" title="Choisissez une classe."
                     hint="Le suivi de mémorisation et les évaluations orales apparaissent une fois la classe choisie." />
    </div>

    <div x-show="selectedClassroomId" x-cloak class="mt-4">
        <div class="flex gap-2 border-b border-slate-200">
            <button type="button" x-on:click="activeTab = 'progress'"
                    :class="activeTab === 'progress' ? 'border-primary text-primary' : 'border-transparent text-slate-500 hover:text-slate-700'"
                    class="px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors">
                Suivi de mémorisation
            </button>
            <button type="button" x-on:click="activeTab = 'evaluations'"
                    :class="activeTab === 'evaluations' ? 'border-primary text-primary' : 'border-transparent text-slate-500 hover:text-slate-700'"
                    class="px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors">
                Évaluations orales
            </button>
        </div>

        @* Task 3 remplace ces deux panneaux par les tableaux réels (chargement, vide, lignes). *@
        <div x-show="activeTab === 'progress'" x-cloak class="card mt-4 p-6">
            <p class="text-sm text-slate-400">Suivi de mémorisation — à venir (Task 3).</p>
        </div>
        <div x-show="activeTab === 'evaluations'" x-cloak class="card mt-4 p-6">
            <p class="text-sm text-slate-400">Évaluations orales — à venir (Task 3).</p>
        </div>
    </div>
</div>
```

- [ ] **Step 7: Lancer les tests pour vérifier qu'ils passent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: PASS (8/8 — les 4 du Task 1 + les 4 ci-dessus)

- [ ] **Step 8: Vérifier la compilation .NET**

Run: `dotnet build sama_ecole/SamaEcole.sln` (ou `dotnet build` depuis la racine du dépôt selon l'emplacement du `.sln`)
Expected: Build réussi, aucune erreur dans `PagesController.cs` ni `Views/Coran/Index.cshtml`.

- [ ] **Step 9: Vérifier au navigateur**

Skill `run` : se connecter en Directeur sur une école avec `IsCoranModuleEnabled = true` (Paramètres › Modules), vérifier que « Coran » apparaît dans la sidebar, ouvrir `/coran`, choisir une classe, vérifier que les deux onglets basculent.

- [ ] **Step 10: Commit**

```bash
git add src/SamaEcole.Web/Controllers/PagesController.cs src/SamaEcole.Web/Views/Shared/_Layout.cshtml src/SamaEcole.Web/Views/Coran/Index.cshtml src/SamaEcole.Web/wwwroot/js/coran.js src/SamaEcole.Web/tests/js/coran.test.mjs
git commit -m "feat(coran): route /coran, navigation et selecteur de classe"
```

---

### Task 3: Chargement des données — deux onglets en lecture seule

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/coran.js`
- Modify: `src/SamaEcole.Web/Views/Coran/Index.cshtml`
- Test: `src/SamaEcole.Web/tests/js/coran.test.mjs` (section 3, ajoutée à la suite)

**Interfaces:**
- Consumes: `GET /quran/progress/classroom/{classroomId}` → `ClassQuranProgressRowDto[]` (`{studentId, matricule, fullName, entries: [{id, studentId, juzNumber, hizbNumber, surahNumber, status, evaluationDate, notes, rowVersion}]}`) ; `GET /quran/evaluations/classroom/{classroomId}` → `ClassQuranEvaluationRowDto[]` (`{studentId, matricule, fullName, entries: [{id, studentId, evaluationDate, memoryMistakes, tajwidMistakes, hesitations, finalScore, rowVersion}]}`).
- Produces: `progressRows`, `evaluationRows`, `loadingProgress`, `loadingEvaluations`, `progressError`, `evaluationError`, `loadProgress()`, `loadEvaluations()`, `toggleExpand(row)`, `sortedProgressEntries(entries)`, `sortedEvaluationEntries(entries)`, `latestProgress(row)`, `latestEvaluation(row)`, `statusLabel(status)` — consommés par Task 4/5 (modales) et par le balisage de ce Task.

- [ ] **Step 1: Écrire les tests (échouent — les méthodes n'existent pas)**

Ajouter à la suite de `src/SamaEcole.Web/tests/js/coran.test.mjs` :

```javascript
// ---------------------------------------------------------------- 3. Chargement et tri des onglets

function progressRow(overrides = {}) {
    return {
        studentId: 's-1', matricule: 'ELEV-0001', fullName: 'Awa Fall',
        entries: [
            { id: 'p-1', studentId: 's-1', juzNumber: 1, hizbNumber: 1, surahNumber: 1, status: 'InProcess', evaluationDate: '2026-09-10', notes: null, rowVersion: 1 },
            { id: 'p-2', studentId: 's-1', juzNumber: 2, hizbNumber: 3, surahNumber: 10, status: 'Memorized', evaluationDate: '2026-09-20', notes: 'Bien', rowVersion: 1 }
        ],
        ...overrides
    };
}

function evaluationRow(overrides = {}) {
    return {
        studentId: 's-1', matricule: 'ELEV-0001', fullName: 'Awa Fall',
        entries: [
            { id: 'e-1', studentId: 's-1', evaluationDate: '2026-09-05', memoryMistakes: 2, tajwidMistakes: 1, hesitations: 0, finalScore: 15, rowVersion: 1 },
            { id: 'e-2', studentId: 's-1', evaluationDate: '2026-09-18', memoryMistakes: 0, tajwidMistakes: 0, hesitations: 1, finalScore: 18, rowVersion: 1 }
        ],
        ...overrides
    };
}

test('choisir une classe charge les deux onglets en parallèle', async () => {
    const { view, calls } = boot({ progress: () => [progressRow()], evaluations: () => [evaluationRow()] });
    await view.init();

    await view.selectClassroom('c-cm2');

    assert.ok(calls.some(c => c.endpoint === '/quran/progress/classroom/c-cm2'));
    assert.ok(calls.some(c => c.endpoint === '/quran/evaluations/classroom/c-cm2'));
    assert.equal(view.progressRows.length, 1);
    assert.equal(view.evaluationRows.length, 1);
});

test('un onglet en échec n\'empêche pas l\'autre de s\'afficher', async () => {
    const { view } = boot({
        evaluations: () => [evaluationRow()],
        failWith: { progress: Object.assign(new Error('panne'), { status: 500 }) }
    });
    await view.init();

    await view.selectClassroom('c-cm2');

    assert.match(view.progressError, /mémorisation/i);
    assert.equal(view.evaluationError, null);
    assert.equal(view.evaluationRows.length, 1);
});

test('sortedProgressEntries trie par date décroissante, une entrée SANS date en dernier', () => {
    const { view } = boot();
    const entries = [
        { id: 'a', evaluationDate: '2026-09-10' },
        { id: 'b', evaluationDate: null },
        { id: 'c', evaluationDate: '2026-09-20' }
    ];

    assert.deepEqual(plain(view.sortedProgressEntries(entries).map(e => e.id)), ['c', 'a', 'b']);
});

test('latestProgress renvoie l\'entrée la plus récente, null si la liste est vide', () => {
    const { view } = boot();

    assert.equal(view.latestProgress(progressRow()).id, 'p-2');
    assert.equal(view.latestProgress(progressRow({ entries: [] })), null);
});

test('sortedEvaluationEntries trie par date décroissante (EvaluationDate toujours renseignée)', () => {
    const { view } = boot();
    const entries = [
        { id: 'a', evaluationDate: '2026-09-05' },
        { id: 'b', evaluationDate: '2026-09-18' }
    ];

    assert.deepEqual(plain(view.sortedEvaluationEntries(entries).map(e => e.id)), ['b', 'a']);
});

test('latestEvaluation renvoie l\'entrée la plus récente, null si la liste est vide', () => {
    const { view } = boot();

    assert.equal(view.latestEvaluation(evaluationRow()).id, 'e-2');
    assert.equal(view.latestEvaluation(evaluationRow({ entries: [] })), null);
});

test('statusLabel traduit les trois statuts et retombe sur la valeur brute sinon', () => {
    const { view } = boot();

    assert.equal(view.statusLabel('InProcess'), 'En cours');
    assert.equal(view.statusLabel('Memorized'), 'Mémorisé');
    assert.equal(view.statusLabel('Revised'), 'Révisé');
    assert.equal(view.statusLabel('Autre'), 'Autre');
});

test('toggleExpand bascule l\'état déplié d\'une ligne', () => {
    const { view } = boot();
    const row = { expanded: false };

    view.toggleExpand(row);
    assert.equal(row.expanded, true);
    view.toggleExpand(row);
    assert.equal(row.expanded, false);
});

test('changer de classe recharge les deux onglets pour la nouvelle classe', async () => {
    const { view, calls } = boot({ progress: () => [progressRow()], evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');

    await view.selectClassroom('c-6a');

    assert.ok(calls.some(c => c.endpoint === '/quran/progress/classroom/c-6a'));
    assert.ok(calls.some(c => c.endpoint === '/quran/evaluations/classroom/c-6a'));
});
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: FAIL — `view.progressRows`/`sortedProgressEntries`/etc. sont `undefined`.

- [ ] **Step 3: Étendre `coran.js`**

Dans `src/SamaEcole.Web/wwwroot/js/coran.js`, remplacer le corps de `selectClassroom` :

```javascript
        async selectClassroom(id) {
            this.selectedClassroomId = id;
            if (!id) {
                this.progressRows = [];
                this.evaluationRows = [];
                return;
            }
            // Indépendants : un onglet en échec n'empêche pas l'affichage de l'autre.
            await Promise.all([this.loadProgress(), this.loadEvaluations()]);
        },
```

Puis ajouter, juste après `selectClassroom`, avant le `}));` final :

```javascript
        progressRows: [],
        loadingProgress: false,
        progressError: null,

        evaluationRows: [],
        loadingEvaluations: false,
        evaluationError: null,

        async loadProgress() {
            if (!this.selectedClassroomId) return;
            this.loadingProgress = true;
            this.progressError = null;
            try {
                const rows = await window.api.get(`/quran/progress/classroom/${this.selectedClassroomId}`);
                this.progressRows = rows.map(r => ({ ...r, expanded: false }));
            } catch (err) {
                this.progressError = window.api.toMessage(err, 'Erreur lors du chargement du suivi de mémorisation.');
            } finally {
                this.loadingProgress = false;
            }
        },

        async loadEvaluations() {
            if (!this.selectedClassroomId) return;
            this.loadingEvaluations = true;
            this.evaluationError = null;
            try {
                const rows = await window.api.get(`/quran/evaluations/classroom/${this.selectedClassroomId}`);
                this.evaluationRows = rows.map(r => ({ ...r, expanded: false }));
            } catch (err) {
                this.evaluationError = window.api.toMessage(err, 'Erreur lors du chargement des évaluations orales.');
            } finally {
                this.loadingEvaluations = false;
            }
        },

        toggleExpand(row) {
            row.expanded = !row.expanded;
        },

        /** Entrées triées par date décroissante ; une entrée SANS date (QuranProgress.evaluationDate
         *  nullable) passe en dernier — jamais confondue avec la plus récente. */
        sortedProgressEntries(entries) {
            return [...entries].sort((a, b) => {
                if (!a.evaluationDate && !b.evaluationDate) return 0;
                if (!a.evaluationDate) return 1;
                if (!b.evaluationDate) return -1;
                return b.evaluationDate.localeCompare(a.evaluationDate);
            });
        },

        /** QuranEvaluation.evaluationDate est TOUJOURS renseignée (champ requis, pas nullable) : tri
         *  direct, sans le cas "sans date" de sortedProgressEntries. */
        sortedEvaluationEntries(entries) {
            return [...entries].sort((a, b) => b.evaluationDate.localeCompare(a.evaluationDate));
        },

        latestProgress(row) {
            const sorted = this.sortedProgressEntries(row.entries);
            return sorted.length > 0 ? sorted[0] : null;
        },

        latestEvaluation(row) {
            const sorted = this.sortedEvaluationEntries(row.entries);
            return sorted.length > 0 ? sorted[0] : null;
        },

        statusLabel(status) {
            return { InProcess: 'En cours', Memorized: 'Mémorisé', Revised: 'Révisé' }[status] || status;
        },
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: PASS (17/17)

- [ ] **Step 5: Remplacer les panneaux placeholder par les tableaux réels**

Dans `src/SamaEcole.Web/Views/Coran/Index.cshtml`, remplacer les deux `<div>` marqués « à venir (Task 3) » par :

```html
        <div x-show="activeTab === 'progress'" x-cloak class="mt-4">
            <div x-show="loadingProgress" x-cloak class="card p-12 flex items-center justify-center">
                <svg aria-hidden="true" class="animate-spin h-8 w-8 text-primary" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>
            </div>
            <div x-show="progressError" x-cloak class="card p-4 border-l-4 border-danger bg-danger-bg">
                <p class="text-sm text-danger" x-text="progressError"></p>
            </div>
            <div x-show="!loadingProgress && !progressError && progressRows.length === 0" x-cloak class="card">
                <empty-state icon="users" title="Cette classe ne compte aucun élève."
                             hint="Inscrivez des élèves dans cette classe pour commencer le suivi." />
            </div>
            <div x-show="!loadingProgress && !progressError && progressRows.length > 0" x-cloak class="card relative">
                <div class="overflow-x-auto">
                    <table class="w-full text-left border-collapse">
                        <thead>
                            <tr class="table-head">
                                <th class="py-3 px-4 whitespace-nowrap">Matricule</th>
                                <th class="py-3 px-4">Élève</th>
                                <th class="py-3 px-4">Dernière observation</th>
                                <th class="py-3 px-4 w-32"></th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-slate-100">
                            <template x-for="row in progressRows" :key="row.studentId">
                                <template x-if="true">
                                    <tr class="hover:bg-slate-50 transition-colors duration-150">
                                        <td class="py-2 px-4 text-xs font-mono text-slate-500 whitespace-nowrap" x-text="row.matricule"></td>
                                        <td class="py-2 px-4 font-medium text-slate-900" x-text="row.fullName"></td>
                                        <td class="py-2 px-4 text-sm">
                                            <template x-if="latestProgress(row)">
                                                <span>
                                                    Juz <span x-text="latestProgress(row).juzNumber"></span> —
                                                    <span x-text="statusLabel(latestProgress(row).status)"></span>
                                                </span>
                                            </template>
                                            <span x-show="!latestProgress(row)" class="text-slate-400">Aucune observation</span>
                                        </td>
                                        <td class="py-2 px-4 text-right">
                                            <button type="button" x-show="row.entries.length > 0" x-cloak
                                                    class="text-xs font-medium text-primary-600 hover:text-primary-700"
                                                    x-on:click="toggleExpand(row)">
                                                <span x-text="row.expanded ? 'Masquer' : 'Historique (' + row.entries.length + ')'"></span>
                                            </button>
                                        </td>
                                    </tr>
                                </template>
                            </template>
                            <template x-for="row in progressRows" :key="row.studentId + '-history'">
                                <tr x-show="row.expanded" x-cloak class="bg-slate-50/60">
                                    <td colspan="4" class="p-4">
                                        <table class="w-full text-sm">
                                            <thead>
                                                <tr class="text-xs text-slate-400">
                                                    <th class="text-left py-1 pr-2">Juz</th>
                                                    <th class="text-left py-1 pr-2">Hizb</th>
                                                    <th class="text-left py-1 pr-2">Sourate</th>
                                                    <th class="text-left py-1 pr-2">Statut</th>
                                                    <th class="text-left py-1 pr-2">Date</th>
                                                    <th class="text-left py-1 pr-2">Notes</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <template x-for="entry in sortedProgressEntries(row.entries)" :key="entry.id">
                                                    <tr class="border-t border-slate-100">
                                                        <td class="py-1 pr-2" x-text="entry.juzNumber"></td>
                                                        <td class="py-1 pr-2" x-text="entry.hizbNumber"></td>
                                                        <td class="py-1 pr-2" x-text="entry.surahNumber"></td>
                                                        <td class="py-1 pr-2" x-text="statusLabel(entry.status)"></td>
                                                        <td class="py-1 pr-2" x-text="entry.evaluationDate || '—'"></td>
                                                        <td class="py-1 pr-2" x-text="entry.notes || ''"></td>
                                                    </tr>
                                                </template>
                                            </tbody>
                                        </table>
                                    </td>
                                </tr>
                            </template>
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
        <div x-show="activeTab === 'evaluations'" x-cloak class="mt-4">
            <div x-show="loadingEvaluations" x-cloak class="card p-12 flex items-center justify-center">
                <svg aria-hidden="true" class="animate-spin h-8 w-8 text-primary" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>
            </div>
            <div x-show="evaluationError" x-cloak class="card p-4 border-l-4 border-danger bg-danger-bg">
                <p class="text-sm text-danger" x-text="evaluationError"></p>
            </div>
            <div x-show="!loadingEvaluations && !evaluationError && evaluationRows.length === 0" x-cloak class="card">
                <empty-state icon="users" title="Cette classe ne compte aucun élève."
                             hint="Inscrivez des élèves dans cette classe pour commencer les évaluations." />
            </div>
            <div x-show="!loadingEvaluations && !evaluationError && evaluationRows.length > 0" x-cloak class="card relative">
                <div class="overflow-x-auto">
                    <table class="w-full text-left border-collapse">
                        <thead>
                            <tr class="table-head">
                                <th class="py-3 px-4 whitespace-nowrap">Matricule</th>
                                <th class="py-3 px-4">Élève</th>
                                <th class="py-3 px-4">Dernière évaluation</th>
                                <th class="py-3 px-4 w-32"></th>
                            </tr>
                        </thead>
                        <tbody class="divide-y divide-slate-100">
                            <template x-for="row in evaluationRows" :key="row.studentId">
                                <tr class="hover:bg-slate-50 transition-colors duration-150">
                                    <td class="py-2 px-4 text-xs font-mono text-slate-500 whitespace-nowrap" x-text="row.matricule"></td>
                                    <td class="py-2 px-4 font-medium text-slate-900" x-text="row.fullName"></td>
                                    <td class="py-2 px-4 text-sm">
                                        <template x-if="latestEvaluation(row)">
                                            <span>
                                                <span x-text="latestEvaluation(row).evaluationDate"></span> —
                                                note <span x-text="latestEvaluation(row).finalScore"></span>
                                            </span>
                                        </template>
                                        <span x-show="!latestEvaluation(row)" class="text-slate-400">Aucune évaluation</span>
                                    </td>
                                    <td class="py-2 px-4 text-right">
                                        <button type="button" x-show="row.entries.length > 0" x-cloak
                                                class="text-xs font-medium text-primary-600 hover:text-primary-700"
                                                x-on:click="toggleExpand(row)">
                                            <span x-text="row.expanded ? 'Masquer' : 'Historique (' + row.entries.length + ')'"></span>
                                        </button>
                                    </td>
                                </tr>
                            </template>
                            <template x-for="row in evaluationRows" :key="row.studentId + '-history'">
                                <tr x-show="row.expanded" x-cloak class="bg-slate-50/60">
                                    <td colspan="4" class="p-4">
                                        <table class="w-full text-sm">
                                            <thead>
                                                <tr class="text-xs text-slate-400">
                                                    <th class="text-left py-1 pr-2">Date</th>
                                                    <th class="text-left py-1 pr-2">Erreurs mémoire</th>
                                                    <th class="text-left py-1 pr-2">Erreurs tajwid</th>
                                                    <th class="text-left py-1 pr-2">Hésitations</th>
                                                    <th class="text-left py-1 pr-2">Note</th>
                                                </tr>
                                            </thead>
                                            <tbody>
                                                <template x-for="entry in sortedEvaluationEntries(row.entries)" :key="entry.id">
                                                    <tr class="border-t border-slate-100">
                                                        <td class="py-1 pr-2" x-text="entry.evaluationDate"></td>
                                                        <td class="py-1 pr-2" x-text="entry.memoryMistakes"></td>
                                                        <td class="py-1 pr-2" x-text="entry.tajwidMistakes"></td>
                                                        <td class="py-1 pr-2" x-text="entry.hesitations"></td>
                                                        <td class="py-1 pr-2" x-text="entry.finalScore"></td>
                                                    </tr>
                                                </template>
                                            </tbody>
                                        </table>
                                    </td>
                                </tr>
                            </template>
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
```

- [ ] **Step 6: Vérifier la compilation .NET**

Run: `dotnet build sama_ecole/SamaEcole.sln`
Expected: Build réussi.

- [ ] **Step 7: Vérifier au navigateur**

Skill `run` : ouvrir `/coran`, choisir une classe avec des élèves, vérifier les deux onglets (tableau vide si aucune observation saisie côté API — normal, la saisie arrive au Task 4/5), déplier/replier l'historique d'une ligne si des données existent déjà en base de test.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/coran.js src/SamaEcole.Web/Views/Coran/Index.cshtml src/SamaEcole.Web/tests/js/coran.test.mjs
git commit -m "feat(coran): chargement et affichage des deux onglets (lecture seule)"
```

---

### Task 4: Modale Suivi de mémorisation (ajout + correction)

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/coran.js`
- Modify: `src/SamaEcole.Web/Views/Coran/Index.cshtml`
- Test: `src/SamaEcole.Web/tests/js/coran.test.mjs` (section 4, ajoutée à la suite)

**Interfaces:**
- Consumes: `POST /quran/progress` (`{studentId, juzNumber, hizbNumber, surahNumber, status, evaluationDate, notes}` → `QuranProgressDto`), `PUT /quran/progress/{id}` (`{status, evaluationDate, notes, rowVersion}` → `QuranProgressDto`).
- Produces: `progressModal` (state), `openCreateProgressModal(row)`, `openEditProgressModal(row, entry)`, `closeProgressModal()`, `progressValidationError()`, `saveProgress()` — consommés uniquement par le balisage de ce Task.

- [ ] **Step 1: Écrire les tests (échouent — la modale n'existe pas)**

Ajouter à la suite de `src/SamaEcole.Web/tests/js/coran.test.mjs` :

```javascript
// ---------------------------------------------------------------- 4. Modale Suivi de mémorisation

test('openCreateProgressModal pose un état neuf pour l\'élève choisi', async () => {
    const { view } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');

    view.openCreateProgressModal(view.progressRows[0]);

    assert.equal(view.progressModal.open, true);
    assert.equal(view.progressModal.mode, 'create');
    assert.equal(view.progressModal.studentId, 's-1');
    assert.equal(view.progressModal.studentName, 'Awa Fall');
    assert.equal(view.progressModal.juzNumber, '');
    assert.equal(view.progressModal.status, 'InProcess');
    assert.equal(view.progressModal.error, null);
});

test('openEditProgressModal pré-remplit depuis l\'entrée choisie', async () => {
    const { view } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.progressRows[0].entries[1]; // p-2 : Memorized

    view.openEditProgressModal(view.progressRows[0], entry);

    assert.equal(view.progressModal.mode, 'edit');
    assert.equal(view.progressModal.entryId, 'p-2');
    assert.equal(view.progressModal.juzNumber, 2);
    assert.equal(view.progressModal.status, 'Memorized');
    assert.equal(view.progressModal.notes, 'Bien');
    assert.equal(view.progressModal.rowVersion, 1);
});

test('création : bornes Juz/Hizb/Sourate refusées AVANT tout appel réseau', async () => {
    const { view, calls } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateProgressModal(view.progressRows[0]);
    view.progressModal.juzNumber = '31';
    view.progressModal.hizbNumber = '1';
    view.progressModal.surahNumber = '1';

    await view.saveProgress();

    assert.match(view.progressModal.error, /juz/i);
    assert.equal(calls.filter(c => c.method === 'POST').length, 0);
});

test('création : envoie POST /quran/progress avec les champs saisis', async () => {
    const { view, calls } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateProgressModal(view.progressRows[0]);
    view.progressModal.juzNumber = '5';
    view.progressModal.hizbNumber = '9';
    view.progressModal.surahNumber = '20';
    view.progressModal.status = 'Memorized';
    view.progressModal.notes = 'Récité sans erreur';

    await view.saveProgress();

    const post = calls.find(c => c.method === 'POST' && c.endpoint === '/quran/progress');
    assert.deepEqual(post.body, {
        studentId: 's-1', juzNumber: 5, hizbNumber: 9, surahNumber: 20,
        status: 'Memorized', evaluationDate: null, notes: 'Récité sans erreur'
    });
    assert.equal(view.progressModal.open, false, 'la modale se ferme après succès');
});

test('correction : envoie PUT avec le rowVersion, SANS Juz/Hizb/Sourate/Élève', async () => {
    const { view, calls } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.progressRows[0].entries[0]; // p-1, rowVersion 1
    view.openEditProgressModal(view.progressRows[0], entry);
    view.progressModal.status = 'Revised';
    view.progressModal.notes = 'Révisé en classe';

    await view.saveProgress();

    const put = calls.find(c => c.method === 'PUT' && c.endpoint === '/quran/progress/p-1');
    assert.deepEqual(put.body, { status: 'Revised', evaluationDate: '2026-09-10', notes: 'Révisé en classe', rowVersion: 1 });
});

test('409 à la correction : message explicite, la modale reste ouverte', async () => {
    const conflict = Object.assign(new Error('conflit'), { status: 409 });
    const { view } = boot({ progress: () => [progressRow()], failWith: { PUT: conflict } });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.progressRows[0].entries[0];
    view.openEditProgressModal(view.progressRows[0], entry);

    await view.saveProgress();

    assert.match(view.progressModal.error, /modifiée par un autre utilisateur/);
    assert.equal(view.progressModal.open, true);
});

test('closeProgressModal ferme la modale', async () => {
    const { view } = boot({ progress: () => [progressRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateProgressModal(view.progressRows[0]);

    view.closeProgressModal();

    assert.equal(view.progressModal.open, false);
});
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: FAIL — `view.progressModal`/`openCreateProgressModal`/etc. sont `undefined`.

- [ ] **Step 3: Ajouter la modale à `coran.js`**

Dans `src/SamaEcole.Web/wwwroot/js/coran.js`, ajouter juste avant le `}));` final :

```javascript
        // --- Modale Suivi de mémorisation --------------------------------------------------

        progressModal: { open: false, mode: 'create', studentId: null, studentName: '', entryId: null, juzNumber: '', hizbNumber: '', surahNumber: '', status: 'InProcess', evaluationDate: '', notes: '', rowVersion: null, saving: false, error: null },

        openCreateProgressModal(row) {
            this.progressModal = { open: true, mode: 'create', studentId: row.studentId, studentName: row.fullName, entryId: null, juzNumber: '', hizbNumber: '', surahNumber: '', status: 'InProcess', evaluationDate: '', notes: '', rowVersion: null, saving: false, error: null };
        },

        openEditProgressModal(row, entry) {
            this.progressModal = { open: true, mode: 'edit', studentId: row.studentId, studentName: row.fullName, entryId: entry.id, juzNumber: entry.juzNumber, hizbNumber: entry.hizbNumber, surahNumber: entry.surahNumber, status: entry.status, evaluationDate: entry.evaluationDate || '', notes: entry.notes || '', rowVersion: entry.rowVersion, saving: false, error: null };
        },

        closeProgressModal() {
            this.progressModal.open = false;
        },

        /** Bornes reflétées du serveur (CreateQuranProgressCommandValidator) — évite un aller-retour
         *  pour une erreur triviale ; le serveur reste seul autorité (422 sinon). Seule la CRÉATION
         *  porte Juz/Hizb/Sourate : ils sont immuables et non affichés en édition. */
        progressValidationError() {
            const m = this.progressModal;
            if (m.mode === 'create') {
                const juz = Number(m.juzNumber), hizb = Number(m.hizbNumber), surah = Number(m.surahNumber);
                if (!Number.isInteger(juz) || juz < 1 || juz > 30) return 'Le Juz doit être compris entre 1 et 30.';
                if (!Number.isInteger(hizb) || hizb < 1 || hizb > 60) return 'Le Hizb doit être compris entre 1 et 60.';
                if (!Number.isInteger(surah) || surah < 1 || surah > 114) return 'La Sourate doit être comprise entre 1 et 114.';
            }
            return null;
        },

        async saveProgress() {
            const m = this.progressModal;
            const validationError = this.progressValidationError();
            if (validationError) { m.error = validationError; return; }

            m.saving = true;
            m.error = null;
            try {
                if (m.mode === 'create') {
                    await window.api.post('/quran/progress', {
                        studentId: m.studentId,
                        juzNumber: Number(m.juzNumber),
                        hizbNumber: Number(m.hizbNumber),
                        surahNumber: Number(m.surahNumber),
                        status: m.status,
                        evaluationDate: m.evaluationDate || null,
                        notes: m.notes || null
                    });
                } else {
                    await window.api.put(`/quran/progress/${m.entryId}`, {
                        status: m.status,
                        evaluationDate: m.evaluationDate || null,
                        notes: m.notes || null,
                        rowVersion: m.rowVersion
                    });
                }
                this.closeProgressModal();
                await this.loadProgress();
            } catch (err) {
                m.error = err && err.status === 409
                    ? 'Cette observation a été modifiée par un autre utilisateur.'
                    : window.api.toMessage(err, 'Une erreur est survenue.');
            } finally {
                m.saving = false;
            }
        },
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: PASS (24/24)

- [ ] **Step 5: Ajouter le balisage — bouton « + Ajouter », bouton « Corriger », modale**

Dans `src/SamaEcole.Web/Views/Coran/Index.cshtml`, dans la table du Suivi de mémorisation, remplacer la cellule du bouton « Historique » (dernière `<td class="py-2 px-4 text-right">...</td>` de la ligne principale) par :

```html
                                        <td class="py-2 px-4 text-right">
                                            <div class="flex items-center justify-end gap-3">
                                                <button type="button" x-show="row.entries.length > 0" x-cloak
                                                        class="text-xs font-medium text-primary-600 hover:text-primary-700"
                                                        x-on:click="toggleExpand(row)">
                                                    <span x-text="row.expanded ? 'Masquer' : 'Historique (' + row.entries.length + ')'"></span>
                                                </button>
                                                <button type="button" x-show="canWrite" x-cloak
                                                        class="btn-modal-secondary text-xs py-1 px-2"
                                                        x-on:click="openCreateProgressModal(row)">
                                                    <icon name="plus" class="w-3.5 h-3.5" />
                                                    Ajouter
                                                </button>
                                            </div>
                                        </td>
```

Et, dans le tableau d'historique déplié, remplacer la cellule Notes (dernière `<td class="py-1 pr-2" x-text="entry.notes || ''"></td>`) par :

```html
                                                        <td class="py-1 pr-2" x-text="entry.notes || ''"></td>
                                                        <td class="py-1 pr-2 text-right">
                                                            <button type="button" x-show="canWrite" x-cloak
                                                                    class="text-primary-600 hover:text-primary-700"
                                                                    title="Corriger"
                                                                    x-on:click="openEditProgressModal(row, entry)">
                                                                <icon name="pencil" class="w-3.5 h-3.5" label="Corriger" />
                                                            </button>
                                                        </td>
```

(Ajouter une colonne `<th class="text-left py-1 pr-2"></th>` vide dans l'en-tête `<thead>` de ce même tableau interne, et augmenter le `colspan` de la ligne d'historique de `4` à... — ce dernier reste `4` : le `colspan` porte sur les colonnes du tableau EXTÉRIEUR, pas du tableau interne, il n'est pas concerné.)

Enfin, ajouter la modale juste avant la fermeture du `<div x-data="coranPage()">` :

```html
    <modal-shell open="progressModal.open" on-close="closeProgressModal()" size="md"
                  title="Suivi de mémorisation">
        <div class="space-y-4">
            <p class="text-sm text-slate-500">Élève : <strong x-text="progressModal.studentName"></strong></p>

            <div class="grid grid-cols-3 gap-3">
                <div>
                    <label class="form-label">Juz</label>
                    <input type="number" min="1" max="30" x-model="progressModal.juzNumber"
                           x-show="progressModal.mode === 'create'" x-cloak class="input-field mt-1" />
                    <p x-show="progressModal.mode === 'edit'" x-cloak class="input-field mt-1 bg-slate-50 text-slate-500" x-text="progressModal.juzNumber"></p>
                </div>
                <div>
                    <label class="form-label">Hizb</label>
                    <input type="number" min="1" max="60" x-model="progressModal.hizbNumber"
                           x-show="progressModal.mode === 'create'" x-cloak class="input-field mt-1" />
                    <p x-show="progressModal.mode === 'edit'" x-cloak class="input-field mt-1 bg-slate-50 text-slate-500" x-text="progressModal.hizbNumber"></p>
                </div>
                <div>
                    <label class="form-label">Sourate</label>
                    <input type="number" min="1" max="114" x-model="progressModal.surahNumber"
                           x-show="progressModal.mode === 'create'" x-cloak class="input-field mt-1" />
                    <p x-show="progressModal.mode === 'edit'" x-cloak class="input-field mt-1 bg-slate-50 text-slate-500" x-text="progressModal.surahNumber"></p>
                </div>
            </div>

            <div>
                <label class="form-label">Statut</label>
                <select-field model="progressModal.status"
                              options-expr="[{value:'InProcess', label:'En cours'},{value:'Memorized', label:'Mémorisé'},{value:'Revised', label:'Révisé'}]" />
            </div>

            <div>
                <label class="form-label">Date (facultative)</label>
                <input type="date" x-model="progressModal.evaluationDate" class="input-field mt-1" />
            </div>

            <div>
                <label class="form-label">Notes (facultatives)</label>
                <textarea x-model="progressModal.notes" rows="3" maxlength="2000" class="input-field mt-1"></textarea>
            </div>

            <p x-show="progressModal.error" x-cloak class="field-error" x-text="progressModal.error"></p>

            <div class="flex justify-end gap-3 pt-2">
                <button type="button" class="btn-modal-secondary group" x-on:click="closeProgressModal()">Annuler</button>
                <button type="button" class="btn-modal-primary group" :disabled="progressModal.saving"
                        x-on:click="saveProgress()">
                    <span x-text="progressModal.saving ? 'Enregistrement…' : 'Enregistrer'"></span>
                </button>
            </div>
        </div>
    </modal-shell>
```

- [ ] **Step 6: Vérifier la compilation .NET**

Run: `dotnet build sama_ecole/SamaEcole.sln`
Expected: Build réussi.

- [ ] **Step 7: Vérifier au navigateur**

Skill `run` : en Directeur ou Enseignant, ouvrir `/coran`, onglet Suivi de mémorisation, ajouter une observation à un élève, vérifier son apparition dans le résumé et l'historique, la corriger, vérifier le rejet d'un Juz hors bornes (31) côté client ET d'une valeur limite côté serveur si contournée.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/coran.js src/SamaEcole.Web/Views/Coran/Index.cshtml src/SamaEcole.Web/tests/js/coran.test.mjs
git commit -m "feat(coran): modale d'ajout et de correction du suivi de memorisation"
```

---

### Task 5: Modale Évaluation orale (ajout + correction)

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/coran.js`
- Modify: `src/SamaEcole.Web/Views/Coran/Index.cshtml`
- Test: `src/SamaEcole.Web/tests/js/coran.test.mjs` (section 5, ajoutée à la suite)

**Interfaces:**
- Consumes: `POST /quran/evaluations` (`{studentId, evaluationDate, memoryMistakes, tajwidMistakes, hesitations, finalScore}` → `QuranEvaluationDto`), `PUT /quran/evaluations/{id}` (`{evaluationDate, memoryMistakes, tajwidMistakes, hesitations, finalScore, rowVersion}` → `QuranEvaluationDto`).
- Produces: `evaluationModal` (state), `todayIso()`, `openCreateEvaluationModal(row)`, `openEditEvaluationModal(row, entry)`, `closeEvaluationModal()`, `evaluationValidationError()`, `saveEvaluation()` — consommés uniquement par le balisage de ce Task.

- [ ] **Step 1: Écrire les tests (échouent — la modale n'existe pas)**

Ajouter à la suite de `src/SamaEcole.Web/tests/js/coran.test.mjs` :

```javascript
// ---------------------------------------------------------------- 5. Modale Évaluation orale

test('openCreateEvaluationModal pose un état neuf, date du jour, compteurs à 0', async () => {
    const { view } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');

    view.openCreateEvaluationModal(view.evaluationRows[0]);

    assert.equal(view.evaluationModal.open, true);
    assert.equal(view.evaluationModal.mode, 'create');
    assert.equal(view.evaluationModal.studentId, 's-1');
    assert.equal(view.evaluationModal.evaluationDate, view.todayIso());
    assert.equal(view.evaluationModal.memoryMistakes, '0');
    assert.equal(view.evaluationModal.finalScore, '');
});

test('openEditEvaluationModal pré-remplit depuis l\'entrée choisie', async () => {
    const { view } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.evaluationRows[0].entries[1]; // e-2

    view.openEditEvaluationModal(view.evaluationRows[0], entry);

    assert.equal(view.evaluationModal.mode, 'edit');
    assert.equal(view.evaluationModal.entryId, 'e-2');
    assert.equal(view.evaluationModal.evaluationDate, '2026-09-18');
    assert.equal(view.evaluationModal.hesitations, '1');
    assert.equal(view.evaluationModal.finalScore, '18');
    assert.equal(view.evaluationModal.rowVersion, 1);
});

test('date future refusée AVANT tout appel réseau', async () => {
    const { view, calls } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateEvaluationModal(view.evaluationRows[0]);
    view.evaluationModal.evaluationDate = '2099-01-01';
    view.evaluationModal.finalScore = '15';

    await view.saveEvaluation();

    assert.match(view.evaluationModal.error, /future/i);
    assert.equal(calls.filter(c => c.method === 'POST').length, 0);
});

test('compteur négatif refusé AVANT tout appel réseau', async () => {
    const { view, calls } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateEvaluationModal(view.evaluationRows[0]);
    view.evaluationModal.memoryMistakes = '-1';
    view.evaluationModal.finalScore = '15';

    await view.saveEvaluation();

    assert.match(view.evaluationModal.error, /mémoire/i);
    assert.equal(calls.filter(c => c.method === 'POST').length, 0);
});

test('création : envoie POST /quran/evaluations avec les champs saisis', async () => {
    const { view, calls } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    view.openCreateEvaluationModal(view.evaluationRows[0]);
    view.evaluationModal.evaluationDate = '2026-09-25';
    view.evaluationModal.memoryMistakes = '1';
    view.evaluationModal.tajwidMistakes = '0';
    view.evaluationModal.hesitations = '2';
    view.evaluationModal.finalScore = '16.5';

    await view.saveEvaluation();

    const post = calls.find(c => c.method === 'POST' && c.endpoint === '/quran/evaluations');
    assert.deepEqual(post.body, {
        studentId: 's-1', evaluationDate: '2026-09-25',
        memoryMistakes: 1, tajwidMistakes: 0, hesitations: 2, finalScore: 16.5
    });
    assert.equal(view.evaluationModal.open, false);
});

test('correction : envoie PUT avec le rowVersion, SANS studentId', async () => {
    const { view, calls } = boot({ evaluations: () => [evaluationRow()] });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.evaluationRows[0].entries[0]; // e-1, rowVersion 1
    view.openEditEvaluationModal(view.evaluationRows[0], entry);
    view.evaluationModal.finalScore = '17';

    await view.saveEvaluation();

    const put = calls.find(c => c.method === 'PUT' && c.endpoint === '/quran/evaluations/e-1');
    assert.deepEqual(put.body, {
        evaluationDate: '2026-09-05', memoryMistakes: 2, tajwidMistakes: 1, hesitations: 0,
        finalScore: 17, rowVersion: 1
    });
    assert.ok(!('studentId' in put.body), 'studentId ne doit jamais être envoyé en correction');
});

test('409 à la correction : message explicite, la modale reste ouverte', async () => {
    const conflict = Object.assign(new Error('conflit'), { status: 409 });
    const { view } = boot({ evaluations: () => [evaluationRow()], failWith: { PUT: conflict } });
    await view.init();
    await view.selectClassroom('c-cm2');
    const entry = view.evaluationRows[0].entries[0];
    view.openEditEvaluationModal(view.evaluationRows[0], entry);

    await view.saveEvaluation();

    assert.match(view.evaluationModal.error, /modifiée par un autre utilisateur/);
    assert.equal(view.evaluationModal.open, true);
});
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: FAIL — `view.evaluationModal`/`openCreateEvaluationModal`/etc. sont `undefined`.

- [ ] **Step 3: Ajouter la modale à `coran.js`**

Dans `src/SamaEcole.Web/wwwroot/js/coran.js`, ajouter juste avant le `}));` final (après le bloc `saveProgress` du Task 4) :

```javascript
        // --- Modale Évaluation orale -----------------------------------------------------------

        evaluationModal: { open: false, mode: 'create', studentId: null, studentName: '', entryId: null, evaluationDate: '', memoryMistakes: '0', tajwidMistakes: '0', hesitations: '0', finalScore: '', rowVersion: null, saving: false, error: null },

        todayIso() {
            return new Date().toISOString().slice(0, 10);
        },

        openCreateEvaluationModal(row) {
            this.evaluationModal = { open: true, mode: 'create', studentId: row.studentId, studentName: row.fullName, entryId: null, evaluationDate: this.todayIso(), memoryMistakes: '0', tajwidMistakes: '0', hesitations: '0', finalScore: '', rowVersion: null, saving: false, error: null };
        },

        openEditEvaluationModal(row, entry) {
            this.evaluationModal = { open: true, mode: 'edit', studentId: row.studentId, studentName: row.fullName, entryId: entry.id, evaluationDate: entry.evaluationDate, memoryMistakes: String(entry.memoryMistakes), tajwidMistakes: String(entry.tajwidMistakes), hesitations: String(entry.hesitations), finalScore: String(entry.finalScore), rowVersion: entry.rowVersion, saving: false, error: null };
        },

        closeEvaluationModal() {
            this.evaluationModal.open = false;
        },

        /** Bornes reflétées du serveur (CreateQuranEvaluationCommandValidator) — voir progressValidationError. */
        evaluationValidationError() {
            const m = this.evaluationModal;
            if (!m.evaluationDate) return 'La date est obligatoire.';
            if (m.evaluationDate > this.todayIso()) return 'La date ne peut pas être future.';
            const memory = Number(m.memoryMistakes), tajwid = Number(m.tajwidMistakes), hesitations = Number(m.hesitations), score = Number(m.finalScore);
            if (!Number.isInteger(memory) || memory < 0) return 'Le nombre d\'erreurs de mémoire doit être positif ou nul.';
            if (!Number.isInteger(tajwid) || tajwid < 0) return 'Le nombre d\'erreurs de tajwid doit être positif ou nul.';
            if (!Number.isInteger(hesitations) || hesitations < 0) return 'Le nombre d\'hésitations doit être positif ou nul.';
            if (Number.isNaN(score) || score < 0) return 'La note finale doit être positive ou nulle.';
            return null;
        },

        async saveEvaluation() {
            const m = this.evaluationModal;
            const validationError = this.evaluationValidationError();
            if (validationError) { m.error = validationError; return; }

            m.saving = true;
            m.error = null;
            try {
                const payload = {
                    evaluationDate: m.evaluationDate,
                    memoryMistakes: Number(m.memoryMistakes),
                    tajwidMistakes: Number(m.tajwidMistakes),
                    hesitations: Number(m.hesitations),
                    finalScore: Number(m.finalScore)
                };
                if (m.mode === 'create') {
                    await window.api.post('/quran/evaluations', { studentId: m.studentId, ...payload });
                } else {
                    await window.api.put(`/quran/evaluations/${m.entryId}`, { ...payload, rowVersion: m.rowVersion });
                }
                this.closeEvaluationModal();
                await this.loadEvaluations();
            } catch (err) {
                m.error = err && err.status === 409
                    ? 'Cette observation a été modifiée par un autre utilisateur.'
                    : window.api.toMessage(err, 'Une erreur est survenue.');
            } finally {
                m.saving = false;
            }
        }
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `node --test src/SamaEcole.Web/tests/js/coran.test.mjs`
Expected: PASS (31/31)

- [ ] **Step 5: Ajouter le balisage — bouton « + Ajouter », bouton « Corriger », modale**

Dans `src/SamaEcole.Web/Views/Coran/Index.cshtml`, dans la table des Évaluations orales, remplacer la cellule du bouton « Historique » de la ligne principale par :

```html
                                    <td class="py-2 px-4 text-right">
                                        <div class="flex items-center justify-end gap-3">
                                            <button type="button" x-show="row.entries.length > 0" x-cloak
                                                    class="text-xs font-medium text-primary-600 hover:text-primary-700"
                                                    x-on:click="toggleExpand(row)">
                                                <span x-text="row.expanded ? 'Masquer' : 'Historique (' + row.entries.length + ')'"></span>
                                            </button>
                                            <button type="button" x-show="canWrite" x-cloak
                                                    class="btn-modal-secondary text-xs py-1 px-2"
                                                    x-on:click="openCreateEvaluationModal(row)">
                                                <icon name="plus" class="w-3.5 h-3.5" />
                                                Ajouter
                                            </button>
                                        </div>
                                    </td>
```

Et, dans le tableau d'historique déplié des évaluations, remplacer la cellule Note (dernière `<td class="py-1 pr-2" x-text="entry.finalScore"></td>`) par :

```html
                                                        <td class="py-1 pr-2" x-text="entry.finalScore"></td>
                                                        <td class="py-1 pr-2 text-right">
                                                            <button type="button" x-show="canWrite" x-cloak
                                                                    class="text-primary-600 hover:text-primary-700"
                                                                    title="Corriger"
                                                                    x-on:click="openEditEvaluationModal(row, entry)">
                                                                <icon name="pencil" class="w-3.5 h-3.5" label="Corriger" />
                                                            </button>
                                                        </td>
```

Enfin, ajouter la modale juste après la fermeture de `</modal-shell>` du Task 4 (Suivi de mémorisation), avant la fermeture du `<div x-data="coranPage()">` :

```html
    <modal-shell open="evaluationModal.open" on-close="closeEvaluationModal()" size="md"
                  title="Évaluation orale">
        <div class="space-y-4">
            <p class="text-sm text-slate-500">Élève : <strong x-text="evaluationModal.studentName"></strong></p>

            <div>
                <label class="form-label">Date</label>
                <input type="date" x-model="evaluationModal.evaluationDate" class="input-field mt-1" />
            </div>

            <div class="grid grid-cols-3 gap-3">
                <div>
                    <label class="form-label">Erreurs mémoire</label>
                    <input type="number" min="0" x-model="evaluationModal.memoryMistakes" class="input-field mt-1" />
                </div>
                <div>
                    <label class="form-label">Erreurs tajwid</label>
                    <input type="number" min="0" x-model="evaluationModal.tajwidMistakes" class="input-field mt-1" />
                </div>
                <div>
                    <label class="form-label">Hésitations</label>
                    <input type="number" min="0" x-model="evaluationModal.hesitations" class="input-field mt-1" />
                </div>
            </div>

            <div>
                <label class="form-label">Note finale</label>
                <input type="number" min="0" step="0.5" x-model="evaluationModal.finalScore" class="input-field mt-1" />
            </div>

            <p x-show="evaluationModal.error" x-cloak class="field-error" x-text="evaluationModal.error"></p>

            <div class="flex justify-end gap-3 pt-2">
                <button type="button" class="btn-modal-secondary group" x-on:click="closeEvaluationModal()">Annuler</button>
                <button type="button" class="btn-modal-primary group" :disabled="evaluationModal.saving"
                        x-on:click="saveEvaluation()">
                    <span x-text="evaluationModal.saving ? 'Enregistrement…' : 'Enregistrer'"></span>
                </button>
            </div>
        </div>
    </modal-shell>
```

- [ ] **Step 6: Vérifier la compilation .NET**

Run: `dotnet build sama_ecole/SamaEcole.sln`
Expected: Build réussi.

- [ ] **Step 7: Vérifier au navigateur**

Skill `run` : onglet Évaluations orales, ajouter une évaluation, vérifier son apparition dans le résumé et l'historique, la corriger, vérifier le rejet d'une date future et d'un compteur négatif côté client.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/coran.js src/SamaEcole.Web/Views/Coran/Index.cshtml src/SamaEcole.Web/tests/js/coran.test.mjs
git commit -m "feat(coran): modale d'ajout et de correction des evaluations orales"
```

---

### Task 6: Entrée du Centre d'aide

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/help.js`

**Interfaces:**
- Produces: une fiche d'aide `id: 'ecran-coran'` — aucune interface consommée ailleurs (fiche terminale).

- [ ] **Step 1: Lancer `help.test.mjs` pour confirmer l'état actuel (passe déjà — sert de filet)**

Run: `node --test src/SamaEcole.Web/tests/js/help.test.mjs`
Expected: PASS (état avant ajout — sert de référence pour le Step 3)

- [ ] **Step 2: Ajouter la fiche d'aide**

Dans `src/SamaEcole.Web/wwwroot/js/help.js`, dans le module Pédagogie (celui qui porte déjà les fiches Notes/Internat), ajouter un nouvel objet à la suite des fiches existantes, avant la fermeture du tableau `articles` :

```javascript
                {
                    id: 'ecran-coran',
                    title: 'Suivi de mémorisation et évaluations orales (module Coran/Franco-Arabe)',
                    location: 'Coran',
                    href: '/coran',
                    roles: ['Directeur', 'Enseignant', 'Secrétariat'],
                    definition:
                        "Écran Coran : un sélecteur de classe et deux onglets — Suivi de mémorisation (Juz, Hizb, " +
                        "Sourate, statut En cours/Mémorisé/Révisé) et Évaluations orales (erreurs de mémoire et de " +
                        "tajwid, hésitations, note finale). Chaque élève porte une liste d'observations, illimitée " +
                        "dans le temps, consultable par un historique dépliable.",
                    objectif:
                        "Donner à l'école un suivi structuré de la mémorisation coranique et des évaluations orales, " +
                        "au même titre que les notes académiques, sans mélanger les deux systèmes de notation.",
                    probleme:
                        "Sans écran dédié, ce suivi ne vit que sur des registres papier ou des carnets personnels de " +
                        "l'enseignant : rien n'est consultable par le Directeur ou le Secrétariat, rien ne survit à un " +
                        "changement d'enseignant, et aucun historique ne permet de mesurer la progression d'un élève " +
                        "d'un mois sur l'autre.",
                    procedure: [
                        "Ouvrez Coran et choisissez une classe.",
                        "Onglet « Suivi de mémorisation » : cliquez « Ajouter » sur la ligne d'un élève, renseignez Juz, Hizb, Sourate et le statut, puis enregistrez.",
                        "Onglet « Évaluations orales » : cliquez « Ajouter », renseignez la date, les erreurs de mémoire et de tajwid, les hésitations et la note finale.",
                        "Cliquez « Historique » sur la ligne d'un élève pour voir toutes ses observations passées, les plus récentes en premier.",
                        "Cliquez l'icône crayon d'une observation pour la corriger — l'identité de la ligne (Juz/Hizb/Sourate ou l'élève) ne peut jamais être changée après coup, seuls le statut, la date et les notes ou chiffres le peuvent."
                    ],
                    impacts: [
                        "Bulletin : ce suivi n'apparaît PAS sur le bulletin de notes — c'est un système de suivi séparé, indépendant du calcul des moyennes et des coefficients.",
                        "Module désactivé : si le Directeur désactive le module Coran depuis Paramètres, l'écran affiche une erreur d'accès et aucune saisie n'est possible, quel que soit le rôle.",
                        "Rôles : Directeur et Enseignant peuvent ajouter et corriger ; le Secrétariat consulte uniquement, sans pouvoir écrire."
                    ],
                    recommandations: [
                        "Saisissez une évaluation orale le jour même de la séance : une date antérieure reste possible, mais une date future est refusée par l'écran.",
                        "En cas de refus lors d'une correction concurrente (« modifiée par un autre utilisateur »), rouvrez l'historique pour repartir de l'observation à jour avant de corriger à nouveau."
                    ]
                }
```

- [ ] **Step 3: Lancer `help.test.mjs` pour vérifier que la nouvelle fiche respecte le squelette**

Run: `node --test src/SamaEcole.Web/tests/js/help.test.mjs`
Expected: PASS — la fiche `ecran-coran` porte les six rubriques (`definition`, `objectif`, `probleme`, `procedure`, `impacts`, `recommandations`), toutes non vides, `href` commence par `/`, `roles` non vide.

- [ ] **Step 4: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/help.js
git commit -m "docs(coran): ajoute la fiche d'aide de l'ecran Coran"
```
