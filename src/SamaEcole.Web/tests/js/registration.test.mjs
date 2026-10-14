/**
 * Formulaire PUBLIC d'inscription self-service (wwwroot/js/registration.js).
 *
 * Garde-fou fondateur de ce fichier : le 04/09/2026, `validate()` référençait `isSafeText`,
 * `passwordPolicyErrors`, `EMAIL_REGEX` et `SENEGAL_PHONE_REGEX`, tous déclarés à l'intérieur de
 * l'IIFE en tête de registration.js — mais le composant Alpine `registrationForm` qui les appelle
 * est enregistré dans un bloc SÉPARÉ, hors de portée de cette IIFE. Chaque clic sur « Envoyer ma
 * demande » levait donc un ReferenceError synchrone AVANT le try/catch de submit(), sans le moindre
 * retour visible à l'écran (ni spinner, ni bandeau d'erreur) — un bug qu'aucun test ne couvrait.
 *
 * `npm test` exécute réellement le fichier (via node:vm, voir harness.mjs) : un helper qui redevient
 * hors de portée fait échouer le premier test ci-dessous avec la même erreur qu'à l'écran, plutôt que
 * d'attendre qu'un Directeur signale que le bouton « ne fait rien ».
 *
 * Un établissement PRIVÉ choisit une offre par tranche d'effectif (profil, internat, cycles, effectif exact,
 * tranche présélectionnée) ; un PUBLIC garde l'offre historique à un cycle. Les prix affichés viennent de la
 * grille publique du serveur (ici une doublure), jamais d'un montant écrit dans le script.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const row = (tier, maxStudents, softLimit, monthlyXof, annualXof, surchargeM = 0, surchargeA = 0) => ({
    tier, maxStudents, softLimit, monthlyXof, annualXof,
    boardingSurchargeMonthlyXof: surchargeM, boardingSurchargeAnnualXof: surchargeA
});

const GRID = {
    currency: 'XOF',
    quoteAboveStudents: 400,
    tiers: [
        row('Tier_100', 100, 110, 5000, 50000),
        row('Tier1_150', 150, 160, 10000, 100000),
        row('Tier_250', 250, 260, 15000, 150000),
        row('Tier2_400', 400, 420, 25000, 250000)
    ]
};

/** fetch de test : sert la grille publique ; les soumissions sont consignées dans `calls`, la réponse est réglable. */
function fakeFetch({ grid = GRID, submit, calls = [] } = {}) {
    const fetchStub = async (url, init) => {
        if (String(url).endsWith('/public/pricing-grid')) {
            if (!grid) return { ok: false, status: 500, json: async () => null };
            return { ok: true, status: 200, json: async () => grid };
        }
        calls.push({ url, body: JSON.parse(init.body) });
        if (submit) return submit(url, init);
        return { ok: true, status: 200, json: async () => ({ trackingReference: 'REG-TEST1234' }) };
    };
    fetchStub.calls = calls;
    return fetchStub;
}

function boot(fetchStub, search = '') {
    return loadScripts(['api.js', 'registration.js'], {
        fetch: fetchStub || fakeFetch(),
        preload: { location: { protocol: 'https:', hostname: 'localhost', pathname: '/inscription', search } }
    });
}

/** Formulaire PRIVÉ complet et valide (Enseignement général, Élémentaire + Moyen, 120 élèves → tranche 150). */
async function mountValidForm(fetchStub) {
    const ctx = boot(fetchStub);
    const form = ctx.component('registrationForm');
    await form.loadGrid();

    form.schoolName = 'Complexe Touba Darou Karim';
    form.city = 'Touba';
    form.region = 'Diourbel';
    form.schoolAddress = 'Darou Karim';
    form.selectOwnership('Private');
    form.selectProfile('EnseignementGeneral');
    form.toggleCycle('Primaire');
    form.toggleCycle('College');
    form.estimatedStudentCount = 120;
    form.syncTier();
    form.directorFullName = 'Cheikh Mbacké Nguirane';
    form.directorEmail = 'nguirane600@gmail.com';
    form.directorPhone = '763545916';
    form.directorPassword = 'TestPassw0rd!2026';

    return { ctx, form };
}

test('garde-fou : validate() ne lève aucune ReferenceError sur un formulaire valide', async () => {
    const { form } = await mountValidForm();

    // Un throw ici (isSafeText/passwordPolicyErrors/EMAIL_REGEX hors de portée) ferait échouer le
    // test tout seul — c'est précisément le scénario du bug fondateur.
    const isValid = form.validate();

    assert.equal(isValid, true);
    assert.deepEqual(plain(form.errors), {});
});

test('un caractère HTML dans le nom d\'établissement est rejeté (isSafeText)', async () => {
    const { form } = await mountValidForm();
    form.schoolName = '<script>alert(1)</script>';

    assert.equal(form.validate(), false);
    assert.match(form.errors.schoolname, /caractères interdits/);
});

test('un mot de passe trop faible est rejeté avec un motif précis (passwordPolicyErrors)', async () => {
    const { form } = await mountValidForm();
    form.directorPassword = 'password';

    assert.equal(form.validate(), false);
    assert.ok(form.errors.directorpassword, 'errors.directorpassword devrait être renseigné');
});

test('un téléphone hors format sénégalais est rejeté (SENEGAL_PHONE_REGEX)', async () => {
    const { form } = await mountValidForm();
    form.directorPhone = '123456';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directorphone, /sénégalais/);
});

test('une adresse e-mail mal formée est rejetée (EMAIL_REGEX)', async () => {
    const { form } = await mountValidForm();
    form.directorEmail = 'pas-un-email';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directoremail, /invalide/);
});

test('un domaine e-mail sans TLD alphabétique valide est rejeté (contrôle strict du domaine)', async () => {
    const { form } = await mountValidForm();
    form.directorEmail = 'awa@monecole.c1';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directoremail, /invalide/);
});

test('un nom complet contenant un chiffre est rejeté (isValidName)', async () => {
    const { form } = await mountValidForm();
    form.directorFullName = 'Awa123';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directorfullname, /2 lettres/);
});

test('un nom complet d\'une seule lettre est rejeté (minimum 2 lettres)', async () => {
    const { form } = await mountValidForm();
    form.directorFullName = 'A';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directorfullname, /2 lettres/);
});

test('un nom complet avec tiret et accents est accepté (isValidName)', async () => {
    const { form } = await mountValidForm();
    form.directorFullName = 'Ndèye-Awa Fall';

    assert.equal(form.validate(), true);
});

test('un mot de passe de 8 caractères respectant toutes les règles est désormais accepté', async () => {
    const { form } = await mountValidForm();
    form.directorPassword = 'Kx7!mQ2p';

    assert.equal(form.validate(), true);
});

test('un mot de passe de 7 caractères reste rejeté (minimum 8)', async () => {
    const { form } = await mountValidForm();
    form.directorPassword = 'Kx7!mQp';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directorpassword, /8 caractères/);
});

test('soumission valide : la requête part et la référence de suivi s\'affiche', async () => {
    const fetchStub = fakeFetch();
    const { form } = await mountValidForm(fetchStub);

    await form.submit();

    assert.equal(fetchStub.calls.length, 1, 'submit() doit effectivement appeler fetch (régression : throw avant envoi)');
    assert.equal(fetchStub.calls[0].url, '/api/v1/registration-requests');
    assert.equal(fetchStub.calls[0].body.schoolName, 'Complexe Touba Darou Karim');
    assert.equal(form.trackingReference, 'REG-TEST1234');
    assert.equal(form.isSubmitting, false);
});

test('un 422 serveur retombe sous le bon champ (window.api.toFieldErrors)', async () => {
    const fetchStub = fakeFetch({
        submit: async () => ({
            ok: false,
            status: 422,
            json: async () => ({
                message: 'Une ou plusieurs erreurs de validation se sont produites.',
                code: 'VALIDATION_ERROR',
                details: {
                    DirectorPhone: ['Le numéro de téléphone doit être un numéro sénégalais valide (ex: 77 123 45 67).']
                }
            })
        })
    });
    const { form } = await mountValidForm(fetchStub);

    await form.submit();

    assert.equal(form.trackingReference, null);
    assert.match(form.errors.directorphone, /sénégalais/);
    assert.equal(form.isSubmitting, false);
});

test('un 422 sur la tranche retombe sous le champ tranche', async () => {
    const fetchStub = fakeFetch({
        submit: async () => ({
            ok: false,
            status: 422,
            json: async () => ({
                message: 'Validation.', code: 'VALIDATION_ERROR',
                details: { Tier: ['Votre effectif déclaré (130 élèves) dépasse la capacité de la tranche choisie.'] }
            })
        })
    });
    const { form } = await mountValidForm(fetchStub);

    await form.submit();

    assert.match(form.errors.tier, /130/);
});

// ---------------------------------------------------------------- Type d'établissement

test('aucun type d\'établissement n\'est présélectionné : le choix est explicite', () => {
    const form = boot().component('registrationForm');

    assert.equal(form.ownership, '');
    assert.equal(form.cycleProfile, '');
    assert.equal(form.profile, '');
    assert.equal(form.tier, '');
});

test('sans type d\'établissement, la validation refuse et nomme le champ', async () => {
    const { form } = await mountValidForm();
    form.ownership = '';

    assert.equal(form.validate(), false);
    assert.match(form.errors.ownership, /public ou privé/);
});

test('un public gère un seul cycle et n\'a ni profil, ni tranche, ni cycles cochés à donner', async () => {
    const { form } = await mountValidForm();

    form.selectOwnership('Public');
    assert.deepEqual(Array.from(form.cycleOptions.map((o) => o.value)), ['Primaire', 'College', 'Lycee']);
    assert.equal(form.validate(), false);
    assert.match(form.errors.cycleprofile, /cycle/);

    form.cycleProfile = 'College';
    assert.equal(form.validate(), true);
    assert.equal(form.profile, '');
    assert.equal(form.tier, '');
    assert.deepEqual(Array.from(form.cycles), []);
});

test('un privé n\'a pas de liste de cycles « historique » : il choisit un profil puis ses cycles', async () => {
    const { form } = await mountValidForm();

    assert.deepEqual(Array.from(form.cycleOptions), []);
    assert.deepEqual(Array.from(form.profileOptions.map((p) => p.value)),
        ['EnseignementGeneral', 'FrancoArabe', 'DaaraMemorisation', 'ComptabiliteRapports']);
});

test('passer de privé à public efface le profil, les cycles, l\'internat et la tranche', async () => {
    const { form } = await mountValidForm();
    form.hasBoarding = true;

    form.selectOwnership('Public');

    assert.equal(form.profile, '');
    assert.deepEqual(Array.from(form.cycles), []);
    assert.equal(form.hasBoarding, false);
    assert.equal(form.tier, '');
});

// ---------------------------------------------------------------- Profil et cycles

test('les cycles ne se demandent que pour un profil académique', async () => {
    const { form } = await mountValidForm();

    form.selectProfile('EnseignementGeneral');
    assert.equal(form.needsCycles, true);
    form.selectProfile('FrancoArabe');
    assert.equal(form.needsCycles, true);
    form.selectProfile('DaaraMemorisation');
    assert.equal(form.needsCycles, false);
    form.selectProfile('ComptabiliteRapports');
    assert.equal(form.needsCycles, false);
});

test('choisir un Daara efface les cycles déjà cochés, qui ne sont plus demandés', async () => {
    const { form } = await mountValidForm();
    assert.equal(form.cycles.length, 2);

    form.selectProfile('DaaraMemorisation');

    assert.deepEqual(Array.from(form.cycles), []);
    assert.equal(form.validate(), true, 'un Daara n\'a pas de cycle à cocher');
});

test('les quatre cycles ont les libellés du Directeur et se cochent dans l\'ordre canonique', async () => {
    const { form } = await mountValidForm();

    assert.deepEqual(Array.from(form.cycleChoices.map((c) => c.label)), ['Préscolaire', 'Élémentaire', 'Moyen', 'Secondaire']);

    form.cycles = [];
    form.toggleCycle('Lycee');
    form.toggleCycle('Maternelle');
    assert.deepEqual(Array.from(form.cycles), ['Maternelle', 'Lycee']);

    form.toggleCycle('Lycee');
    assert.deepEqual(Array.from(form.cycles), ['Maternelle']);
});

test('un profil académique sans aucun cycle coché est refusé', async () => {
    const { form } = await mountValidForm();
    form.cycles = [];

    assert.equal(form.validate(), false);
    assert.match(form.errors.cycles, /au moins un cycle/);
});

test('un seul cycle suffit : préscolaire seul, ou élémentaire seul', async () => {
    const { form } = await mountValidForm();

    form.cycles = [];
    form.toggleCycle('Maternelle');
    assert.equal(form.validate(), true);

    form.cycles = [];
    form.toggleCycle('Primaire');
    assert.equal(form.validate(), true);
});

test('un privé doit choisir un profil', async () => {
    const { form } = await mountValidForm();
    form.profile = '';

    assert.equal(form.validate(), false);
    assert.match(form.errors.profile, /profil/);
});

// ---------------------------------------------------------------- Effectif et tranche

test('la tranche se présélectionne d\'après l\'effectif saisi', async () => {
    const { form } = await mountValidForm();

    for (const [count, expected] of [[60, 'Tier_100'], [100, 'Tier_100'], [101, 'Tier1_150'], [250, 'Tier_250'], [251, 'Tier2_400'], [400, 'Tier2_400']]) {
        form.estimatedStudentCount = count;
        form.syncTier();
        assert.equal(form.tier, expected, `effectif ${count}`);
    }
});

test('une tranche plus petite que l\'effectif n\'est pas choisissable', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = 200;
    form.syncTier();

    assert.equal(form.isTierDisabled('Tier_100'), true);
    assert.equal(form.isTierDisabled('Tier1_150'), true);
    assert.equal(form.isTierDisabled('Tier_250'), false);

    form.selectTier('Tier_100');

    assert.equal(form.tier, 'Tier_250', 'le clic sur une tranche trop petite est sans effet');
});

test('le Directeur peut prendre une tranche plus grande : elle n\'est pas reprise quand l\'effectif change', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = 80;
    form.syncTier();
    assert.equal(form.tier, 'Tier_100');

    form.selectTier('Tier_250');
    form.estimatedStudentCount = 90;
    form.syncTier();

    assert.equal(form.tier, 'Tier_250');
});

test('une tranche choisie à la main est relevée si l\'effectif dépasse son plafond', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = 80;
    form.syncTier();
    form.selectTier('Tier1_150');

    form.estimatedStudentCount = 180;
    form.syncTier();

    assert.equal(form.tier, 'Tier_250');
});

test('tant que le Directeur n\'a rien choisi, la tranche suit l\'effectif dans les deux sens', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = 200;
    form.syncTier();
    assert.equal(form.tier, 'Tier_250');

    form.estimatedStudentCount = 60;
    form.syncTier();

    assert.equal(form.tier, 'Tier_100');
});

test('au-delà de 400 élèves : Grand Groupe, sur devis, aucune tranche et envoi refusé', async () => {
    const fetchStub = fakeFetch();
    const { form } = await mountValidForm(fetchStub);

    form.estimatedStudentCount = 410;
    form.syncTier();

    assert.equal(form.requiresQuote, true);
    assert.equal(form.tier, '');
    assert.equal(form.validate(), false);
    assert.match(form.errors.tier, /Grand Groupe/);

    await form.submit();
    assert.equal(fetchStub.calls.length, 0, 'rien ne part tant que la demande est sur devis');
});

test('l\'effectif exact est obligatoire pour un privé, facultatif pour un public', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = '';

    assert.equal(form.validate(), false);
    assert.match(form.errors.estimatedstudentcount, /effectif exact/);

    form.selectOwnership('Public');
    form.cycleProfile = 'Primaire';
    assert.equal(form.validate(), true);
});

test('un effectif non entier est refusé', async () => {
    const { form } = await mountValidForm();
    form.estimatedStudentCount = 12.5;

    assert.equal(form.validate(), false);
    assert.match(form.errors.estimatedstudentcount, /entier/);
});

test('sans tranche choisie, la validation le dit', async () => {
    const { form } = await mountValidForm();
    form.tier = '';

    assert.equal(form.validate(), false);
    assert.match(form.errors.tier, /tranche/);
});

test('si la grille n\'a pas pu être chargée, le privé ne peut pas envoyer et le dit', async () => {
    const fetchStub = fakeFetch({ grid: null });
    const { form } = await mountValidForm(fetchStub);

    assert.match(form.gridError, /tarifs/);
    assert.equal(form.validate(), false);
    assert.match(form.errors.tier, /tarifs/);
});

// ---------------------------------------------------------------- Prix affichés

test('les prix viennent de la grille du serveur : mensuel, annuel et mois payés', async () => {
    const { form } = await mountValidForm();

    assert.equal(form.tier, 'Tier1_150');
    assert.deepEqual(plain(form.quote), { monthly: 10000, annual: 100000, paidMonths: 10, boarding: false });
    assert.deepEqual(Array.from(form.tierOptions.map((o) => o.monthly)), [5000, 10000, 15000, 25000]);
});

test('les libellés de tranche reprennent les bornes de la grille', async () => {
    const { form } = await mountValidForm();

    assert.deepEqual(Array.from(form.tierOptions.map((o) => o.label)), [
        "Jusqu'à 100 élèves", '101 à 150 élèves', '151 à 250 élèves', '251 à 400 élèves'
    ]);
});

test('l\'internat ajoute la majoration de la grille aux prix affichés', async () => {
    const grid = { ...GRID, tiers: GRID.tiers.map((t) => (t.tier === 'Tier1_150' ? row('Tier1_150', 150, 160, 10000, 100000, 2000, 20000) : t)) };
    const { form } = await mountValidForm(fakeFetch({ grid }));

    assert.equal(form.quote.monthly, 10000);

    form.hasBoarding = true;

    assert.deepEqual(plain(form.quote), { monthly: 12000, annual: 120000, paidMonths: 10, boarding: true });
});

// ---------------------------------------------------------------- Corps envoyé

test('le formulaire n\'envoie aucun montant ni formule : il envoie profil, internat, cycles et tranche', async () => {
    const fetchStub = fakeFetch();
    const { form } = await mountValidForm(fetchStub);
    form.hasBoarding = true;

    await form.submit();

    const body = fetchStub.calls[0].body;
    assert.equal('requestedPlan' in body, false);
    assert.equal(Object.keys(body).some((key) => /price|amount|xof|plafond/i.test(key)), false);
    assert.equal(body.ownership, 'Private');
    assert.equal(body.profile, 'EnseignementGeneral');
    assert.equal(body.hasBoarding, true);
    assert.deepEqual(body.cycles, ['Primaire', 'College']);
    assert.equal(body.tier, 'Tier1_150');
    assert.equal(body.estimatedStudentCount, 120);
    assert.equal('cycleProfile' in body, false);
    assert.equal('sizeTier' in body, false);
});

test('un Daara est envoyé sans cycles', async () => {
    const fetchStub = fakeFetch();
    const { form } = await mountValidForm(fetchStub);
    form.selectProfile('DaaraMemorisation');

    await form.submit();

    assert.equal(fetchStub.calls[0].body.profile, 'DaaraMemorisation');
    assert.equal(fetchStub.calls[0].body.cycles, null);
});

test('un établissement public est envoyé avec son cycle, sans profil ni tranche', async () => {
    const fetchStub = fakeFetch();
    const { form } = await mountValidForm(fetchStub);
    form.selectOwnership('Public');
    form.cycleProfile = 'College';
    form.estimatedStudentCount = '';

    await form.submit();

    const body = fetchStub.calls[0].body;
    assert.equal(body.ownership, 'Public');
    assert.equal(body.cycleProfile, 'College');
    assert.equal(body.sizeTier, null);
    assert.equal('profile' in body, false);
    assert.equal('tier' in body, false);
});

// ---------------------------------------------------------------- Liens de la vitrine

test('arriver depuis une carte de tranche préremplit le type et la tranche', async () => {
    const ctx = boot(fakeFetch(), '?type=prive&tranche=Tier_250');
    const form = ctx.component('registrationForm');
    await form.loadGrid();

    assert.equal(form.ownership, 'Private');
    assert.equal(form.tier, 'Tier_250');
});

test('arriver depuis la vitrine avec un profil préremplit le profil', () => {
    const form = boot(fakeFetch(), '?type=prive&profil=FrancoArabe').component('registrationForm');

    assert.equal(form.ownership, 'Private');
    assert.equal(form.profile, 'FrancoArabe');
});

test('un lien de vitrine public préremplit le cycle', () => {
    const form = boot(fakeFetch(), '?type=public&cycles=College').component('registrationForm');

    assert.equal(form.ownership, 'Public');
    assert.equal(form.cycleProfile, 'College');
});

test('un lien de vitrine inconnu ou incohérent ne préremplit rien de faux', async () => {
    const form = boot(fakeFetch(), '?type=public&cycles=Complexe').component('registrationForm');
    assert.equal(form.ownership, 'Public');
    assert.equal(form.cycleProfile, '', 'un public n\'a pas de grand complexe');

    const unknownTier = boot(fakeFetch(), '?type=prive&tranche=Tier_9999&profil=Inconnu').component('registrationForm');
    await unknownTier.loadGrid();
    assert.equal(unknownTier.tier, '');
    assert.equal(unknownTier.profile, '');
});
