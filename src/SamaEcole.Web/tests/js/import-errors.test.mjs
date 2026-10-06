/**
 * Lecture et export des erreurs de l'aperçu d'import de masse (élèves, enseignants) —
 * wwwroot/js/import-errors.js et son branchement dans studentsView / teachersView.
 *
 * Le rendu (partielle _ImportErrorsSummary) se vérifie à l'écran ; ces tests verrouillent ce qui casse en
 * silence : un message perdu, un classement instable, un CSV qu'Excel ouvrirait mal ou exécuterait comme une
 * formule.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const helper = () => loadScripts(['import-errors.js']).window.importErrors;

const LABELS = { fullName: 'Nom complet', birthDate: 'Naissance', gender: 'Genre', classroomName: 'Classe' };

const ROWS = [
    { rowNumber: 2, isValid: true, fullName: 'Awa Ndiaye', birthDate: '12/03/2015', gender: 'F', classroomName: 'CM2 A', fieldErrors: {} },
    { rowNumber: 3, isValid: false, fullName: '', birthDate: '31/02/2015', gender: 'F', classroomName: 'CM2 A',
      fieldErrors: { birthDate: 'Date invalide.', fullName: 'Le nom complet est obligatoire.' } },
    { rowNumber: 5, isValid: false, fullName: 'Modou', birthDate: '01/06/2015', gender: 'M', classroomName: 'Inconnue',
      fieldErrors: { classroomName: 'Classe introuvable.' } }
];

// ---------------------------------------------------------------- logique pure

test('chaque erreur de champ devient une entrée, avec sa valeur saisie et le libellé de la colonne', () => {
    const entries = plain(helper().entries(ROWS, LABELS));

    assert.equal(entries.length, 3);
    assert.deepEqual(entries[2], {
        rowNumber: 5, field: 'classroomName', label: 'Classe', value: 'Inconnue', message: 'Classe introuvable.'
    });
});

test('les entrées sont triées par ligne, puis dans l\'ordre des colonnes — pas dans l\'ordre du serveur', () => {
    const entries = plain(helper().entries(ROWS, LABELS));

    // La ligne 3 renvoie birthDate avant fullName ; l'écran montre fullName en premier.
    assert.deepEqual(entries.filter((e) => e.rowNumber === 3).map((e) => e.field), ['fullName', 'birthDate']);
    assert.deepEqual(entries.map((e) => e.rowNumber), [3, 3, 5]);
});

test('un champ inconnu des libellés n\'est jamais perdu : il passe en dernier, sous son nom technique', () => {
    const rows = [{ rowNumber: 4, isValid: false, fieldErrors: { nouveauChamp: 'Erreur.', fullName: 'Obligatoire.' } }];

    const entries = plain(helper().entries(rows, LABELS));

    assert.deepEqual(entries.map((e) => e.label), ['Nom complet', 'nouveauChamp']);
    assert.equal(entries[1].value, '', 'une valeur absente donne une chaîne vide, jamais « undefined »');
});

test('entrées : tolère l\'absence de lignes, de libellés et de fieldErrors', () => {
    const h = helper();

    assert.deepEqual(plain(h.entries(null, LABELS)), []);
    assert.deepEqual(plain(h.entries([{ rowNumber: 1, isValid: true }], undefined)), []);
});

test('le filtre ne garde que les lignes en erreur, dans leur ordre', () => {
    const rows = plain(helper().invalidRows(ROWS)).map((r) => r.rowNumber);

    assert.deepEqual(rows, [3, 5]);
});

test('une ligne marquée invalide par le serveur reste visible même sans message de champ', () => {
    const h = helper();

    assert.equal(h.rowHasErrors({ rowNumber: 9, isValid: false, fieldErrors: {} }), true);
    assert.equal(h.rowHasErrors({ rowNumber: 9, isValid: true, fieldErrors: { x: 'y' } }), true);
    assert.equal(h.rowHasErrors({ rowNumber: 9, isValid: true, fieldErrors: {} }), false);
});

// ---------------------------------------------------------------- CSV

test('le CSV commence par le BOM UTF-8, utilise « ; » et des fins de ligne CRLF', () => {
    const h = helper();

    const csv = h.toCsv(h.entries(ROWS, LABELS));

    assert.equal(csv.charCodeAt(0), 0xFEFF, 'sans BOM, Excel abîme les accents');
    assert.equal(csv.split('\r\n')[0].slice(1), 'Ligne;Champ;Valeur saisie;Message');
    assert.equal(csv.split('\r\n').length, 5, 'en-tête + 3 erreurs + fin de ligne finale');
    assert.ok(csv.endsWith('\r\n'));
});

test('une cellule contenant « ; », un guillemet ou un saut de ligne est entre guillemets, guillemets doublés', () => {
    const h = helper();
    const csv = h.toCsv([
        { rowNumber: 2, label: 'Nom', value: 'Dia; "Le Grand"\nSow', message: 'Date : 12/03/2015; invalide' }
    ]);

    assert.ok(csv.includes('"Dia; ""Le Grand""\nSow"'));
    assert.ok(csv.includes('"Date : 12/03/2015; invalide"'));
});

test('les accents sont conservés tels quels', () => {
    const h = helper();

    const csv = h.toCsv([{ rowNumber: 2, label: 'Lieu', value: 'Thiès', message: 'Élève introuvable.' }]);

    assert.ok(csv.includes('Thiès') && csv.includes('Élève introuvable.'));
});

test('une valeur qui commencerait une formule Excel est neutralisée (injection CSV)', () => {
    const h = helper();

    for (const dangerous of ['=HYPERLINK("http://x")', '+33 1 02', '-2+3', '@SUM(A1)', '\t=1']) {
        const csv = h.toCsv([{ rowNumber: 2, label: 'Nom', value: dangerous, message: 'm' }]);
        const valueCell = csv.split('\r\n')[1].split(';')[2];
        assert.ok(valueCell.startsWith("'") || valueCell.startsWith('"\''), `${dangerous} → ${valueCell}`);
    }
});

test('un CSV sans erreur ne contient que l\'en-tête', () => {
    assert.equal(helper().toCsv([]).split('\r\n').length, 2);
});

test('nom de fichier : sujet et date LOCALE sur deux chiffres', () => {
    assert.equal(helper().fileName('Eleves', new Date(2026, 9, 6, 23, 59)), 'Rapport-Erreurs-Import-Eleves-2026-10-06.csv');
    assert.equal(helper().fileName('Enseignants', new Date(2026, 0, 5)), 'Rapport-Erreurs-Import-Enseignants-2026-01-05.csv');
});

// ---------------------------------------------------------------- branchement dans les composants

function studentsView() {
    const ctx = loadScripts(['import-errors.js', 'students.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            api: { get: async () => ({ items: [], totalCount: 0 }), toMessage: (_e, f) => f, toFieldErrors: (_e, f) => ({ global: f }) },
            location: { href: 'https://localhost/eleves', search: '' }
        }
    });
    const view = ctx.initAlpine().get('studentsView')();
    return { view, ctx };
}

function teachersView() {
    const ctx = loadScripts(['import-errors.js', 'teachers.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            api: { get: async () => ({ items: [], totalCount: 0 }), toMessage: (_e, f) => f, toFieldErrors: (_e, f) => ({ global: f }) },
            location: { href: 'https://localhost/enseignants', search: '' }
        }
    });
    const view = ctx.initAlpine().get('teachersView')();
    return { view, ctx };
}

test('élèves : sans aperçu, aucune erreur et aucune ligne', () => {
    const { view } = studentsView();

    assert.deepEqual(plain(view.importErrorEntries), []);
    assert.deepEqual(plain(view.importVisibleRows), []);
});

test('élèves : le récapitulatif utilise les libellés des colonnes de l\'écran', () => {
    const { view } = studentsView();
    view.importPreview = {
        totalRows: 2, validRows: 1, invalidRows: 1,
        rows: [
            { rowNumber: 2, isValid: true, fullName: 'Awa', fieldErrors: {} },
            { rowNumber: 3, isValid: false, guardianPhone: '12', guardianEmail: 'x', fieldErrors: { guardianEmail: 'E-mail invalide.', guardianPhone: 'Téléphone invalide.' } }
        ]
    };

    const entries = plain(view.importErrorEntries);

    assert.deepEqual(entries.map((e) => e.label), ['Tél. tuteur', 'E-mail tuteur']);
    assert.equal(entries[0].value, '12');
});

test('élèves : cocher « lignes en erreur seulement » filtre l\'aperçu, et le décocher le rétablit', () => {
    const { view } = studentsView();
    view.importPreview = { rows: ROWS };

    assert.equal(view.importVisibleRows.length, 3);
    view.importOnlyErrors = true;
    assert.deepEqual(plain(view.importVisibleRows).map((r) => r.rowNumber), [3, 5]);
    view.importOnlyErrors = false;
    assert.equal(view.importVisibleRows.length, 3);
});

test('élèves : l\'export envoie le CSV des erreurs sous un nom daté « Eleves »', () => {
    const { view, ctx } = studentsView();
    view.importPreview = { rows: ROWS };
    const downloads = [];
    ctx.window.importErrors.download = (name, content) => downloads.push({ name, content });

    view.exportImportErrors();

    assert.equal(downloads.length, 1);
    assert.match(downloads[0].name, /^Rapport-Erreurs-Import-Eleves-\d{4}-\d{2}-\d{2}\.csv$/);
    assert.equal(downloads[0].content.split('\r\n').length, 5);
});

test('élèves : fermer ou recommencer un import remet le filtre à zéro', () => {
    const { view } = studentsView();
    view.importOnlyErrors = true;

    view.resetImportState();

    assert.equal(view.importOnlyErrors, false);
});

test('enseignants : libellés propres à leur aperçu et export sous le nom « Enseignants »', () => {
    const { view, ctx } = teachersView();
    view.importPreview = {
        rows: [{ rowNumber: 2, isValid: false, email: 'pas-un-mail', subjects: '', fieldErrors: { subjects: 'Matière inconnue.', email: 'E-mail invalide.' } }]
    };
    const downloads = [];
    ctx.window.importErrors.download = (name, content) => downloads.push({ name, content });

    assert.deepEqual(plain(view.importErrorEntries).map((e) => e.label), ['E-mail', 'Matières']);

    view.exportImportErrors();
    assert.match(downloads[0].name, /^Rapport-Erreurs-Import-Enseignants-/);
});

test('enseignants : le filtre se réinitialise comme pour les élèves', () => {
    const { view } = teachersView();
    view.importOnlyErrors = true;

    view.resetImportState();

    assert.equal(view.importOnlyErrors, false);
});

test('un fichier corrigé redéposé avec le filtre encore coché n\'affiche pas un aperçu vide', () => {
    const { view } = studentsView();
    view.importOnlyErrors = true;
    view.importPreview = { rows: [ROWS[0]] }; // plus aucune erreur : la case du récapitulatif est masquée

    assert.equal(view.importVisibleRows.length, 1);
});

test('déposer un nouveau fichier remet le filtre à zéro (élèves et enseignants)', () => {
    for (const make of [studentsView, teachersView]) {
        const { view } = make();
        view.previewImport = async () => {};
        view.importOnlyErrors = true;

        view.onImportFileSelected({ name: 'corrige.csv' });

        assert.equal(view.importOnlyErrors, false);
    }
});
