/**
 * Pagination complète d'une collection (`window.api.getAllPages`, wwwroot/js/api.js).
 *
 * Le test qui compte vraiment ici est « aucune requête ne demande plus de 100 lignes » : c'est
 * exactement ce qui a cassé les trois écrans du module Surveillant. Ils demandaient
 * `/students?page=1&pageSize=1000` ; les validateurs serveur plafonnent `pageSize` à 100
 * (GetStudentsQueryValidator.MaxPageSize), la requête repartait en 422 VALIDATION_ERROR, le `catch`
 * remettait la liste à vide — et le menu déroulant restait désespérément vide SANS aucun message.
 * Billet d'entrée, billet de sortie, convocation de parent et registre de discipline étaient donc
 * tous inutilisables, sans la moindre trace à l'écran.
 *
 * Le second test (« au-delà de 100 élèves, aucun n'est perdu ») fige l'autre moitié de la
 * correction : se contenter de demander 100 lignes aurait remplacé un bug visible par un bug
 * invisible — le 101ᵉ élève introuvable dans le sélecteur, sans que rien ne le signale.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/**
 * Doublure de fetch qui sert une collection paginée façon `PaginatedStudents`
 * (`{ items, totalCount, page, pageSize }`), en relisant page/pageSize dans l'URL.
 */
function pagedFetch(totalCount, { rejectAbove = 100 } = {}) {
    const calls = [];

    const fetchStub = async (url, init) => {
        const query = new URL(url, 'https://localhost').searchParams;
        const page = Number(query.get('page'));
        const pageSize = Number(query.get('pageSize'));
        calls.push({ url, page, pageSize, method: init?.method });

        // Reproduit fidèlement le serveur : au-delà du plafond, 422 VALIDATION_ERROR.
        if (pageSize > rejectAbove) {
            return {
                ok: false,
                status: 422,
                json: async () => ({ code: 'VALIDATION_ERROR', message: 'PageSize trop grand.' })
            };
        }

        const start = (page - 1) * pageSize;
        const items = Array.from(
            { length: Math.max(0, Math.min(pageSize, totalCount - start)) },
            (_, i) => ({ id: `eleve-${start + i + 1}` })
        );

        return {
            ok: true,
            status: 200,
            json: async () => ({ items, totalCount, page, pageSize })
        };
    };

    return { fetchStub, calls };
}

function bootApi(fetchStub) {
    const ctx = loadScripts(['api.js'], {
        fetch: fetchStub,
        preload: {
            auth: {
                accessToken: 'jwt-de-test',
                isAuthenticated: () => false,
                isAccessTokenStale: () => false,
                redirectToLogin() {}
            }
        }
    });
    ctx.window.api.retryBaseDelayMs = 1;
    return ctx.window.api;
}

test('getAllPages ne demande jamais plus de 100 lignes par requête', async () => {
    const { fetchStub, calls } = pagedFetch(250);
    const api = bootApi(fetchStub);

    await api.getAllPages('/students');

    assert.ok(calls.length > 0, 'au moins une requête doit partir');
    for (const call of calls) {
        assert.ok(
            call.pageSize <= 100,
            `pageSize=${call.pageSize} dépasse le plafond serveur : la requête serait rejetée en 422`
        );
    }
});

test('getAllPages ramène TOUS les élèves au-delà de la première page', async () => {
    const { fetchStub, calls } = pagedFetch(250);
    const api = bootApi(fetchStub);

    const all = await api.getAllPages('/students');

    assert.equal(all.length, 250, 'aucun élève ne doit être perdu au-delà du centième');
    assert.equal(all[0].id, 'eleve-1');
    assert.equal(all[249].id, 'eleve-250');
    assert.equal(calls.length, 3, '250 lignes = 3 pages de 100');
    assert.deepEqual(calls.map((c) => c.page), [1, 2, 3]);
});

test('getAllPages s\'arrête net quand la collection tient sur une page', async () => {
    const { fetchStub, calls } = pagedFetch(12);
    const api = bootApi(fetchStub);

    const all = await api.getAllPages('/students');

    assert.equal(all.length, 12);
    assert.equal(calls.length, 1, 'une seule requête : inutile d\'en tenter une seconde');
});

test('getAllPages rend une liste vide sans boucler quand il n\'y a aucune ligne', async () => {
    const { fetchStub, calls } = pagedFetch(0);
    const api = bootApi(fetchStub);

    const all = await api.getAllPages('/students');

    // `all` vient du contexte node:vm : on compare sa LONGUEUR, jamais par deepStrictEqual, qui
    // échouerait sur la comparaison des prototypes (cf. le helper `plain` de harness.mjs).
    assert.equal(all.length, 0);
    assert.equal(calls.length, 1);
});

test('getAllPages conserve les filtres déjà présents dans l\'URL', async () => {
    const { fetchStub, calls } = pagedFetch(5);
    const api = bootApi(fetchStub);

    await api.getAllPages('/students?classroomId=abc');

    assert.match(calls[0].url, /classroomId=abc/, 'le filtre ne doit pas être perdu');
    assert.match(calls[0].url, /[?&]page=1/, 'la pagination s\'ajoute avec &, pas avec un second ?');
    assert.equal((calls[0].url.match(/\?/g) || []).length, 1, 'une seule marque de requête');
});

test('getAllPages ne boucle jamais indéfiniment (garde-fou maxPages)', async () => {
    // Serveur incohérent : annonce 10 000 lignes mais n'en sert jamais assez pour atteindre le total.
    const { fetchStub, calls } = pagedFetch(10000);
    const api = bootApi(fetchStub);

    const all = await api.getAllPages('/students', { maxPages: 4 });

    assert.equal(calls.length, 4, 'la borne maxPages doit couper la boucle');
    assert.equal(all.length, 400);
});
