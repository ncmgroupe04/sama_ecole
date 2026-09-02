/**
 * GARDE-FOUS DE NON-RÉGRESSION — les bugs qui sont déjà revenus plusieurs fois.
 *
 * Raison d'être : chacun des défauts ci-dessous a DÉJÀ été trouvé, corrigé, et documenté par un
 * commentaire… puis est réapparu ailleurs, parce qu'un commentaire n'empêche rien. Le cas d'école est
 * `pageSize` : corrigé sur /teachers le 27/08/2026 (payroll.js) avec un commentaire de dix lignes
 * expliquant le piège — et les trois écrans du module Surveillant sont restés cassés des semaines,
 * jusqu'à ce qu'un utilisateur signale que « la liste des élèves ne charge pas ».
 *
 * Ces tests ne vérifient pas un comportement : ils balaient le DÉPÔT et refusent la réapparition du
 * motif, où qu'il soit écrit. C'est la seule forme de correction qui tienne dans le temps.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const WEB_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const JS_DIR = path.join(WEB_ROOT, 'wwwroot', 'js');

/** Retire commentaires de ligne et de bloc : un motif cité dans une explication n'est pas un appel. */
function stripComments(source) {
    return source
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/(^|[^:])\/\/.*$/gm, '$1');
}

function jsFiles() {
    return readdirSync(JS_DIR)
        .filter((f) => f.endsWith('.js'))
        .map((f) => ({ name: f, source: readFileSync(path.join(JS_DIR, f), 'utf8') }));
}

/* ------------------------------------------------------------------------------------------------
 * GARDE-FOU 1 — aucune requête ne peut demander plus de lignes que le serveur n'en accorde.
 * ---------------------------------------------------------------------------------------------- */

test('garde-fou : aucun appel ne demande un pageSize supérieur au plafond serveur (100)', () => {
    // Les validateurs FluentValidation plafonnent PageSize à 100 (GetStudentsQueryValidator,
    // GetTeachersQueryValidator, GetPaymentsQueryValidator, GetInventoryItemsQueryValidator…).
    // Au-delà, la réponse est un 422 VALIDATION_ERROR — et comme l'appelant se contente en général
    // d'un console.error, la liste reste vide SANS message. Pour charger une collection entière,
    // utiliser `api.getAllPages(endpoint)`, qui enchaîne les pages sous le plafond.
    const SERVER_MAX_PAGE_SIZE = 100;
    const offenders = [];

    for (const { name, source } of jsFiles()) {
        const code = stripComments(source);
        for (const match of code.matchAll(/pageSize=(\d+)/g)) {
            const requested = Number(match[1]);
            if (requested > SERVER_MAX_PAGE_SIZE) {
                offenders.push(`${name} : pageSize=${requested}`);
            }
        }
    }

    assert.deepEqual(
        offenders,
        [],
        `pageSize au-dessus du plafond serveur (${SERVER_MAX_PAGE_SIZE}) — ces appels partiront en 422 ` +
        `et laisseront la liste vide sans message. Utiliser api.getAllPages().\n  ` +
        offenders.join('\n  ')
    );
});

/* ------------------------------------------------------------------------------------------------
 * GARDE-FOU 2 — la plage d'années du calendrier reste identique des deux côtés.
 * ---------------------------------------------------------------------------------------------- */

test('garde-fou : la plage d\'années de dateField() colle à celle du DateFieldTagHelper', () => {
    // Le <select> Année est rendu côté serveur ; la navigation par chevrons est bornée côté client.
    // Si les deux divergent, viewYear peut désigner une année SANS <option> : le navigateur retombe
    // sur la première de la liste et x-model la réécrit — le calendrier saute d'un siècle sous les
    // doigts de l'utilisateur, sans la moindre erreur en console.
    const tagHelper = readFileSync(
        path.join(WEB_ROOT, 'TagHelpers', 'DateFieldTagHelper.cs'), 'utf8');
    const ui = readFileSync(path.join(JS_DIR, 'ui-components.js'), 'utf8');

    const csharp = /Enumerable\.Range\(currentYear\s*-\s*(\d+)\s*,\s*(\d+)\)/.exec(tagHelper);
    assert.ok(csharp, 'plage d\'années introuvable dans DateFieldTagHelper.cs — garde-fou à réaligner');

    const back = Number(csharp[1]);            // années en arrière
    const forward = Number(csharp[2]) - back - 1; // Range(start, count) → dernière = start + count - 1

    const jsBack = Number(/yearsBack:\s*(\d+)/.exec(ui)?.[1]);
    const jsForward = Number(/yearsForward:\s*(\d+)/.exec(ui)?.[1]);

    assert.equal(jsBack, back,
        `dateField().yearsBack (${jsBack}) ≠ DateFieldTagHelper (${back} ans en arrière)`);
    assert.equal(jsForward, forward,
        `dateField().yearsForward (${jsForward}) ≠ DateFieldTagHelper (${forward} ans en avant)`);
});

/* ------------------------------------------------------------------------------------------------
 * GARDE-FOU 3 — cliquet sur les chargements qui échouent en silence.
 * ---------------------------------------------------------------------------------------------- */

/**
 * Chargements dont le `catch` n'affiche rien à l'utilisateur, CONNUS et tolérés à ce jour.
 *
 * Cette liste ne peut que RÉTRÉCIR. Elle n'est pas une autorisation : c'est une dette recensée. Un
 * chargement absent de cette liste et qui avale son erreur fait échouer ce test — c'est exactement
 * ce qui aurait fait remonter le bug du module Surveillant le jour où il a été écrit, au lieu
 * d'attendre qu'un utilisateur s'en plaigne.
 *
 * Pour en retirer une entrée : ajouter un `toast.error(window.api.toMessage(err, '…'))` (ou poser
 * `this.error`) dans le catch, puis supprimer la ligne ici.
 */
const SILENT_LOADERS_DEBT = [
    // VIDE — et c'est le but. Tout chargement du produit remonte désormais son échec (toast ou état
    // d'erreur rendu par le gabarit), ou déclare son silence par un `silence-volontaire:` justifié.
    // Le cliquet est donc entièrement fermé : le moindre nouveau `catch` muet fait échouer la suite.
];

/**
 * Extrait le bloc `{ … }` équilibré qui commence à `openIndex`.
 *
 * La première version de ce détecteur découpait des fenêtres de N caractères après le mot-clé. Elle
 * s'est révélée fausse DANS LES DEUX SENS : elle accusait `inventory.js :: loadBeneficiaries` (dont
 * le toast tombait 200 caractères trop loin) et laissait passer quatre vrais coupables
 * (`settings.js :: load`, `students.js :: loadClassrooms`…) dont le corps dépassait la fenêtre. Un
 * garde-fou qui rassure à tort est pire que pas de garde-fou du tout — d'où ce comptage d'accolades.
 */
function balancedBlock(source, openIndex) {
    let depth = 0;
    for (let i = openIndex; i < source.length; i++) {
        if (source[i] === '{') depth++;
        else if (source[i] === '}' && --depth === 0) return source.slice(openIndex, i + 1);
    }
    return source.slice(openIndex);
}

/**
 * Notification visible. Cherchée dans TOUT le corps : un `toast.error(...)` n'est jamais une
 * réinitialisation, où qu'il se trouve. Nécessaire pour inventory.js :: loadBeneficiaries, qui
 * mémorise l'échec dans une variable locale et ne le signale qu'après un Promise.all.
 */
const NOTIFIES = /toast\.|showError|notify\(|alert\(/;

/**
 * Affectation d'un état d'erreur rendu par le gabarit (`analyticsError`, `financeError`,
 * `submitError`…). Cherchée UNIQUEMENT dans les gestionnaires : presque tous les chargements
 * remettent leur état à `null` AVANT le try, et compter cette remise à zéro rendrait le garde-fou
 * aveugle. C'est ce détail qui faisait passer les trois tuiles du tableau de bord pour muettes alors
 * qu'elles affichent chacune son erreur depuis toujours.
 */
const SETS_ERROR_STATE = /this\.[A-Za-z0-9_]*[Ee]rror\s*=/;

/**
 * Silence assumé : un `catch` portant `silence-volontaire:` suivi de sa justification. Réservé aux
 * refus ATTENDUS — typiquement un 403 sur une route réservée à un autre rôle, où un message
 * transformerait le fonctionnement normal en incident (voir payroll.js :: loadUsers).
 */
const DELIBERATE_SILENCE = /silence-volontaire\s*:/;

function findSilentLoaders() {
    const found = [];

    for (const { name, source } of jsFiles()) {
        for (const match of source.matchAll(/async\s+(load[A-Za-z0-9_]*)\s*\([^)]*\)\s*\{/g)) {
            const bodyStart = source.indexOf('{', match.index + match[0].length - 1);
            const body = balancedBlock(source, bodyStart);

            const handlers = [];
            for (const c of body.matchAll(/catch\s*(\([^)]*\))?\s*\{/g)) {
                handlers.push(balancedBlock(body, body.indexOf('{', c.index + c[0].length - 1)));
            }
            for (const a of body.matchAll(/\.catch\s*\(/g)) {
                handlers.push(body.slice(a.index, a.index + 200));
            }
            if (handlers.length === 0) continue;

            if (handlers.some((h) => DELIBERATE_SILENCE.test(h))) continue;
            if (NOTIFIES.test(body)) continue;
            if (handlers.some((h) => SETS_ERROR_STATE.test(h))) continue;

            found.push(`${name} :: ${match[1]}`);
        }
    }

    return found;
}

test('garde-fou : aucun NOUVEAU chargement n\'échoue en silence', () => {
    const current = findSilentLoaders();
    const introduced = current.filter((entry) => !SILENT_LOADERS_DEBT.includes(entry));

    assert.deepEqual(
        introduced,
        [],
        'Nouveau chargement dont le catch n\'affiche RIEN à l\'utilisateur : une liste vide y devient ' +
        'indiscernable de « aucune donnée ». Ajouter un toast.error(window.api.toMessage(...)) dans le ' +
        'catch.\n  ' + introduced.join('\n  ')
    );
});

test('garde-fou : la dette des chargements muets ne fait que diminuer', () => {
    const current = findSilentLoaders();
    const fixed = SILENT_LOADERS_DEBT.filter((entry) => !current.includes(entry));

    assert.deepEqual(
        fixed,
        [],
        'Ces chargements affichent désormais leurs erreurs : retirer ces lignes de SILENT_LOADERS_DEBT ' +
        'pour que le cliquet se resserre.\n  ' + fixed.join('\n  ')
    );
});

/* ------------------------------------------------------------------------------------------------
 * GARDE-FOU 4 — `x-show` ne protège RIEN : une liaison Alpine ne doit jamais déréférencer un état
 * encore null au montage de la vue.
 * ---------------------------------------------------------------------------------------------- */

/**
 * Le piège, en une phrase : `x-show` ne fait que poser `display:none`. Alpine construit et ÉVALUE
 * quand même toutes les liaisons du sous-arbre dès le montage — y compris `x-text`, `x-model`,
 * `:class` et le `x-for` d'un `<template>`. Un bloc masqué par `x-show="receipt"` qui contient
 * `x-text="receipt.receiptNumber"` lève donc, au chargement à froid de la page, un
 * « TypeError: Cannot read properties of null ».
 *
 * Ce n'est pas cosmétique : Alpine ARRÊTE l'évaluation de l'expression fautive. Le rendu du reste de
 * l'élément est abandonné, et la console de l'utilisateur se remplit d'erreurs qui masquent les
 * vraies. Le cas fondateur est /caisse (02/09/2026) : trois familles d'erreurs au simple chargement
 * de l'écran — `installments`, `receiptNumber`, `amount` — alors que rien n'était encore sélectionné.
 *
 * Les deux seules protections reconnues ici :
 *   1. `<template x-if="…">` — Alpine ne rend PAS le contenu tant que la condition est fausse ;
 *   2. un garde dans l'expression elle-même : `receipt?.x`, `receipt && receipt.x`,
 *      `receipt ? receipt.x : ''`, `(balance?.installments ?? [])`.
 *
 * Le corps d'un `<template x-for>` est également exempté : il n'est instancié que par élément de la
 * collection, donc jamais quand celle-ci est vide ou pas encore chargée.
 */

/** Tous les gabarits Razor de Views/, sous-dossiers compris. */
function cshtmlFiles(dir = path.join(WEB_ROOT, 'Views'), out = []) {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) cshtmlFiles(full, out);
        else if (entry.name.endsWith('.cshtml')) out.push(full);
    }
    return out;
}

/** Champs du composant initialisés à `null` — donc déréférençables à vide au montage. */
function nullableRoots(jsSources, component) {
    const at = jsSources.indexOf(`Alpine.data('${component}'`);
    if (at === -1) return null;
    const next = jsSources.indexOf("Alpine.data('", at + 20);
    const body = jsSources.slice(at, next === -1 ? jsSources.length : next);

    const roots = new Set();
    for (const m of body.matchAll(/^\s{4,}([A-Za-z_$][\w$]*)\s*:\s*null\s*,/gm)) roots.add(m[1]);
    return roots;
}

/** Portées `<template>` du gabarit, avec leur nature (`x-if` conditionnel / `x-for` répété). */
function templateScopes(html) {
    const scopes = [];
    const open = [];
    const tagRE = /<template\b([^>]*)>|<\/template>/g;
    let tag;

    while ((tag = tagRE.exec(html))) {
        if (tag[0].startsWith('</')) {
            const start = open.pop();
            if (start) scopes.push({ ...start, end: tag.index });
        } else {
            open.push({
                start: tagRE.lastIndex,
                conditional: /x-if="/.test(tag[1]),
                loop: /x-for="/.test(tag[1])
            });
        }
    }
    return scopes;
}

/** Attributs qu'Alpine évalue — `x-show` COMPRIS, et c'est tout l'intérêt du garde-fou. */
const ALPINE_ATTR =
    /(?:x-text|x-html|x-for|x-if|x-model|x-show|x-effect|:[\w:.-]+|@[\w.:-]+|x-on:[\w.:-]+)="([^"]*)"/g;

function findNullDerefs() {
    const jsSources = jsFiles().map(({ source }) => source).join('\n');
    const found = new Set();

    for (const file of cshtmlFiles()) {
        const html = readFileSync(file, 'utf8');

        const roots = new Set();
        for (const m of html.matchAll(/x-data="([A-Za-z_$][\w$]*)\s*[("]/g)) {
            const fields = nullableRoots(jsSources, m[1]);
            if (fields) for (const f of fields) roots.add(f);
        }
        if (roots.size === 0) continue;

        const scopes = templateScopes(html);
        const label = path.relative(WEB_ROOT, file).split(path.sep).join('/');

        for (const attr of html.matchAll(ALPINE_ATTR)) {
            const expr = attr[1];
            const at = attr.index;

            for (const root of roots) {
                if (!new RegExp(`(?<![\\w$.?])${root}\\s*\\.`).test(expr)) continue;

                // Garde dans l'expression : `root?.`, `root &&`, `root ?`, `root ||`, `!root`…
                const guarded = new RegExp(
                    `(?<![\\w$.])${root}\\s*(\\?\\.|\\?[^.]|&&|\\|\\||===?\\s*null|!==?\\s*null)`
                    + `|!\\s*${root}(?![\\w$])`);
                if (guarded.test(expr)) continue;

                // Protection structurelle : un `<template x-if>` ou le corps d'un `<template x-for>`.
                const sheltered = scopes.some((s) =>
                    at > s.start && at < s.end && (s.conditional || s.loop));
                if (sheltered) continue;

                found.add(`${label} :: ${root}`);
            }
        }
    }

    return [...found].sort();
}

/**
 * Écrans où le motif subsiste, RECENSÉS et non autorisés. Comme SILENT_LOADERS_DEBT, cette liste ne
 * peut que rétrécir. Chaque entrée = au moins une erreur en console au chargement à froid de l'écran.
 *
 * Pour en retirer une : envelopper le bloc dans un `<template x-if="…">`, ou garder chaque expression
 * (`root?.champ`, `root ? root.champ : ''`), puis supprimer la ligne ici.
 *
 * /caisse n'y figure pas — c'est le bug fondateur, corrigé le 02/09/2026. Sa réapparition, ici ou
 * ailleurs, fait échouer ce test.
 */
const NULL_DEREF_DEBT = [
    'Views/Buildings/Index.cshtml :: editingBuilding',
    'Views/Buildings/Index.cshtml :: editingRoom',
    'Views/Dashboard/Index.cshtml :: financeData',
    'Views/Enrollments/Index.cshtml :: receipt',
    'Views/Enrollments/Index.cshtml :: receiptFitMm',
    'Views/Exams/Index.cshtml :: assigningDossier',
    'Views/Exams/Index.cshtml :: editingDossier',
    'Views/Exams/Index.cshtml :: editingSession',
    'Views/Reports/Financial.cshtml :: data',
    'Views/Settings/Index.cshtml :: editingMention',
    'Views/Subjects/Index.cshtml :: editing',
];

test('garde-fou : aucune NOUVELLE liaison Alpine ne déréférence un état null au montage', () => {
    const current = findNullDerefs();
    const introduced = current.filter((entry) => !NULL_DEREF_DEBT.includes(entry));

    assert.deepEqual(
        introduced,
        [],
        'Liaison évaluée au montage sur un état encore null : « Cannot read properties of null » dès ' +
        'le chargement de l\'écran. `x-show` n\'est PAS une protection — Alpine évalue quand même. ' +
        'Envelopper dans un <template x-if="…"> ou garder l\'expression (root?.champ).\n  ' +
        introduced.join('\n  ')
    );
});

test('garde-fou : la dette des déréférencements null ne fait que diminuer', () => {
    const current = findNullDerefs();
    const fixed = NULL_DEREF_DEBT.filter((entry) => !current.includes(entry));

    assert.deepEqual(
        fixed,
        [],
        'Ces écrans sont désormais protégés : retirer ces lignes de NULL_DEREF_DEBT pour que le ' +
        'cliquet se resserre.\n  ' + fixed.join('\n  ')
    );
});
