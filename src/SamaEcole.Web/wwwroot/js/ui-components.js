/*
 * Composants Alpine transverses (Volume 5 §7), chargés une fois par _Layout.cshtml — au même titre
 * que sidebarNav/sessionMenu (auth.js) — pour rester disponibles sur tout écran hôte d'un
 * <date-field> (TagHelpers/DateFieldTagHelper.cs).
 */
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
        viewYear: null,
        viewMonth: null,
        weekdayLabels: ['Lu', 'Ma', 'Me', 'Je', 'Ve', 'Sa', 'Di'],

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

        setView(iso) {
            const base = iso ? new Date(iso + 'T00:00:00') : new Date();
            this.viewYear = base.getFullYear();
            this.viewMonth = base.getMonth();
        },

        /** Rouvre toujours sur le mois de la valeur actuelle — un panneau réutilisé (modal-shell,
         *  x-show) garde sinon la navigation du dernier enregistrement affiché. */
        toggle(currentIso) {
            if (!this.open) {
                this.setView(currentIso);
            }
            this.open = !this.open;
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

        prevMonth() {
            this.viewMonth--;
            if (this.viewMonth < 0) { this.viewMonth = 11; this.viewYear--; }
        },

        nextMonth() {
            this.viewMonth++;
            if (this.viewMonth > 11) { this.viewMonth = 0; this.viewYear++; }
        },

        formatDisplay(iso) {
            return new Date(iso + 'T00:00:00').toLocaleDateString('fr-FR', {
                day: '2-digit', month: 'long', year: 'numeric'
            });
        },

        /** ISO (yyyy-MM-dd) → JJ/MM/AAAA, pour le champ texte. */
        formatInput(iso) {
            const [y, m, d] = iso.split('-');
            return `${d}/${m}/${y}`;
        },

        /** JJ/MM/AAAA → ISO, ou null si incomplet/invalide (rejette aussi les débordements de
         *  calendrier silencieusement corrigés par Date, ex. « 31/02/2024 » → mars). */
        parseInput(text) {
            const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text.trim());
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

        get filteredOptions() {
            const query = this.search.trim().toLowerCase();
            if (!query) return this.options;
            return this.options.filter((option) => option.label.toLowerCase().includes(query));
        },

        toggle() {
            this.open = !this.open;
            if (this.open) {
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
});
