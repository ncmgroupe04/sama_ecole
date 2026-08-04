/**
 * Guard Réseau, sonde de joignabilité et enregistrement du Service Worker (PWA & Résilience).
 * Conforme au Volume 0 §0.8, à la décision D-01 (« plus d'Offline-First ») et aux règles AGENTS.md :
 * aucune base locale, aucune file de mutations, aucune écriture métier hors du serveur.
 *
 * Ce module est la SOURCE UNIQUE de l'état de connectivité côté navigateur. Il expose trois états —
 * et non un simple booléen — parce que `navigator.onLine` ne répond pas à la question qui intéresse
 * un utilisateur en train de saisir un paiement :
 *
 *   'online'   : le serveur a répondu (ou le navigateur n'a jamais signalé de coupure).
 *   'offline'  : le navigateur a signalé une coupure, ou la sonde n'a pas obtenu de réponse.
 *   'checking' : une sonde est en cours — état transitoire, affiché pour ne pas laisser croire à
 *                une coupure pendant les quelques centaines de millisecondes de la vérification.
 *
 * `navigator.onLine` ne prouve QUE l'activité de l'interface réseau : il reste `true` derrière un
 * portail captif d'hôtel ou une 4G qui ne route plus rien. D'où la sonde HEAD sur un asset statique
 * — pas d'authentification, pas de JWT, pas de RLS en jeu, donc utilisable même session expirée.
 */
const NETWORK_PROBE_URL = '/manifest.json';
const NETWORK_PROBE_TIMEOUT_MS = 5000;

window.networkGuard = {
    /** 'online' | 'offline' | 'checking' */
    state: navigator.onLine ? 'online' : 'offline',
    lastProbeAt: null,
    listeners: [],
    probeInFlight: null,

    /** Conservé pour les appelants historiques qui testent un booléen. */
    get isOnline() {
        return this.state === 'online';
    },

    init() {
        // Le navigateur annonce le retour du réseau : on ne le croit pas sur parole, on vérifie que
        // le serveur répond vraiment avant de repasser au vert.
        window.addEventListener('online', () => { this.check(); });

        // Une coupure annoncée, en revanche, est fiable : inutile de sonder pour la confirmer.
        window.addEventListener('offline', () => { this.setState('offline'); });

        this.registerServiceWorker();
    },

    onChange(cb) {
        if (typeof cb === 'function') {
            this.listeners.push(cb);
        }
    },

    setState(next) {
        if (this.state === next) return;
        this.state = next;
        this.notifyListeners();
    },

    notifyListeners() {
        const online = this.isOnline;
        this.listeners.forEach((cb) => {
            try { cb(online, this.state); } catch (e) { console.error(e); }
        });
    },

    /**
     * Vérifie que le serveur répond réellement. Utilisé par le bouton « Vérifier la connexion » et
     * au retour d'un événement `online`. Les appels concurrents partagent la même sonde : marteler
     * le bouton ne déclenche pas dix requêtes.
     */
    async check() {
        if (this.probeInFlight) return this.probeInFlight;

        if (!navigator.onLine) {
            this.setState('offline');
            return false;
        }

        this.setState('checking');

        this.probeInFlight = this.probe()
            .then((reachable) => {
                this.lastProbeAt = new Date();
                this.setState(reachable ? 'online' : 'offline');
                return reachable;
            })
            .finally(() => { this.probeInFlight = null; });

        return this.probeInFlight;
    },

    /**
     * HEAD sur un asset statique, hors cache et hors Service Worker (sw.js n'intercepte ni les
     * requêtes non-GET ni ce chemin). Le paramètre `_probe` déjoue les caches intermédiaires des
     * opérateurs, fréquents sur les réseaux mobiles sénégalais et capables de renvoyer un 200 alors
     * que la liaison est morte.
     */
    async probe() {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), NETWORK_PROBE_TIMEOUT_MS);

        try {
            const response = await fetch(`${NETWORK_PROBE_URL}?_probe=${Date.now()}`, {
                method: 'HEAD',
                cache: 'no-store',
                credentials: 'same-origin',
                signal: controller.signal
            });
            return response.ok;
        } catch (e) {
            return false;
        } finally {
            clearTimeout(timer);
        }
    },

    registerServiceWorker() {
        if ('serviceWorker' in navigator && (window.location.protocol === 'https:' || window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')) {
            window.addEventListener('load', () => {
                navigator.serviceWorker.register('/sw.js', { scope: '/' })
                    .then((reg) => {
                        console.debug('SamaEcole ServiceWorker enregistré avec succès. Scope :', reg.scope);
                    })
                    .catch((err) => {
                        console.warn('Échec de l\'enregistrement du ServiceWorker :', err);
                    });
            });
        }
    }
};

// Initialisation immédiate du Guard Réseau
window.networkGuard.init();

document.addEventListener('alpine:init', () => {
    /**
     * Bandeau d'alerte plein cadre, affiché uniquement sur une coupure CONFIRMÉE ('offline').
     * Volontairement muet pendant 'checking' : un bandeau rouge qui clignote à chaque vérification
     * de routine finit par être ignoré.
     */
    Alpine.data('networkGuardBanner', () => ({
        state: window.networkGuard.state,
        showReconnectedToast: false,

        init() {
            // La reprise se lit sur le dernier état CONFIRMÉ, pas sur l'état précédent brut : le
            // retour du réseau passe par offline → checking → online, et comparer à 'checking'
            // ferait manquer la transition à chaque fois, donc le toast ne s'afficherait jamais.
            let lastConfirmed = this.state === 'checking' ? 'online' : this.state;

            window.networkGuard.onChange((online, state) => {
                this.state = state;
                if (state === 'checking') return;

                if (online && lastConfirmed === 'offline') {
                    this.showReconnectedToast = true;
                    setTimeout(() => { this.showReconnectedToast = false; }, 5000);
                }

                lastConfirmed = state;
            });
        },

        get offline() { return this.state === 'offline'; }
    }));

    /**
     * Badge de connectivité de la barre supérieure.
     *
     * Le compteur affiché est un nombre de BROUILLONS DE FORMULAIRE conservés sur ce poste
     * (localStorage, voir form-draft.js) — pas une file de mutations en attente d'envoi : il n'en
     * existe pas, et il n'en existera pas (D-01/D-09). La nuance est tout l'intérêt du badge : elle
     * dit à la secrétaire « ta saisie n'est pas perdue » sans lui laisser croire « c'est enregistré ».
     */
    Alpine.data('connectivityBadge', () => ({
        state: window.networkGuard.state,
        draftCount: 0,

        init() {
            window.networkGuard.onChange((online, state) => {
                this.state = state;
                this.refreshDrafts();
            });

            this.refreshDrafts();
            window.addEventListener('samaecole:drafts-changed', () => this.refreshDrafts());
            window.addEventListener('focus', () => this.refreshDrafts());
        },

        refreshDrafts() {
            this.draftCount = window.formDraft ? window.formDraft.count() : 0;
        },

        label() {
            if (this.state === 'checking') return 'Vérification…';
            if (this.state === 'offline') {
                return this.draftCount > 0
                    ? `Hors ligne — ${this.draftCount} saisie${this.draftCount > 1 ? 's' : ''} conservée${this.draftCount > 1 ? 's' : ''}`
                    : 'Hors ligne';
            }
            return this.draftCount > 0 ? `Connecté — ${this.draftCount} brouillon${this.draftCount > 1 ? 's' : ''}` : 'Connecté';
        },

        title() {
            if (this.state === 'checking') return 'Vérification de la liaison avec le serveur…';
            if (this.state === 'offline') {
                return this.draftCount > 0
                    ? `Aucune liaison avec le serveur. ${this.draftCount} saisie(s) en cours conservée(s) sur ce poste : rien n'est enregistré tant que la connexion n'est pas rétablie.`
                    : "Aucune liaison avec le serveur. Les enregistrements sont impossibles tant que la connexion n'est pas rétablie.";
            }
            return this.draftCount > 0
                ? `Connecté au serveur. ${this.draftCount} brouillon(s) de formulaire non soumis sur ce poste.`
                : 'Connecté au serveur.';
        },

        badgeClass() {
            if (this.state === 'checking') return 'bg-primary-50 text-primary-700';
            if (this.state === 'offline') return 'bg-warning-bg text-warning';
            return 'bg-success-bg text-success';
        },

        dotClass() {
            if (this.state === 'checking') return 'bg-primary animate-pulse';
            if (this.state === 'offline') return 'bg-warning';
            return 'bg-success';
        },

        checking() { return this.state === 'checking'; },

        recheck() { window.networkGuard.check(); }
    }));
});
