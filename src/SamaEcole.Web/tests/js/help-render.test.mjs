/**
 * Rendu du Centre d'aide : vérifie que CHAQUE expression x-text de Views/Help/Index.cshtml produit
 * bien du texte, pour chaque fiche et chaque rubrique du guide.
 *
 * Pourquoi un test aussi inhabituel : le bug « [object Object] » n'était pas dans les données — le
 * contenu était juste, et help.test.mjs l'aurait confirmé — mais dans la RÉSOLUTION D'IDENTIFIANT
 * entre le gabarit et le composant. La vue appelait `valueOf(article, rubric)` ; Alpine résout un
 * identifiant avec `objects.find(o => Reflect.has(o, name))`, et `Reflect.has` remonte la chaîne de
 * prototypes. Dans un x-for, la portée de boucle « possède » donc valueOf par héritage
 * d'Object.prototype : c'était Object.prototype.valueOf qui répondait, laquelle renvoie l'objet
 * lui-même. Aucun test portant sur les seules données n'aurait pu voir cela.
 *
 * On reproduit donc ici les DEUX pièges d'Alpine (les trappes `has` et `get` de mergeProxies, et
 * l'ordre de pile « portée la plus intérieure d'abord »), puis on évalue les expressions extraites
 * de la vue elle-même — pas une copie recopiée à la main, qui dériverait au premier remaniement.
 *
 * Sans navigateur ni jsdom : c'est la sémantique de résolution qui compte, pas le moteur de rendu.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { loadScripts } from './harness.mjs';

const VIEW = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'Views', 'Help', 'Index.cshtml');

/**
 * Reproduction fidèle de mergeProxies() d'Alpine (wwwroot/js/vendor/alpine.min.js) :
 *
 *     has({objects}, name)      { return objects.some(o => hasOwnProperty(o, name) || Reflect.has(o, name)) }
 *     get({objects}, name, rcv) { return Reflect.get(objects.find(o => Reflect.has(o, name)) || {}, name, rcv) }
 *
 * `objects` va de la portée la plus INTÉRIEURE à la plus extérieure : une portée de boucle x-for
 * masque donc le composant pour tout nom qu'elle « possède », héritage compris.
 */
function alpineScope(objects) {
    return new Proxy({ objects }, {
        ownKeys: ({ objects: o }) => Array.from(new Set(o.flatMap(x => Object.keys(x)))),
        has: ({ objects: o }, name) =>
            name === Symbol.unscopables ? false : o.some(x => Reflect.has(x, name)),
        get: ({ objects: o }, name, receiver) =>
            Reflect.get(o.find(x => Reflect.has(x, name)) || {}, name, receiver)
    });
}

function evaluate(expression, scope) {
    // Exactement la forme utilisée par Alpine pour évaluer une expression de gabarit.
    return new Function('scope', `with (scope) { return (${expression}) }`)(scope);
}

/** Toutes les expressions x-text du gabarit, dans leur ordre d'apparition. */
function xTextExpressions() {
    const source = readFileSync(VIEW, 'utf8');
    return [...source.matchAll(/x-text="([^"]+)"/g)].map(match => match[1]);
}

test('la vue expose bien un jeu d’expressions x-text à vérifier', () => {
    const expressions = xTextExpressions();

    assert.ok(expressions.length >= 10,
        `extraction suspecte : ${expressions.length} expression(s) trouvée(s) dans Views/Help/Index.cshtml`);
});

test('chaque expression x-text rend du texte, pour chaque fiche et chaque rubrique', () => {
    const help = loadScripts(['help.js']).component('helpCenter');
    const expressions = xTextExpressions();
    let evaluations = 0;

    for (const section of help.sections) {
        for (const article of section.articles) {
            for (const block of article.blocks) {
                // Pile de portées telle qu'Alpine la construit : boucles imbriquées d'abord,
                // composant en dernier. `step`, `item`, `index` et `role` vivent dans les boucles
                // les plus intérieures du gabarit.
                const scope = alpineScope([
                    { step: block.items[0] ?? '', item: block.items[0] ?? '', index: 0, role: article.roles[0] },
                    { block },
                    { article },
                    { section },
                    help
                ]);

                for (const expression of expressions) {
                    const value = evaluate(expression, scope);
                    evaluations++;

                    assert.ok(['string', 'number'].includes(typeof value),
                        `${article.id}/${block.key} — « ${expression} » rend un ${typeof value}, pas du texte`);
                    assert.ok(!String(value).includes('[object'),
                        `${article.id}/${block.key} — « ${expression} » rend « ${value} »`);
                }
            }
        }
    }

    assert.ok(evaluations > 500, `couverture insuffisante : ${evaluations} évaluations`);
});

test('un nom hérité d’Object.prototype serait bien masqué — le piège est réel, pas théorique', () => {
    const help = loadScripts(['help.js']).component('helpCenter');
    const article = help.sections[0].articles[0];

    // Reconstitution de l'ancien gabarit : le composant expose ici une méthode `valueOf`, et
    // pourtant c'est Object.prototype.valueOf que la portée de boucle fournit. Ce test échouerait
    // si Alpine changeait de sémantique — auquel cas la précaution prise dans la vue (ne plus
    // appeler de méthode du tout) resterait correcte, mais son commentaire serait à revoir.
    const piege = alpineScope([{ article }, { ...help, valueOf: () => 'texte du composant' }]);
    const rendu = String(evaluate('valueOf(article)', piege));

    assert.ok(rendu.includes('[object'),
        "Alpine ne masque plus les noms hérités : revoir le commentaire de blocksOf() dans help.js");
});
