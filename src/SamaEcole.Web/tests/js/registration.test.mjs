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
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function boot(fetchStub) {
    return loadScripts(['api.js', 'registration.js'], fetchStub ? { fetch: fetchStub } : {});
}

function mountValidForm(fetchStub) {
    const ctx = boot(fetchStub);
    const form = ctx.component('registrationForm');

    form.schoolName = 'Complexe Touba Darou Karim';
    form.city = 'Touba';
    form.region = 'Diourbel';
    form.schoolAddress = 'Darou Karim';
    form.estimatedStudentCount = 2500;
    form.requestedPlan = 'Premium';
    form.directorFullName = 'Cheikh Mbacké Nguirane';
    form.directorEmail = 'nguirane600@gmail.com';
    form.directorPhone = '763545916';
    form.directorPassword = 'TestPassw0rd!2026';

    return { ctx, form };
}

test('garde-fou : validate() ne lève aucune ReferenceError sur un formulaire valide', () => {
    const { form } = mountValidForm();

    // Un throw ici (isSafeText/passwordPolicyErrors/EMAIL_REGEX hors de portée) ferait échouer le
    // test tout seul — c'est précisément le scénario du bug fondateur.
    const isValid = form.validate();

    assert.equal(isValid, true);
    assert.deepEqual(plain(form.errors), {});
});

test('un caractère HTML dans le nom d\'établissement est rejeté (isSafeText)', () => {
    const { form } = mountValidForm();
    form.schoolName = '<script>alert(1)</script>';

    assert.equal(form.validate(), false);
    assert.match(form.errors.schoolname, /caractères interdits/);
});

test('un mot de passe trop faible est rejeté avec un motif précis (passwordPolicyErrors)', () => {
    const { form } = mountValidForm();
    form.directorPassword = 'password';

    assert.equal(form.validate(), false);
    assert.ok(form.errors.directorpassword, 'errors.directorpassword devrait être renseigné');
});

test('un téléphone hors format sénégalais est rejeté (SENEGAL_PHONE_REGEX)', () => {
    const { form } = mountValidForm();
    form.directorPhone = '123456';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directorphone, /sénégalais/);
});

test('une adresse e-mail mal formée est rejetée (EMAIL_REGEX)', () => {
    const { form } = mountValidForm();
    form.directorEmail = 'pas-un-email';

    assert.equal(form.validate(), false);
    assert.match(form.errors.directoremail, /invalide/);
});

test('soumission valide : la requête part et la référence de suivi s\'affiche', async () => {
    const calls = [];
    const fetchStub = async (url, init) => {
        calls.push({ url, body: JSON.parse(init.body) });
        return { ok: true, status: 200, json: async () => ({ trackingReference: 'REG-TEST1234' }) };
    };
    const { form } = mountValidForm(fetchStub);

    await form.submit();

    assert.equal(calls.length, 1, 'submit() doit effectivement appeler fetch (régression : throw avant envoi)');
    assert.equal(calls[0].url, '/api/v1/registration-requests');
    assert.equal(calls[0].body.schoolName, 'Complexe Touba Darou Karim');
    assert.equal(form.trackingReference, 'REG-TEST1234');
    assert.equal(form.isSubmitting, false);
});

test('un 422 serveur retombe sous le bon champ (window.api.toFieldErrors)', async () => {
    const fetchStub = async () => ({
        ok: false,
        status: 422,
        json: async () => ({
            message: 'Une ou plusieurs erreurs de validation se sont produites.',
            code: 'VALIDATION_ERROR',
            details: {
                DirectorPhone: ['Le numéro de téléphone doit être un numéro sénégalais valide (ex: 77 123 45 67).']
            }
        })
    });
    const { form } = mountValidForm(fetchStub);

    await form.submit();

    assert.equal(form.trackingReference, null);
    assert.match(form.errors.directorphone, /sénégalais/);
    assert.equal(form.isSubmitting, false);
});
