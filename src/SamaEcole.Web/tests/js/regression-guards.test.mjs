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
    'attendance.js :: loadClassrooms',
    'attendance.js :: loadSubjects',
    'attendance.js :: loadRoster',
    'caisse.js :: loadCurrentSession',
    'caisse.js :: loadBalance',
    'dashboard.js :: loadAnalytics',
    'dashboard.js :: loadSurveillant',
    'dashboard.js :: loadFinance',
    'exams.js :: loadAudit',
    'exams.js :: loadStatistics',
    'features.js :: load',
    'inventory.js :: loadBeneficiaries',
    'payroll.js :: loadTeachers',
    'payroll.js :: loadUsers',
    'payroll.js :: loadSuggestedHours',
    'sms-settings.js :: load',
    'students.js :: loadActiveYear',
    'teachers.js :: loadSubjects',
    'teachers.js :: loadClassrooms'
];

function findSilentLoaders() {
    const found = [];
    for (const { name, source } of jsFiles()) {
        for (const match of source.matchAll(/async\s+(load[A-Za-z0-9_]*)\s*\([^)]*\)\s*\{/g)) {
            const body = source.slice(match.index, match.index + 1400);
            const catchAt = body.indexOf('catch');
            if (catchAt === -1) continue;

            const catchBlock = body.slice(catchAt, catchAt + 400);
            const surfacesError = /toast\.|this\.error\s*=|showError|notify|alert\(/.test(catchBlock);
            if (!surfacesError) found.push(`${name} :: ${match[1]}`);
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
