/**
 * Gestionnaire de sauvegarde locale temporaire (localStorage) pour les brouillons de formulaires.
 * Conforme aux décisions architecturales du Volume 0 §0.8 (Résilience réseau côté client).
 *
 * Source de vérité : toujours le serveur central PostgreSQL. Ce module sert uniquement
 * à conserver les champs saisis par l'utilisateur en cas d'interruption réseau courte
 * ou de fermeture accidentelle d'onglet, puis à les restaurer à la réouverture.
 */
window.formDraft = {
    getStorageKey(key) {
        const schoolId = window.auth?.schoolId || 'global';
        return `draft_samaecole_${schoolId}_${key}`;
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
