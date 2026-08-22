/**
 * Centre d'aide intégré (wwwroot/js/help.js).
 *
 * Deux garanties comptent ici. La première est STRUCTURELLE : chaque fiche doit porter les six
 * rubriques du squelette pédagogique — une fiche amputée d'« Impacts » ou de « Recommandations »
 * s'afficherait sans erreur, avec un bloc vide, et personne ne s'en apercevrait avant qu'un
 * utilisateur ne cherche la réponse qui manque. La seconde est la RECHERCHE : elle porte sur le
 * texte intégral, y compris celui des tiroirs repliés, et doit ignorer les accents — un secrétaire
 * pressé tape « echeancier », pas « échéancier ».
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const RUBRICS = ['definition', 'objectif', 'probleme', 'procedure', 'impacts', 'recommandations'];

function helpCenter() {
    return loadScripts(['help.js']).component('helpCenter');
}

test('chaque fiche porte les six rubriques du squelette pédagogique, toutes renseignées', () => {
    const help = helpCenter();

    assert.ok(help.sections.length > 0, 'aucun module chargé');

    for (const section of help.sections) {
        assert.ok(section.articles.length > 0, `module vide : ${section.title}`);

        for (const article of section.articles) {
            for (const key of RUBRICS) {
                const value = article[key];
                assert.ok(value !== undefined && value !== null, `${article.id} : rubrique « ${key} » absente`);

                if (Array.isArray(value)) {
                    assert.ok(value.length > 0, `${article.id} : rubrique « ${key} » vide`);
                    value.forEach(item => assert.ok(item.trim().length > 0, `${article.id} : « ${key} » contient une entrée vide`));
                } else {
                    assert.ok(value.trim().length > 0, `${article.id} : rubrique « ${key} » vide`);
                }
            }

            // Le bouton « Ouvrir cet écran » doit mener quelque part : un href absent produirait un
            // lien mort, exactement ce que _Layout.cshtml s'interdit pour le menu latéral.
            assert.match(article.href, /^\//, `${article.id} : href invalide`);
            assert.ok(article.roles.length > 0, `${article.id} : aucun rôle indiqué`);
        }
    }
});

/**
 * Bouton « Haut de page ». Le seul comportement qui mérite un test est celui qui se trompe
 * silencieusement : dans cette application, ce n'est pas la fenêtre qui défile mais <main>
 * (_Layout.cshtml : body en overflow-hidden). Un bouton qui appellerait window.scrollTo() se
 * comporterait normalement à la lecture du code et ne ferait strictement RIEN à l'écran.
 */
function fakeScroller() {
    return {
        scrollTop: 0,
        listeners: [],
        scrolledTo: null,
        removed: false,
        addEventListener(type, fn) { this.listeners.push({ type, fn }); },
        removeEventListener() { this.removed = true; },
        scrollTo(options) { this.scrolledTo = options; }
    };
}

function backToTop(scroller) {
    // Instanciation manuelle : $el doit exister AVANT init(), ce que le raccourci
    // component() du harness ne permet pas.
    const factory = loadScripts(['help.js']).initAlpine().get('backToTop');
    const instance = factory();
    instance.$el = { closest: () => scroller };
    instance.init();
    return instance;
}

test('le bouton « Haut de page » écoute le conteneur défilant, pas la fenêtre', () => {
    const scroller = fakeScroller();
    const bouton = backToTop(scroller);

    assert.equal(scroller.listeners.length, 1, 'aucun écouteur posé sur <main>');
    assert.equal(scroller.listeners[0].type, 'scroll');
    assert.equal(bouton.visible, false, 'le bouton ne doit pas s’afficher en haut de page');
});

test('le bouton n’apparaît qu’une fois le champ de recherche hors de vue', () => {
    const scroller = fakeScroller();
    const bouton = backToTop(scroller);
    const onScroll = scroller.listeners[0].fn;

    scroller.scrollTop = 200;
    onScroll();
    assert.equal(bouton.visible, false, 'apparition trop précoce : on proposerait de remonter là où on est');

    scroller.scrollTop = 900;
    onScroll();
    assert.equal(bouton.visible, true);

    scroller.scrollTop = 0;
    onScroll();
    assert.equal(bouton.visible, false, 'le bouton doit disparaître une fois en haut');
});

test('« Haut de page » ramène bien le conteneur, et le libère à la destruction', () => {
    const scroller = fakeScroller();
    const bouton = backToTop(scroller);

    bouton.toTop();
    assert.deepEqual(plain(scroller.scrolledTo), { top: 0, behavior: 'smooth' });

    bouton.destroy();
    assert.equal(scroller.removed, true, 'écouteur de défilement laissé attaché après destruction');
});

test('chaque module explique son concept, pas seulement ses fiches', () => {
    const help = helpCenter();

    for (const section of help.sections) {
        assert.equal(typeof section.concept, 'string', `module ${section.number} : concept absent`);
        // Un pôle se résume en une ligne (summary) mais s'EXPLIQUE en un paragraphe : le seuil
        // écarte le résumé déguisé en explication, qui laisserait le lecteur au même point.
        assert.ok(section.concept.length >= 400,
            `module ${section.number} : concept trop bref (${section.concept.length} caractères)`);
        assert.ok(section.summary.length < section.concept.length,
            `module ${section.number} : le résumé ne peut pas être plus long que le concept`);
    }
});

test('la recherche atteint le concept des modules, pas seulement les fiches', () => {
    const help = helpCenter();

    // « sens unique » n'apparaît que dans le concept du module Évaluations — nulle part ailleurs.
    help.search = 'sens unique';

    assert.ok(help.resultCount > 0, 'le concept de module est absent de l’index de recherche');
    assert.equal(help.visibleSections()[0].id, 'evaluations');
});

test('chaque fiche expose six blocs prêts à rendre, jamais un objet nu', () => {
    const help = helpCenter();

    for (const section of help.sections) {
        for (const article of section.articles) {
            assert.equal(article.blocks.length, RUBRICS.length, `${article.id} : nombre de blocs inattendu`);

            article.blocks.forEach((block, index) => {
                assert.equal(block.key, RUBRICS[index], `${article.id} : ordre des rubriques modifié`);
                assert.ok(block.label.trim().length > 0, `${article.id}/${block.key} : intitulé vide`);
                assert.ok(['text', 'steps', 'bullets'].includes(block.kind), `${article.id}/${block.key} : kind inconnu`);

                if (block.kind === 'text') {
                    // Le symptôme du bug corrigé : x-text recevait un objet et affichait
                    // « [object Object] ». Un bloc de texte doit être une CHAÎNE, toujours.
                    assert.equal(typeof block.text, 'string', `${article.id}/${block.key} : le texte n'est pas une chaîne`);
                    assert.ok(block.text.trim().length > 0, `${article.id}/${block.key} : texte vide`);
                    assert.ok(!block.text.includes('[object'), `${article.id}/${block.key} : objet sérialisé dans le texte`);
                    assert.equal(block.items.length, 0, `${article.id}/${block.key} : un bloc de texte ne porte pas d'items`);
                } else {
                    assert.ok(Array.isArray(block.items) && block.items.length > 0, `${article.id}/${block.key} : liste vide`);
                    block.items.forEach(item => assert.equal(typeof item, 'string', `${article.id}/${block.key} : entrée non textuelle`));
                    assert.equal(block.text, '', `${article.id}/${block.key} : une liste ne porte pas de texte`);
                }
            });
        }
    }
});

/**
 * Le bug « [object Object] » venait de là, et d'une seule ligne : la vue appelait
 * `valueOf(article, rubric)`. Le proxy de portée d'Alpine résout un identifiant avec
 * `objects.find(o => Reflect.has(o, name))`, et `Reflect.has` remonte la chaîne de prototypes —
 * la portée de boucle d'un x-for « possède » donc valueOf, toString, constructor, hasOwnProperty…
 * par héritage. La méthode du composant n'était jamais atteinte. Ce test l'interdit pour de bon.
 */
test("aucun membre du composant ne porte un nom hérité d'Object.prototype", () => {
    const help = helpCenter();
    const herites = Object.getOwnPropertyNames(Object.prototype);

    // Propriétés PROPRES uniquement : le prototype du composant est Object.prototype lui-même,
    // l'inclure ferait échouer le test sur ses propres membres — ce qu'on cherche, ce sont les
    // noms que le composant DÉFINIT et qui se trouvent aussi sur Object.prototype.
    const collisions = Object.getOwnPropertyNames(help).filter(name => herites.includes(name));

    assert.deepEqual(plain(collisions), [],
        `noms masqués par Object.prototype dans une portée x-for : ${collisions.join(', ')}`);
});

test('les identifiants de fiche sont uniques — ils servent de clé de tiroir', () => {
    const help = helpCenter();
    const ids = help.sections.flatMap(section => section.articles.map(article => article.id));

    assert.equal(new Set(ids).size, ids.length, 'deux fiches partagent le même identifiant');
});

test('la recherche ignore les accents et la casse', () => {
    const help = helpCenter();

    help.search = 'ECHEANCIER';
    const sansAccent = help.resultCount;

    help.search = 'échéancier';
    const avecAccent = help.resultCount;

    assert.ok(sansAccent > 0, 'la recherche sans accent ne trouve rien');
    assert.equal(sansAccent, avecAccent);
});

test('la recherche porte sur le texte intégral, pas seulement sur les titres', () => {
    const help = helpCenter();

    // « carnet à souches » n'apparaît que dans la rubrique « Problème résolu » de l'encaissement.
    help.search = 'carnet à souches';

    assert.equal(help.resultCount, 1);
    assert.equal(help.visibleSections()[0].articles.filter(a => a.haystack.includes('carnet a souches')).length, 1);
});

test('une recherche sans réponse ne laisse aucun module affiché', () => {
    const help = helpCenter();
    help.search = 'zzzz-inexistant';

    assert.equal(help.resultCount, 0);
    assert.equal(help.visibleSections().length, 0);
});

test('un résultat unique s’ouvre de lui-même, plusieurs résultats restent repliés', () => {
    const help = helpCenter();

    help.search = 'carnet à souches';
    help.onSearchInput();
    assert.deepEqual(plain(help.openIds), ['encaissement']);

    help.search = 'bulletin';
    help.onSearchInput();
    assert.ok(help.resultCount > 1, 'le scénario suppose plusieurs réponses');
    assert.deepEqual(plain(help.openIds), [], 'plusieurs réponses : aucune ne doit être présumée');
});

test('mode « un seul tiroir » : ouvrir le suivant referme le précédent', () => {
    const help = helpCenter();

    help.toggle('annee-scolaire');
    help.toggle('infrastructures');

    assert.deepEqual(plain(help.openIds), ['infrastructures']);
    assert.equal(help.isOpen('annee-scolaire'), false);
});

test('mode « dépliage multiple » : les tiroirs s’accumulent, et la bascule inverse n’en garde qu’un', () => {
    const help = helpCenter();
    help.toggleMultiple();

    help.toggle('annee-scolaire');
    help.toggle('infrastructures');
    assert.deepEqual(plain(help.openIds), ['annee-scolaire', 'infrastructures']);

    // Retour au mode épuré : on conserve le DERNIER ouvert, celui que l'utilisateur regardait.
    help.toggleMultiple();
    assert.deepEqual(plain(help.openIds), ['infrastructures']);
});

test('un second clic sur un tiroir ouvert le referme', () => {
    const help = helpCenter();

    help.toggle('saisie-notes');
    assert.equal(help.isOpen('saisie-notes'), true);

    help.toggle('saisie-notes');
    assert.equal(help.isOpen('saisie-notes'), false);
});

test('« Tout déplier » ne déplie que les fiches retenues par la recherche courante', () => {
    const help = helpCenter();

    help.search = 'bulletin';
    help.expandAll();

    assert.equal(help.openIds.length, help.resultCount);
    assert.ok(help.openIds.length < help.totalCount, 'la recherche devait restreindre le périmètre');

    // Effacer la recherche remet la page à plat : ni filtre, ni tiroir ouvert.
    help.clearSearch();
    assert.equal(help.search, '');
    assert.deepEqual(plain(help.openIds), []);
    assert.equal(help.resultCount, help.totalCount);
});
