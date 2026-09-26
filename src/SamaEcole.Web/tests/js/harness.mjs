/**
 * Bac à sable minimal pour tester les scripts de wwwroot/js sous `node --test`.
 *
 * Ces fichiers sont des scripts CLASSIQUES qui s'attachent à `window` (pas des modules ES) : ils ne
 * peuvent pas être importés. On les évalue donc dans un contexte `node:vm` où l'on injecte des
 * doublures de `window`, `document`, `navigator`, `localStorage` et `fetch`, ce qui permet de piloter
 * exactement les scénarios qui comptent — coupure réseau en plein envoi, serveur injoignable alors
 * que l'interface réseau est active, brouillon expiré — sans navigateur ni dépendance npm.
 *
 * Volontairement sans Alpine : les composants déclarés via Alpine.data() sont de simples fabriques
 * d'objets, qu'on instancie directement. On teste leur logique, pas le moteur de réactivité.
 */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import vm from 'node:vm';

const JS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'wwwroot', 'js');

function createLocalStorage() {
    const store = new Map();
    return {
        get length() { return store.size; },
        key(index) { return Array.from(store.keys())[index] ?? null; },
        getItem(key) { return store.has(key) ? store.get(key) : null; },
        setItem(key, value) { store.set(String(key), String(value)); },
        removeItem(key) { store.delete(String(key)); },
        clear() { store.clear(); }
    };
}

function createEventTarget() {
    const listeners = new Map();
    return {
        addEventListener(type, cb) {
            if (!listeners.has(type)) listeners.set(type, []);
            listeners.get(type).push(cb);
        },
        dispatchEvent(event) {
            (listeners.get(event.type) || []).forEach((cb) => cb(event));
            return true;
        },
        /** Déclenche un événement par son nom, pour les tests. */
        emit(type, detail) {
            this.dispatchEvent({ type, detail });
        }
    };
}

/**
 * Charge un ou plusieurs scripts de wwwroot/js dans un contexte isolé.
 *
 * @param {string[]} files      noms de fichiers, dans l'ordre de chargement du _Layout
 * @param {object}   options
 * @param {boolean}  options.onLine   valeur initiale de navigator.onLine
 * @param {Function} options.fetch    doublure de fetch
 * @param {object}   options.preload  propriétés posées sur `window` AVANT l'évaluation (ex. auth)
 * @param {object}   options.document propriétés posées sur `document` AVANT l'évaluation (ex. querySelectorAll)
 */
export function loadScripts(files, options = {}) {
    const windowTarget = createEventTarget();
    // options.document : propriétés posées sur `document` AVANT l'évaluation, pour les scripts qui lisent le DOM au
    // chargement (ex. marketing.js : querySelectorAll, readyState).
    const documentTarget = Object.assign(createEventTarget(), options.document || {});
    const alpineComponents = new Map();
    const alpineStores = new Map();

    const win = Object.assign(windowTarget, {
        location: { protocol: 'https:', hostname: 'localhost', pathname: '/' },
        ...(options.preload || {})
    });

    const sandbox = {
        window: win,
        document: documentTarget,
        navigator: { onLine: options.onLine !== false },
        localStorage: createLocalStorage(),
        fetch: options.fetch || (async () => ({ ok: true, status: 200, json: async () => ({}) })),
        // Alpine.data() enregistre une fabrique de composant ; Alpine.store() lit (1 arg) ou
        // déclare (2 args) un store global — plusieurs scripts de _Layout en déclarent depuis leur
        // écouteur alpine:init (accessDenied, changePasswordModal, guide…).
        Alpine: {
            data: (name, factory) => alpineComponents.set(name, factory),
            store: (name, value) => {
                if (value === undefined) return alpineStores.get(name);
                alpineStores.set(name, value);
                return value;
            }
        },
        console: { log() {}, warn() {}, error() {}, debug() {} },
        setTimeout,
        clearTimeout,
        CustomEvent,
        AbortController,
        FormData,
        URLSearchParams,
        // `new URL(window.location.href)` : la façon dont les écrans synchronisent l'onglet ouvert
        // avec l'URL (history.replaceState — settings.js, subjects.js).
        URL,
        // auth.js décode les claims d'un JWT (readClaims) avec atob()/TextDecoder — nécessaires
        // uniquement aux tests qui posent une vraie session via window.auth.saveSession() puis
        // lisent window.auth.role/canView(...). Repris tels quels des globals Node (WHATWG), sans
        // doublure : ce ne sont pas des API réseau/horloge à maîtriser pour les scénarios réseau.
        atob,
        TextDecoder
    };

    sandbox.globalThis = sandbox;
    vm.createContext(sandbox);

    files.forEach((file) => {
        const fullPath = path.join(JS_DIR, file);
        vm.runInContext(readFileSync(fullPath, 'utf8'), sandbox, { filename: fullPath });
    });

    return {
        sandbox,
        window: win,
        navigator: sandbox.navigator,
        localStorage: sandbox.localStorage,

        /** Rejoue l'événement `alpine:init` pour enregistrer les composants Alpine.data(). */
        initAlpine() {
            documentTarget.emit('alpine:init');
            return alpineComponents;
        },

        /** Store Alpine global déclaré par un des scripts chargés (après initAlpine()). */
        store(name) {
            if (alpineStores.size === 0) this.initAlpine();
            return alpineStores.get(name);
        },

        /** Instancie un composant Alpine et exécute son init(). */
        component(name) {
            if (alpineComponents.size === 0) this.initAlpine();
            const instance = alpineComponents.get(name)();
            if (typeof instance.init === 'function') instance.init();
            return instance;
        }
    };
}

/** Nombre de tours de `setTimeout(0)` drainés après l'attente : profondeur max des chaînes attendues. */
const ZERO_DELAY_ROUNDS = 5;

/**
 * Laisse les promesses en attente se résoudre (les reprises utilisent setTimeout).
 *
 * L'attente seule ne suffit pas : un code qui enchaîne des `setTimeout(0)` imbriqués (ex.
 * school-mode-guard.js › present(), qui ouvre la modale au tick SUIVANT la fermeture des autres)
 * rendait les tests instables sous charge. Si la boucle d'événements est bloquée plus de `ms`, le
 * premier timer et celui de l'attente expirent dans la même phase : le premier programme le second,
 * puis l'attente se résout AVANT qu'il ait tourné. Les timers de même délai s'exécutant dans l'ordre
 * où ils ont été posés, chaque tour de `setTimeout(0)` ci-dessous passe après ceux posés par le tour
 * précédent : la chaîne est drainée quelle que soit la charge de la machine.
 */
export async function flush(ms = 20) {
    await new Promise((resolve) => setTimeout(resolve, ms));
    for (let i = 0; i < ZERO_DELAY_ROUNDS; i++) {
        await new Promise((resolve) => setTimeout(resolve, 0));
    }
}

/**
 * Ramène un objet issu du contexte vm dans le realm du test. `node:vm` donne au bac à sable ses
 * propres constructeurs : un objet créé là-bas n'a pas le même `Object.prototype` qu'ici, et
 * `assert.deepStrictEqual` échoue sur la comparaison de prototypes alors que les données sont
 * identiques — d'où le passage par JSON.
 */
export function plain(value) {
    return JSON.parse(JSON.stringify(value));
}
