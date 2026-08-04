/**
 * Gestionnaire de sauvegarde locale temporaire (localStorage) pour les brouillons de formulaires.
 * Conforme aux décisions architecturales du Volume 0 §0.8 (Résilience réseau côté client).
 *
 * Source de vérité : toujours le serveur central PostgreSQL. Ce module sert uniquement
 * à conserver les champs saisis par l'utilisateur en cas d'interruption réseau courte
 * ou de fermeture accidentelle d'onglet, puis à les restaurer à la réouverture.
 */
window.formDraft = {
    /**
     * Préfixe portant le SchoolId du tenant courant. Il n'y a pas de RLS dans un localStorage : ce
     * cloisonnement par clé est le SEUL qui existe côté navigateur, et c'est lui qui garantit qu'un
     * compte multi-établissement (school-switcher.js) ne voit jamais reparaître, dans l'école B, un
     * brouillon saisi pour l'école A. Ne jamais construire une clé de brouillon autrement (même
     * principe que ITenantCacheKeyFactory côté serveur — AGENTS.md règle 2).
     */
    prefix() {
        const schoolId = window.auth?.schoolId || 'global';
        return `draft_samaecole_${schoolId}_`;
    },

    getStorageKey(key) {
        return `${this.prefix()}${key}`;
    },

    /**
     * Sauvegarde un objet de données de formulaire en filtrant automatiquement les champs sensibles.
     */
    save(key, data) {
        if (!data || typeof data !== 'object') return;
        try {
            const sanitized = this.sanitize(data);
            localStorage.setItem(this.getStorageKey(key), JSON.stringify({
                savedAt: new Date().toISOString(),
                payload: sanitized
            }));
            this.notifyChange();
        } catch (e) {
            console.warn('Erreur lors de la sauvegarde du brouillon :', e);
        }
    },

    /**
     * Charge un brouillon sauvegardé s'il a moins de 24 heures.
     */
    load(key) {
        try {
            const raw = localStorage.getItem(this.getStorageKey(key));
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            if (!parsed || !parsed.payload) return null;

            // Expiration automatique après 24 heures
            if (parsed.savedAt) {
                const savedTime = new Date(parsed.savedAt).getTime();
                const now = new Date().getTime();
                if (now - savedTime > 24 * 60 * 60 * 1000) {
                    this.clear(key);
                    return null;
                }
            }
            return parsed.payload;
        } catch (e) {
            return null;
        }
    },

    /**
     * Vérifie si un brouillon valide existe.
     */
    has(key) {
        return this.load(key) !== null;
    },

    /**
     * Purge le brouillon après soumission réussie ou abandon volontaire.
     */
    clear(key) {
        try {
            localStorage.removeItem(this.getStorageKey(key));
            this.notifyChange();
        } catch (e) {
            // Ignorer
        }
    },

    /**
     * Inventaire des brouillons NON EXPIRÉS du tenant courant, alimentant le compteur du badge de
     * connectivité (network-guard.js). Les clés sont collectées AVANT d'être chargées : `load()`
     * purge les brouillons de plus de 24 h, et supprimer une entrée en plein parcours indexé de
     * localStorage décale les index restants, sautant silencieusement une entrée sur deux.
     */
    list() {
        const drafts = [];

        try {
            const prefix = this.prefix();
            const storageKeys = [];

            for (let i = 0; i < localStorage.length; i++) {
                const storageKey = localStorage.key(i);
                if (storageKey && storageKey.startsWith(prefix)) {
                    storageKeys.push(storageKey);
                }
            }

            storageKeys.forEach((storageKey) => {
                const key = storageKey.slice(prefix.length);
                let savedAt = null;

                try {
                    savedAt = JSON.parse(localStorage.getItem(storageKey) || '{}').savedAt || null;
                } catch (e) {
                    // Entrée illisible : load() ci-dessous renverra null, elle sera ignorée.
                }

                // load() applique l'expiration à 24 h et purge au passage.
                if (this.load(key) !== null) {
                    drafts.push({ key, savedAt });
                }
            });
        } catch (e) {
            return drafts;
        }

        return drafts;
    },

    /** Nombre de brouillons conservés sur ce poste pour le tenant courant. */
    count() {
        return this.list().length;
    },

    /**
     * Signale un changement d'inventaire pour que le badge de la barre supérieure se remette à jour
     * sans interrogation périodique. `storage` (l'événement natif) ne convient pas : il ne se
     * déclenche QUE dans les autres onglets, jamais dans celui qui écrit.
     */
    notifyChange() {
        try {
            window.dispatchEvent(new CustomEvent('samaecole:drafts-changed'));
        } catch (e) {
            // Ignorer
        }
    },

    /**
     * Supprime de manière récursive les champs sensibles qui ne doivent jamais transiter
     * ou être stockés en clair (mots de passe, codes secrets, numéros de cartes/tokens).
     */
    sanitize(obj) {
        if (!obj || typeof obj !== 'object') return obj;
        if (Array.isArray(obj)) return obj.map((item) => this.sanitize(item));

        const excludedKeys = ['password', 'confirmpassword', 'token', 'secret', 'pin', 'cvv', 'cardnumber', 'photodata'];
        const copy = {};
        for (const [k, v] of Object.entries(obj)) {
            if (excludedKeys.some((ex) => k.toLowerCase().includes(ex))) {
                continue;
            }
            if (v && typeof v === 'object') {
                copy[k] = this.sanitize(v);
            } else {
                copy[k] = v;
            }
        }
        return copy;
    }
};

/**
 * Composant Alpine réutilisable pour afficher le bandeau d'alerte et gérer le brouillon.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('formDraftAlert', (draftKey, onRestoreCallback, onClearCallback) => ({
        hasDraft: false,

        init() {
            this.checkDraft();
            window.addEventListener('focus', () => this.checkDraft());
        },

        checkDraft() {
            this.hasDraft = window.formDraft && window.formDraft.has(draftKey);
        },

        restore() {
            if (!window.formDraft) return;
            const payload = window.formDraft.load(draftKey);
            if (payload && typeof onRestoreCallback === 'function') {
                onRestoreCallback(payload);
            }
            this.hasDraft = false;
        },

        discard() {
            if (!window.formDraft) return;
            window.formDraft.clear(draftKey);
            this.hasDraft = false;
            if (typeof onClearCallback === 'function') {
                onClearCallback();
            }
        }
    }));
});
