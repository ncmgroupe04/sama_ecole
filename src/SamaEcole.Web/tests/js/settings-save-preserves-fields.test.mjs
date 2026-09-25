/**
 * Écran Paramètres (`settingsView`, wwwroot/js/settings.js) — `saveConfig` ne doit JAMAIS écraser
 * un réglage qu'il ne pilote pas.
 *
 * PUT /schools/current/settings remplace l'ensemble des réglages : un champ omis retombe à sa valeur
 * par défaut côté serveur. Les alertes SMS et le seuil de relance des impayés sont écrits par l'écran
 * SMS (sms-settings.js), pas par celui-ci — l'ancien saveConfig les omettait, et chaque
 * enregistrement de Paramètres › Configuration les remettait à zéro sans aucun message.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const SERVER_SETTINGS = () => ({
    gradingScale: '20',
    studentMatriculeFormat: 'ELEV-{YEAR}-{SEQ:4}',
    teacherMatriculeFormat: 'ENS-{YEAR}-{SEQ:3}',
    autoLogoutMinutes: 10,
    dateFormat: 'dd/MM/yyyy',
    tuitionMonthsPerYear: 9,
    allowSecretaryToManageGrading: false,
    allowFinanceToModifyFees: false,
    allowFinanceToDeleteFees: false,
    typeEtablissement: 'Prive',
    smsOnAttendanceAlert: true,
    smsOnDuesReminder: true,
    smsOnPaymentReceipt: true,
    smsCreditBalance: 120,
    debtorReminderThresholdDays: 30,
    isPedagogyEnabled: true,
    isFinanceEnabled: true,
    isInternatEnabled: false,
    isCoranModuleEnabled: false,
    gradeEditWindowDays: 7,
    evaluationPeriodType: 'Semester',
    customPeriodCount: 3
});

/**
 * Instancie l'écran ; `serverState()` est ce que GET /schools/current/settings renvoie À CET
 * INSTANT (une fonction, pour simuler un enregistrement de l'écran SMS entre deux appels), et
 * `puts` collecte les corps envoyés en PUT. Le serveur factice réécho le corps reçu.
 */
async function settingsView(serverState) {
    const puts = [];
    const ctx = loadScripts(['settings.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') return serverState();
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (_endpoint, body) => { puts.push(plain(body)); return body; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/parametres', search: '' },
            history: { replaceState() {} }
        }
    });
    const view = ctx.component('settingsView');
    await flush();
    return { view, puts };
}

test('enregistrer la configuration conserve les alertes SMS et le seuil de relance', async () => {
    const { view, puts } = await settingsView(SERVER_SETTINGS);

    view.config.autoLogoutMinutes = 15;
    await view.saveConfig();

    assert.equal(puts.length, 1);
    assert.equal(puts[0].autoLogoutMinutes, 15, 'le champ modifié part bien');
    assert.equal(puts[0].smsOnAttendanceAlert, true);
    assert.equal(puts[0].smsOnDuesReminder, true);
    assert.equal(puts[0].smsOnPaymentReceipt, true);
    assert.equal(puts[0].debtorReminderThresholdDays, 30);
});

test('un enregistrement de l\'écran SMS survenu après le chargement n\'est pas écrasé par une valeur périmée', async () => {
    let server = SERVER_SETTINGS();
    const { view, puts } = await settingsView(() => server);

    // Entre le chargement de Paramètres et le clic sur Enregistrer, le Directeur coupe les alertes
    // d'assiduité depuis l'onglet SMS et relève le seuil.
    server = { ...server, smsOnAttendanceAlert: false, debtorReminderThresholdDays: 45 };

    view.config.dateFormat = 'dd MMMM yyyy';
    await view.saveConfig();

    assert.equal(puts[0].dateFormat, 'dd MMMM yyyy');
    assert.equal(puts[0].smsOnAttendanceAlert, false, 'la coupure faite depuis l\'écran SMS est respectée');
    assert.equal(puts[0].debtorReminderThresholdDays, 45);
});

test('les champs pilotés par cet écran priment sur l\'état serveur', async () => {
    const { view, puts } = await settingsView(SERVER_SETTINGS);

    view.config.allowFinanceToModifyFees = true;
    view.config.gradeEditWindowDays = 14;
    await view.saveConfig();

    assert.equal(puts[0].allowFinanceToModifyFees, true);
    assert.equal(puts[0].gradeEditWindowDays, 14);
});

test('le découpage de l\'année est chargé, envoyé une fois modifié, et conservé sinon', async () => {
    const { view, puts } = await settingsView(SERVER_SETTINGS);

    assert.equal(view.config.evaluationPeriodType, 'Semester', 'chargé depuis le serveur');
    assert.equal(view.config.customPeriodCount, 3);

    // Enregistrer un autre champ ne doit pas ramener le découpage à son défaut.
    view.config.autoLogoutMinutes = 20;
    await view.saveConfig();
    assert.equal(puts[0].evaluationPeriodType, 'Semester');

    view.config.evaluationPeriodType = 'Custom';
    view.config.customPeriodCount = 4;
    await view.saveConfig();

    assert.equal(puts[1].evaluationPeriodType, 'Custom');
    assert.equal(puts[1].customPeriodCount, 4);
});

test('un commutateur (saveConfig(false)) ne change pas le libellé des boutons Enregistrer, un vrai enregistrement oui', async () => {
    let seen = null;
    const ctx = loadScripts(['settings.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async (endpoint) => (endpoint === '/schools/current/settings' ? SERVER_SETTINGS()
                    : endpoint === '/schools/current/mode' ? { isLive: false }
                    : endpoint === '/schools/current' ? { name: 'École test' } : []),
                put: async (_endpoint, body) => { seen = { saving: view.configSaving, label: view.configSavingLabelVisible }; return body; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/parametres', search: '' },
            history: { replaceState() {} }
        }
    });
    const view = ctx.component('settingsView');
    await flush();

    await view.saveConfig(false);
    assert.deepEqual(seen, { saving: true, label: false }, 'boutons désactivés mais libellé (donc largeur) inchangé');
    assert.equal(view.configSaved, false);

    await view.saveConfig();
    assert.deepEqual(seen, { saving: true, label: true }, 'le formulaire affiche « Enregistrement… »');
    assert.equal(view.configSavingLabelVisible, false, 'remis à zéro après coup');
    assert.equal(view.configSaving, false);
});
