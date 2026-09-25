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
 * Modale de GUIDAGE universelle — window.guide(titre, message).
 *
 * À appeler quand un bouton d'action a un PRÉ-REQUIS non rempli (aucun enseignant sélectionné, aucune
 * année scolaire active, aucune session de caisse ouverte…). À la place d'un bandeau rouge qui donne
 * une fausse impression d'erreur système, une modale centrée bienveillante qui explique l'étape à
 * faire. C'est à l'appelant d'AUSSI court-circuiter l'appel API voué à l'échec (return après l'appel).
 *
 * Pilote le store Alpine `guide` (défini dans le bloc alpine:init ci-dessous), rendu une seule fois
 * par _Layout (_GuidanceModal.cshtml). Comme window.closeAllModals, ce point d'entrée reste utilisable
 * depuis n'importe quel script classique, sans injecter le store.
 */
window.guide = function (title, message) {
    // Appelé sur un clic bouton : Alpine est initialisé depuis longtemps, le store existe.
    if (typeof Alpine !== 'undefined' && Alpine.store('guide')) {
        Alpine.store('guide').show(title, message);
    } else {
        // Garde-fou (Alpine indisponible) : au pire un avertissement, jamais un crash de l'écran.
        console.warn('[guide] store Alpine indisponible :', title, message);
    }
};

/**
 * Verrou de défilement de l'arrière-plan pendant qu'un overlay plein écran est ouvert (modale
 * modal-shell, tiroir latéral mobile). Sans lui, la page continue de défiler DERRIÈRE la modale
 * pendant qu'on navigue dedans — le « double scroll » signalé.
 *
 * Deux cibles, parce que le conteneur défilant dépend du gabarit : <main> dans _Layout (voir
 * help.js backToTop — `<body>` y est `overflow-hidden`), <html> dans _AuthLayout. On fige les deux,
 * l'un des deux est toujours le bon et figer l'autre est sans effet.
 *
 * Compensation de la largeur de la barre de défilement par un `padding-right` temporaire : sans ça,
 * sa disparition élargit le contenu de ~15 px à chaque ouverture (saut visible sous le fond flouté).
 *
 * Set d'éléments plutôt qu'un booléen : deux overlays superposés (rare — p. ex. l'assistant de
 * démarrage ouvert par-dessus une fiche) ne déverrouillent qu'une fois le dernier fermé. `set()` est
 * idempotent : x-effect l'appelle en boucle, `dataset.scrollLocked` évite de re-mesurer le gap.
 */
window.__modalScrollLock = {
    _open: new Set(),

    set(el, isOpen) {
        if (!el) return;
        if (isOpen) this._open.add(el); else this._open.delete(el);
        this._apply(this._open.size > 0);
    },

    _apply(lock) {
        const targets = [document.documentElement];
        const main = document.querySelector('main');
        if (main) targets.push(main);

        for (const t of targets) {
            if (lock && !t.dataset.scrollLocked) {
                const gap = t === document.documentElement
                    ? window.innerWidth - document.documentElement.clientWidth
                    : t.offsetWidth - t.clientWidth;
                if (gap > 0) t.style.paddingRight = gap + 'px';
                t.style.overflow = 'hidden';
                t.dataset.scrollLocked = '1';
            } else if (!lock && t.dataset.scrollLocked) {
                t.style.overflow = '';
                t.style.paddingRight = '';
                delete t.dataset.scrollLocked;
            }
        }
    }
};

/**
 * Position `fixed` (coordonnées VIEWPORT) d'un popover flottant — calendrier ou liste déroulante.
 * Partagé par selectField() (listes déroulantes — filtres Mois/Années, onglets…) et dateField()
 * (calendrier). Le résultat se transforme en chaîne de style par window.floatingStyleFrom().
 *
 * REMPLACE un ancien mécanisme `position: absolute` + classe CSS `top-full`/`bottom-full`, qui ne
 * protégeait le popover QUE contre un dépassement du bas de l'ÉCRAN. Un élément `absolute` reste
 * physiquement DANS son ancêtre positionné : un ancêtre qui rogne le débordement (`overflow-y-auto`
 * — le corps défilant d'une <modal-shell>, typiquement) le découpe bien avant qu'il n'atteigne le
 * bord de l'écran, quel que soit le côté choisi. C'était la cause des calendriers/menus tronqués
 * (haut OU bas coupé, en-tête mois/année invisible) dès qu'un champ se trouvait vers le bas d'une
 * modale ou d'une page qui défile.
 *
 * `position: fixed` échappe à ce rognage par construction — même principe que <modal-shell>, voir
 * ModalShellTagHelper — le popover n'est plus jamais tronqué par la modale ou le panneau qui
 * l'héberge, seulement par le VIEWPORT lui-même, qu'on borne nous-mêmes ici : `maxHeight` clampe le
 * popover à la place réellement disponible (il défile alors EN INTERNE, jamais invisible en silence)
 * et `left`/`width` le ramènent dans l'écran horizontalement.
 *
 * AUTO-FLIP + OFFSET : par défaut le menu s'ouvre SOUS le champ, à `GAP` px sous son bord bas. Quand
 * la place manque réellement dessous et qu'il y en a davantage dessus, il bascule AU-DESSUS — ancré
 * cette fois par son bord BAS à `GAP` px du haut du champ (propriété CSS `bottom`, pas `top`). C'est
 * ce point qui corrige le « décalage bizarre » signalé : l'ancien calcul plaçait `top = rect.top -
 * maxHeight` en réservant TOUTE la hauteur maximale, si bien qu'un menu au contenu court (2-3
 * options) flottait loin au-dessus du champ, détaché de lui. Ancré par le bas, le menu reste collé
 * au champ et grandit vers le haut selon son contenu, borné par `maxHeight`.
 *
 * Recalculée à l'OUVERTURE seulement (pas en continu) : le composant hôte ferme le popover au
 * défilement d'un ancêtre (voir closeOnScroll ci-dessous) plutôt que de re-suivre une ancre qui
 * bouge — plus simple, et un popover qui suit le doigt pendant qu'on défile n'apporte rien ici.
 *
 * @param {Element} anchorEl Élément racine du composant (le déclencheur mesuré).
 * @param {number} menuHeight Hauteur approximative du popover à pleine place, en pixels.
 * @param {number} [menuWidth] Largeur fixe voulue ; omis, la largeur suit celle du déclencheur
 *   (reproduit l'ancien `w-full` d'un select-field relatif à son ancre).
 * @returns {{openUp:boolean, top:(number|null), bottom:(number|null), left:number, width:number, maxHeight:number}}
 *   en pixels viewport. `top` est renseigné pour une ouverture vers le bas, `bottom` pour une
 *   ouverture vers le haut ; l'autre vaut null.
 */
window.computeFloatingPosition = function (anchorEl, menuHeight, menuWidth) {
    const margin = 8;   // respiration minimale entre le popover et le bord du viewport
    const gap = 6;      // décalage voulu entre le champ et le popover (4-8 px demandés)
    const minHeight = 120; // en deçà, un menu ne sert plus à rien — on garde ce plancher, défilement interne
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
    const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0;
    const needed = menuHeight || 300;

    if (!anchorEl || typeof anchorEl.getBoundingClientRect !== 'function') {
        return { openUp: false, top: margin, bottom: null, left: margin, width: menuWidth || 300, maxHeight: needed };
    }

    const rect = anchorEl.getBoundingClientRect();
    const spaceBelow = viewportHeight - rect.bottom - gap - margin;
    const spaceAbove = rect.top - gap - margin;
    // Ouvre vers le bas par défaut ; bascule vers le haut seulement quand la place manque réellement
    // dessous ET qu'il y en a davantage dessus — sinon, autant garder le sens le plus prévisible.
    const openUp = spaceBelow < needed && spaceAbove > spaceBelow;

    const chosenSpace = openUp ? spaceAbove : spaceBelow;
    // Jamais plus haut que la place réelle du côté choisi (sinon le menu déborde du viewport, contenu
    // inaccessible) ; jamais moins que `minHeight`, pour rester utilisable même à l'étroit.
    const maxHeight = Math.max(minHeight, Math.min(needed, chosenSpace));

    // Largeur bornée au viewport (moins 2×marge) : sur un téléphone étroit, un popover à largeur
    // fixe (le calendrier fait 300 px) débordait à droite dès que le champ était près du bord —
    // `left` seul ne suffit pas, il faut aussi rétrécir le menu. Défilement interne si besoin.
    const width = Math.min(menuWidth || rect.width, Math.max(0, viewportWidth - 2 * margin));
    const left = Math.min(Math.max(margin, rect.left), Math.max(margin, viewportWidth - margin - width));

    return openUp
        ? { openUp: true, top: null, bottom: viewportHeight - rect.top + gap, left, width, maxHeight }
        : { openUp: false, top: rect.bottom + gap, bottom: null, left, width, maxHeight };
};

/**
 * Chaîne de style `:style` d'un popover flottant à partir du retour de window.computeFloatingPosition.
 * Émet `top:` OU `bottom:` selon le sens d'ouverture — les deux composants hôtes (dateField,
 * selectField) partagent ce format pour ne pas dupliquer la règle top/bottom.
 */
window.floatingStyleFrom = function (pos) {
    const vertical = pos.openUp ? `bottom:${pos.bottom}px` : `top:${pos.top}px`;
    return `${vertical}; left:${pos.left}px; width:${pos.width}px; max-height:${pos.maxHeight}px;`;
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
            // z-[60] : au-dessus de la modale (z-50) — une notification reste lisible même modale ouverte.
            el.className = 'fixed top-4 right-4 z-[60] flex flex-col gap-2 items-end pointer-events-none';
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
     * Store de la modale de GUIDAGE (voir window.guide plus haut et _GuidanceModal.cshtml).
     * `visible` (booléen) pilote l'ouverture de la modal-shell ; `title` / `message` son contenu.
     */
    Alpine.store('guide', {
        visible: false,
        title: '',
        message: '',
        show(title, message) {
            this.title = title || 'Information';
            this.message = message || '';
            this.visible = true;
        },
        dismiss() {
            this.visible = false;
        }
    });

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
        // Position `fixed` (top/left/width/max-height en px), recalculée à chaque ouverture — voir
        // window.computeFloatingPosition. Chaîne vide tant que le popover n'a jamais été ouvert :
        // x-show le garde caché avant ça, aucun flash à la position (0,0) par défaut.
        floatingStyle: '',
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
            // Référence STABLE (une seule fonction pour toute la vie du composant) : addEventListener
            // et removeEventListener doivent recevoir exactement la même référence pour que le retrait
            // fonctionne — une fonction fléchée recréée à chaque appel de toggle() ne se retirerait
            // jamais, laissant une fuite d'écouteurs à chaque ouverture/fermeture.
            this.boundClose = () => this.close();
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
                this.close();
                return;
            }
            this.setView(currentIso);
            // AVANT d'afficher (pas de $nextTick) : $root, le déclencheur, est déjà dans le DOM à sa
            // position finale — inutile d'attendre. Calculer après aurait affiché le popover une
            // frame à sa position précédente (ou vide, à la toute première ouverture) avant de
            // sauter à la bonne place.
            const pos = window.computeFloatingPosition(this.$root, 380, 300);
            this.floatingStyle = window.floatingStyleFrom(pos);
            this.open = true;
            // position:fixed ne suit pas le défilement d'un ancêtre (corps de modale, panneau) : on
            // ferme plutôt que de laisser le popover figé au-dessus d'un autre champ. capture:true
            // attrape aussi le défilement d'un ancêtre scrollable, qui ne bouillonne pas jusqu'à
            // window en phase normale.
            window.addEventListener('scroll', this.boundClose, true);
        },

        close() {
            this.open = false;
            window.removeEventListener('scroll', this.boundClose, true);
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
        // Position `fixed` (top/left/width/max-height en px), recalculée à chaque ouverture — voir
        // window.computeFloatingPosition et le commentaire équivalent de dateField() ci-dessus.
        floatingStyle: '',

        init() {
            // Référence STABLE : voir le commentaire équivalent de dateField.init().
            this.boundClose = () => this.close();
        },

        get filteredOptions() {
            const query = this.search.trim().toLowerCase();
            if (!query) return this.options;
            return this.options.filter((option) => option.label.toLowerCase().includes(query));
        },

        toggle() {
            if (this.open) {
                this.close();
                return;
            }
            // AVANT d'afficher (pas de $nextTick) : voir le commentaire équivalent de dateField.toggle().
            const pos = window.computeFloatingPosition(this.$root, 300);
            this.floatingStyle = window.floatingStyleFrom(pos);
            this.open = true;
            this.search = '';
            // Le champ de recherche, lui, n'existe dans le DOM qu'une fois `open` vrai (x-show) :
            // il lui faut bien le prochain tick avant de pouvoir recevoir le focus.
            this.$nextTick(() => this.$refs.search?.focus());
            // position:fixed ne suit pas le défilement d'un ancêtre : voir le commentaire équivalent
            // de dateField.toggle().
            window.addEventListener('scroll', this.boundClose, true);
        },

        close() {
            this.open = false;
            window.removeEventListener('scroll', this.boundClose, true);
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
            // Mois abrégé (« sept. », « janv. »…) : la pilule reste compacte sans rogner le jour de la semaine.
            const dateStr = now.toLocaleDateString('fr-FR', {
                weekday: 'long',
                year: 'numeric',
                month: 'short',
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
