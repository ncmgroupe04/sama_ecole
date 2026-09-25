/**
 * Écran Matières — matières optionnelles. Le PUT d'une matière est un REMPLACEMENT COMPLET : un champ omis
 * retombe à sa valeur par défaut. Ce fichier verrouille que le drapeau et le groupe repartent à chaque
 * enregistrement, y compris ceux qui ne les concernent pas (réordonner, entêtes).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const ARABE = {
    id: 's-ara', name: 'Arabe', level: 'Collège', coefficient: 2, rowVersion: '1',
    parentSubjectId: null, maxScore: null, displayOrder: 0, isOptional: true, optionGroup: 'LV2'
};

async function view(rows = [ARABE]) {
    const puts = [];
    const posts = [];
    const ctx = loadScripts(['subjects.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async () => rows,
                put: async (url, body) => { puts.push({ url, body }); return {}; },
                post: async (url, body) => { posts.push({ url, body }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/matieres', search: '' },
            history: { replaceState() {} }
        }
    });
    const component = ctx.component('subjectsView');
    await flush();
    return { component, puts, posts };
}

test('un enregistrement qui ne concerne pas les options renvoie quand même le drapeau et le groupe', async () => {
    const { component, puts } = await view();

    await component.saveSubject(ARABE, { displayOrder: 3 });

    assert.equal(puts[0].url, '/subjects/s-ara');
    assert.equal(puts[0].body.isOptional, true);
    assert.equal(puts[0].body.optionGroup, 'LV2');
    assert.equal(puts[0].body.displayOrder, 3);
});

test('l\'édition recopie le drapeau et le groupe de la matière', async () => {
    const { component } = await view();

    component.openEdit(ARABE);

    assert.equal(component.editing.isOptional, true);
    assert.equal(component.editing.optionGroup, 'LV2');
});

test('la création part matière obligatoire, sans groupe', async () => {
    const { component, posts } = await view();

    component.openCreate();
    component.newSubject.name = 'Espagnol';
    component.newSubject.level = 'Collège';
    await component.submitCreate();

    assert.equal(posts[0].body.isOptional, false);
    assert.equal(posts[0].body.optionGroup, null, 'un groupe vide part en null, jamais en chaîne vide');
});

test('décocher « optionnelle » vide le groupe avant l\'envoi', async () => {
    const { component, puts } = await view();

    component.openEdit(ARABE);
    component.editing.isOptional = false;
    await component.submitEdit();

    assert.equal(puts[0].body.isOptional, false);
    assert.equal(puts[0].body.optionGroup, null);
});

test('les groupes d\'options déjà utilisés sont suggérés une fois, sans doublon de casse', async () => {
    const { component } = await view([
        ARABE,
        { ...ARABE, id: 's-esp', name: 'Espagnol', optionGroup: ' lv2 ' },
        { ...ARABE, id: 's-pc', name: 'PC', optionGroup: 'Option scientifique' },
        { ...ARABE, id: 's-maths', name: 'Maths', isOptional: false, optionGroup: 'Ancien' }
    ]);

    assert.deepEqual(plain(component.existingOptionGroups), ['LV2', 'Option scientifique']);
});
