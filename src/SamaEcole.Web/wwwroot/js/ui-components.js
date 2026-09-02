/*
 * Composants Alpine transverses (Volume 5 §7), chargés une fois par _Layout.cshtml — au même titre
 * que sidebarNav/sessionMenu (auth.js) — pour rester disponibles sur tout écran hôte d'un
 * <date-field> (TagHelpers/DateFieldTagHelper.cs).
 */

/** Fermeture globale et systématique de toutes les modales actives (modal-shell) */
window.closeAllModals = function() {
    window.dispatchEvent(new CustomEvent('close-modals'));
};

/**
 * Placement intelligent (auto-flip) d'un menu flottant. Ouvre vers le BAS par défaut ; bascule vers
 * le HAUT uniquement quand la place manque réellement dessous ET qu'il y en a davantage dessus.
 * Partagé par selectField() (listes déroulantes — filtres Mois/Années, onglets…) et dateField()
 * (calendrier) : un menu ouvert près du pli n'est plus tronqué ni caché sous la fenêtre.
 *
 * La place disponible est mesurée contre le plus proche CONTENEUR QUI ROGNE (overflow auto / scroll
 * / hidden) — typiquement le corps défilant d'une modale — et non la seule fenêtre : un popover
 * `absolute` enfermé dans une modale est coupé par ce conteneur bien avant d'atteindre le bord de
 * l'écran. C'était la cause des calendriers tronqués dans « Nouveau prêt » / « Nouveau mouvement ».
 *
 * @param {Element} anchorEl Élément racine du composant (le déclencheur mesuré).
 * @param {number} estimatedMenuHeight Hauteur approximative du menu, en pixels.
 * @returns {'top'|'bottom'}
 */
window.computeFlipPlacement = function (anchorEl, estimatedMenuHeight) {
    if (!anchorEl || typeof anchorEl.getBoundingClientRect !== 'function') return 'bottom';
    const rect = anchorEl.getBoundingClientRect();
    const needed = estimatedMenuHeight || 288;

    // Bornes de la zone visible : la fenêtre, resserrée par chaque ancêtre qui rogne le débordement
    // (le corps d'une modale, un panneau scrollable…). L'intersection de tous donne la zone où le
    // popover restera réellement visible.
    let clipTop = 0;
    let clipBottom = window.innerHeight || document.documentElement.clientHeight || 0;

    for (let el = anchorEl.parentElement; el && el !== document.body && el !== document.documentElement; el = el.parentElement) {
        const overflowY = window.getComputedStyle(el).overflowY;
        if (overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'hidden') {
            const r = el.getBoundingClientRect();
            clipTop = Math.max(clipTop, r.top);
            clipBottom = Math.min(clipBottom, r.bottom);
        }
    }

    const spaceBelow = clipBottom - rect.bottom;
    const spaceAbove = rect.top - clipTop;
    return (spaceBelow < needed && spaceAbove > spaceBelow) ? 'top' : 'bottom';
};

/**
 * Notifications éphémères (succès/erreur), référencées comme `toast.success(...)`/`toast.error(...)`
 * depuis discipline.js/billets.js/payroll.js — jusqu'ici jamais défini nulle part : chaque appel
 * levait une ReferenceError silencieuse, interrompant le script AVANT la fermeture du slide-over ou
 * le rafraîchissement de la liste (ex. discipline.js submitCreate), ce qui donnait l'impression d'un
 * enregistrement resté sans effet alors que l'appel API avait réussi.
 */
window.toast = (function () {
    function ensureContainer() {
        let el = document.getElementById('app-toast-container');
        if (!el) {
            el = document.createElement('div');
            el.id = 'app-toast-container';
            el.className = 'fixed top-4 right-4 z-[100] flex flex-col gap-2 items-end pointer-events-none';
            document.body.appendChild(el);
        }
        return el;
    }

    function show(message, variant) {
        // Message vide : rien à annoncer. Cas concret — window.api.toMessage() renvoie '' pour une
        // erreur déjà portée par la modale universelle « Accès refusé » (voir api.js) ; sans ce garde,
        // un rectangle rouge vide s'afficherait par-dessus.
        if (message === null || message === undefined || String(message).trim() === '') return;

        const container = ensureContainer();
        const el = document.createElement('div');
        const styles = variant === 'error'
            ? 'bg-danger-bg text-danger border-danger/30'
            : 'bg-success-bg text-success border-success/30';
        el.className = `pointer-events-auto max-w-sm w-full px-4 py-3 rounded-xl border shadow-lg text-sm font-medium ${styles} transition-opacity duration-200`;
        el.textContent = message;
        container.appendChild(el);
        setTimeout(() => {
            el.style.opacity = '0';
            setTimeout(() => el.remove(), 200);
        }, 4000);
    }

    return {
        success(message) { show(message, 'success'); },
        error(message) { show(message, 'error'); }
    };
})();

document.addEventListener('alpine:init', () => {
    /**
     * Navigation mois/année du calendrier maison. La VALEUR sélectionnée (lecture/écriture) est
     * portée directement par l'expression Alpine du parent (voir DateFieldTagHelper) : ce composant
     * ne gère que l'affichage de la grille du mois courant.
     *
     * x-data ne reçoit JAMAIS la valeur du modèle en argument (ex. « editingStudent.birthDate ») :
     * tant que le formulaire hôte n'a pas encore de donnée (« editingStudent » vaut null avant
     * l'ouverture d'une fiche), cette lecture lèverait une exception AU MOMENT de l'évaluation de
     * l'argument — avant même l'appel de cette factory — et Alpine abandonnerait tout le composant
     * (aucune propriété réactive créée). toggle() relit la valeur en toute sécurité, seulement au
     * clic, quand elle est garantie déjà renseignée.
     */
    Alpine.data('dateField', (initialIso) => ({
        open: false,
        // Placement vertical du popover, recalculé à chaque ouverture (auto-flip haut/bas).
        placement: 'bottom',
        viewYear: null,
        viewMonth: null,
        weekdayLabels: ['Lu', 'Ma', 'Me', 'Je', 'Ve', 'Sa', 'Di'],

        /**
         * Bornes de la navigation, IDENTIQUES à la plage d'<option> rendue par
         * TagHelpers/DateFieldTagHelper.cs (`Enumerable.Range(currentYear - 100, 106)`).
         *
         * Elles ne sont pas cosmétiques : si prevMonth()/nextMonth() emmène `viewYear` hors de cette
         * plage, le <select x-model.number="viewYear"> n'a plus aucune <option> correspondante. Le
         * navigateur retombe alors sur sa PREMIÈRE option et x-model réécrit `viewYear` avec — le
         * calendrier saute brutalement d'un siècle sous les doigts de l'utilisateur. Le test
         * date-field-taghelper-sync.test.mjs échoue si ces deux constantes cessent de correspondre
         * au Tag Helper.
         */
        yearsBack: 100,
        yearsForward: 5,

        get minYear() { return new Date().getFullYear() - this.yearsBack; },
        get maxYear() { return new Date().getFullYear() + this.yearsForward; },

        // Saisie clavier (hybride avec le calendrier) : `text` est le tampon affiché/tapé
        // (JJ/MM/AAAA), distinct de Model — Model ne reçoit une écriture que lorsque `text` forme
        // une date complète et valide (voir onTextInput). `pendingIso` porte le résultat du dernier
        // parsing pour que le x-on:input émis par le Tag Helper (qui ne connaît pas cette fonction,
        // seulement Model) sache s'il doit écrire dans Model ou laisser sa valeur inchangée.
        text: '',
        pendingIso: null,

        init() {
            this.setView(initialIso);
        },

        /**
         * Ramène n'importe quelle valeur de modèle à `yyyy-MM-dd`, ou '' si elle n'en contient pas.
         *
         * Indispensable parce que le modèle lié n'est PAS toujours une DateOnly : une propriété
         * DateTimeOffset de l'API arrive horodatée (« 2024-03-07T00:00:00+00:00 »), et un formulaire
         * encore vide porte null. Sans cette normalisation, `split('-')` rendait « 07T00:00:00+00:00 »
         * comme jour, et `formatInput(null)` levait un TypeError qui cassait toute l'expression Alpine.
         */
        normalizeIso(value) {
            if (typeof value !== 'string') return '';
            const match = /^(\d{4}-\d{2}-\d{2})/.exec(value.trim());
            return match ? match[1] : '';
        },

        /** Borne l'année dans la plage réellement offerte par le <select> (voir yearsBack/yearsForward). */
        clampYear(year) {
            return Math.min(this.maxYear, Math.max(this.minYear, year));
        },

        setView(iso) {
            const normalized = this.normalizeIso(iso);
            const base = normalized ? new Date(normalized + 'T00:00:00') : new Date();
            this.viewYear = this.clampYear(base.getFullYear());
            this.viewMonth = base.getMonth();
        },

        /** Rouvre toujours sur le mois de la valeur actuelle — un panneau réutilisé (modal-shell,
         *  x-show) garde sinon la navigation du dernier enregistrement affiché. */
        toggle(currentIso) {
            if (this.open) {
                this.open = false;
                return;
            }
            this.setView(currentIso);
            this.placement = window.computeFlipPlacement(this.$root, 380);
            this.open = true;
            // Repli : si, malgré le flip, le calendrier dépasse encore de son conteneur (modale très
            // courte), on l'y ramène par un défilement minimal — jamais un saut de toute la page.
            this.$nextTick(() =>
                this.$refs.calendarPopover?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }));
        },

        /** Classes de position du popover selon le placement calculé (auto-flip). */
        get menuPlacementClass() {
            return this.placement === 'top' ? 'bottom-full mb-2' : 'top-full mt-2';
        },

        get days() {
            const firstOfMonth = new Date(this.viewYear, this.viewMonth, 1);
            const leading = (firstOfMonth.getDay() + 6) % 7; // Lundi en première colonne
            const start = new Date(this.viewYear, this.viewMonth, 1 - leading);

            return Array.from({ length: 42 }, (_, i) => {
                const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
                return {
                    iso: this.toIso(d),
                    label: d.getDate(),
                    currentMonth: d.getMonth() === this.viewMonth
                };
            });
        },

        toIso(d) {
            const mm = String(d.getMonth() + 1).padStart(2, '0');
            const dd = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${mm}-${dd}`;
        },

        isToday(iso) { return iso === this.toIso(new Date()); },

        /** Recule d'un mois, sans jamais sortir de la plage d'années du <select> (voir yearsBack). */
        prevMonth() {
            if (this.viewMonth === 0) {
                if (this.viewYear <= this.minYear) return; // borne atteinte : on ne bouge plus
                this.viewMonth = 11;
                this.viewYear--;
                return;
            }
            this.viewMonth--;
        },

        /** Avance d'un mois, sans jamais sortir de la plage d'années du <select> (voir yearsForward). */
        nextMonth() {
            if (this.viewMonth === 11) {
                if (this.viewYear >= this.maxYear) return; // borne atteinte : on ne bouge plus
                this.viewMonth = 0;
                this.viewYear++;
                return;
            }
            this.viewMonth++;
        },

        formatDisplay(iso) {
            const normalized = this.normalizeIso(iso);
            // Jamais « Invalid Date » à l'écran : une valeur absente ou illisible n'affiche RIEN.
            if (!normalized) return '';
            return new Date(normalized + 'T00:00:00').toLocaleDateString('fr-FR', {
                day: '2-digit', month: 'long', year: 'numeric'
            });
        },

        /** ISO (yyyy-MM-dd, horodaté toléré) → JJ/MM/AAAA. '' si la valeur est absente. */
        formatInput(iso) {
            const normalized = this.normalizeIso(iso);
            if (!normalized) return '';
            const [y, m, d] = normalized.split('-');
            return `${d}/${m}/${y}`;
        },

        /** JJ/MM/AAAA → ISO, ou null si incomplet/invalide (rejette aussi les débordements de
         *  calendrier silencieusement corrigés par Date, ex. « 31/02/2024 » → mars).
         *  Jour et mois sur 1 OU 2 chiffres : la frappe est normalisée par onTextInput, mais un
         *  COLLAGE (« 1/1/2024 ») arrive tel quel et ne doit pas être perdu en silence. */
        parseInput(text) {
            if (typeof text !== 'string') return null;
            const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(text.trim());
            if (!match) return null;
            const day = Number(match[1]), month = Number(match[2]), year = Number(match[3]);
            const d = new Date(year, month - 1, day);
            if (d.getFullYear() !== year || d.getMonth() !== month - 1 || d.getDate() !== day) return null;
            return this.toIso(d);
        },

        /** Appelé à chaque frappe : insère les « / » au fil de la saisie (l'utilisateur ne tape que
         *  des chiffres) et tente le parsing. Écrit dans `pendingIso`, pas directement dans Model —
         *  ce composant ne connaît pas l'expression Model, seule la balise émise par le Tag Helper
         *  la connaît (même convention que day.iso pour la grille du calendrier). */
        onTextInput() {
            const digits = this.text.replace(/\D/g, '').slice(0, 8);
            let formatted = digits.slice(0, 2);
            if (digits.length > 2) formatted += '/' + digits.slice(2, 4);
            if (digits.length > 4) formatted += '/' + digits.slice(4, 8);
            this.text = formatted;

            this.pendingIso = this.parseInput(formatted);
            if (this.pendingIso) this.setView(this.pendingIso);
        }
    }));

    /**
     * Liste déroulante avec recherche interne (TagHelpers/SelectFieldTagHelper.cs). Même convention
     * que dateField() : ce composant ne porte que l'état d'ouverture/recherche, la valeur
     * sélectionnée (I/O) reste portée par l'expression Alpine du parent (x-model sur le champ caché,
     * relue directement dans les directives émises par le Tag Helper).
     *
     * `options` démarre vide et n'est JAMAIS reçu en argument de factory : le Tag Helper l'alimente
     * via x-effect="options = ..." sur l'élément racine, qui réévalue l'expression (littéral JSON
     * statique ou expression Alpine dynamique, ex. « classrooms.map(...) ») à chaque changement d'une
     * dépendance réactive qu'elle lit — un argument de factory, lui, n'aurait été capturé qu'une
     * fois, figeant la liste sur son état (souvent vide) au tout premier rendu.
     */
    Alpine.data('selectField', () => ({
        open: false,
        search: '',
        options: [],
        // Placement vertical du menu, recalculé à chaque ouverture (auto-flip haut/bas selon la
        // place disponible dans le viewport).
        placement: 'bottom',

        get filteredOptions() {
            const query = this.search.trim().toLowerCase();
            if (!query) return this.options;
            return this.options.filter((option) => option.label.toLowerCase().includes(query));
        },

        /** Classes de position du menu selon le placement calculé (auto-flip). */
        get menuPlacementClass() {
            return this.placement === 'top' ? 'bottom-full mb-2' : 'top-full mt-2';
        },

        toggle() {
            this.open = !this.open;
            if (this.open) {
                this.placement = window.computeFlipPlacement(this.$root, 300);
                this.search = '';
                this.$nextTick(() => this.$refs.search?.focus());
            }
        },

        close() {
            this.open = false;
        },

        labelFor(value) {
            return this.options.find((option) => option.value === value)?.label ?? '';
        }
    }));

    Alpine.data('digitalClock', () => ({
        currentTime: '',
        init() {
            this.updateClock();
            setInterval(() => this.updateClock(), 1000);
        },
        updateClock() {
            const now = new Date();
            const dateStr = now.toLocaleDateString('fr-FR', {
                weekday: 'long',
                year: 'numeric',
                month: 'long',
                day: 'numeric'
            });
            const timeStr = now.toLocaleTimeString('fr-FR', {
                hour: '2-digit',
                minute: '2-digit'
            });
            const capitalizedDate = dateStr.charAt(0).toUpperCase() + dateStr.slice(1);
            this.currentTime = `${capitalizedDate} • ${timeStr}`;
        }
    }));

    // Salutation selon l'heure (barre superieure) — accompagne l'horloge, sans aucune donnee
    // personnelle. Recalculee chaque minute pour franchir les seuils matin / apres-midi / soir /
    // nuit sans recharger la page.
    Alpine.data('greeting', () => ({
        label: '',
        emoji: '',
        init() {
            this.update();
            setInterval(() => this.update(), 60000);
        },
        update() {
            const h = new Date().getHours();
            if (h >= 5 && h < 12)       { this.label = 'Bonjour';        this.emoji = '☀️'; }
            else if (h >= 12 && h < 18) { this.label = 'Bon après-midi'; this.emoji = '🌤️'; }
            else if (h >= 18 && h < 22) { this.label = 'Bonsoir';        this.emoji = '🌆'; }
            else                        { this.label = 'Bonne nuit';     this.emoji = '🌙'; }
        }
    }));
});
