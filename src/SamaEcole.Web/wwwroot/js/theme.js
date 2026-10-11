/**
 * Sélecteur de thème — store Alpine « theme » (clair / sombre / système), monté une seule fois par _Layout.
 *
 * theme-boot.js pose déjà la classe `.dark` avant le premier rendu (anti-flash) ; ce store gère ensuite les
 * changements faits par l'utilisateur et, en mode « système », suit le thème du système en direct.
 * La préférence est enregistrée dans le navigateur (localStorage « sama-theme »), partagée entre les onglets.
 * Script classique chargé avant Alpine (différé) : il enregistre un écouteur alpine:init.
 */
document.addEventListener('alpine:init', () => {
    const KEY = 'sama-theme';
    const DEFAULT = 'light'; // à garder identique à theme-boot.js
    const MODES = ['light', 'dark', 'system'];
    const media = window.matchMedia('(prefers-color-scheme: dark)');

    const read = () => {
        try {
            const value = localStorage.getItem(KEY);
            return MODES.includes(value) ? value : DEFAULT;
        } catch (_) {
            return DEFAULT;
        }
    };

    Alpine.store('theme', {
        mode: read(),
        modes: [
            { value: 'light', label: 'Clair' },
            { value: 'dark', label: 'Sombre' },
            { value: 'system', label: 'Système' }
        ],

        get isDark() {
            return this.mode === 'dark' || (this.mode === 'system' && media.matches);
        },

        apply() {
            document.documentElement.classList.toggle('dark', this.isDark);
        },

        set(mode) {
            if (!MODES.includes(mode)) return;
            this.mode = mode;
            try {
                localStorage.setItem(KEY, mode);
            } catch (_) {
                // Non persisté (mode privé strict) : appliqué pour la session seulement.
            }
            this.apply();
        }
    });

    const store = Alpine.store('theme');
    store.apply();
    // « Système » : suit le thème du système en direct. Autres modes : sans effet (apply relit this.mode).
    media.addEventListener('change', () => store.apply());
    // Un autre onglet a changé la préférence : on s'aligne.
    window.addEventListener('storage', (event) => {
        if (event.key !== KEY) return;
        store.mode = read();
        store.apply();
    });
});
