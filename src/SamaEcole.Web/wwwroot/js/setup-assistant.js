/*
 * Assistant de Premier Paramétrage Express — bouton de la barre supérieure (_Layout.cshtml).
 *
 * DISTINCT du Centre d'aide (/aide, help.js), qui est une documentation de fond consultable à tout
 * moment : cet assistant-ci est un PARCOURS d'amorçage. Il répond à une seule question — « par quoi
 * commencer, et dans quel ordre ? » — pour qu'un Directeur qui se connecte la première fois ne tente
 * pas d'inscrire un élève avant d'avoir créé les classes et la grille tarifaire.
 *
 * Il ne CONFIGURE rien lui-même : chaque étape renvoie vers l'écran déjà en place (Paramètres,
 * Classes, Frais…). Il se contente de LIRE l'état réel de l'établissement via l'API (mêmes routes,
 * mêmes gardes que les écrans) pour cocher ce qui est fait et calculer un pourcentage d'avancement.
 *
 * Chargé une fois par _Layout.cshtml, comme ui-components.js : script classique (donc exécuté avant
 * le script différé d'Alpine), l'écouteur alpine:init est enregistré à temps. Dépend de window.auth
 * et window.api — chargés avant lui.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('setupAssistant', () => ({
        /*
         * Seuls les rôles qui PILOTENT le paramétrage d'un établissement voient l'assistant. Le Super
         * Admin (console plateforme, aucune école) et les rôles opérationnels (Finance, Enseignant,
         * Surveillant) n'ont rien à amorcer ici. Masquer le bouton reste un confort d'affichage : les
         * routes appelées ci-dessous gardent chacune leur propre autorisation côté API.
         */
        ALLOWED_ROLES: ['Directeur', 'Secretariat'],

        // Ouverture AUTOMATIQUE au tout premier accès, une seule fois par navigateur. Ensuite
        // l'assistant ne se rouvre plus seul — il reste accessible par son bouton.
        INTRO_KEY: 'unikol.setupAssistant.introShown',

        eligible: false,
        open: false,
        loaded: false,
        checking: false,
        // Type d'établissement : « Public » masque le module Finance (auth.js sidebarNav) — l'étape
        // « Grille tarifaire » devient alors sans objet plutôt que « à faire ».
        isPublicSchool: false,
        // Index de l'étape dépliée (une seule à la fois), ou null.
        expanded: null,

        /*
         * Le CONTENU du parcours. `state` est recalculé par refresh() à partir de la base :
         *   'todo' | 'done' | 'na'  (na = non applicable, ex. grille tarifaire d'un établissement public)
         * `why` : 2–3 phrases — « pourquoi cette étape, pourquoi maintenant ».
         * `actions` : 1 ou 2 raccourcis directs vers l'écran concerné.
         */
        steps: [
            {
                key: 'annee',
                icon: 'calendar',
                title: 'Année scolaire & périodes',
                why: "Tout dans Unikol se rattache à un exercice : inscriptions, frais, notes, bulletins et trésorerie. "
                    + "Activez l'année en cours pour que chaque saisie tombe dans le bon exercice — les trimestres ou "
                    + "semestres sont générés automatiquement à partir de ses dates.",
                actions: [{ label: "Activer l'année scolaire", url: '/parametres?tab=annees-scolaires' }],
                state: 'todo'
            },
            {
                key: 'pedagogie',
                icon: 'book',
                title: 'Configuration pédagogique',
                why: "Créez les niveaux et les classes, puis les matières avec leurs coefficients. Sans au moins une "
                    + "classe, aucun élève ne peut être inscrit ; sans coefficients, aucune moyenne ni bulletin ne "
                    + "peut être calculé.",
                actions: [
                    { label: 'Niveaux & classes', url: '/classes' },
                    { label: 'Matières & coefficients', url: '/matieres' }
                ],
                state: 'todo'
            },
            {
                key: 'frais',
                icon: 'wallet',
                title: 'Grille tarifaire & plan financier',
                why: "Déclarez les types de frais (inscription, mensualité, transport…), puis le montant par classe et "
                    + "le nombre de mensualités. L'inscription d'un élève s'appuie sur cette grille pour bâtir son "
                    + "échéancier ; sans elle, la Caisse n'a rien à encaisser.",
                naReason: "Module Finance masqué pour les établissements publics : la grille tarifaire ne s'applique pas.",
                actions: [{ label: 'Configurer les frais', url: '/frais' }],
                state: 'todo'
            },
            {
                key: 'enseignants',
                icon: 'users',
                title: 'Fiches enseignants & attributions',
                why: "Enregistrez les enseignants, puis rattachez chacun à ses matières et à ses classes. Ces "
                    + "attributions ouvrent la saisie des notes et l'appel en classe aux bons intervenants.",
                actions: [{ label: 'Ajouter les enseignants', url: '/enseignants' }],
                state: 'todo'
            },
            {
                key: 'inscription',
                icon: 'document',
                title: 'Première inscription & échéancier',
                why: "Inscrivez un premier élève dans une classe : Unikol calcule ses frais, crée son échéancier et "
                    + "édite son reçu. C'est l'étape qui relie pédagogie, finances et Caisse — faites-la une fois "
                    + "pour vérifier que toute la chaîne fonctionne.",
                actions: [{ label: 'Inscrire un élève', url: '/inscriptions' }],
                state: 'todo'
            },
            {
                key: 'simen',
                icon: 'globe',
                title: 'Paramètres étatiques (SIMEN)',
                why: "Renseignez le code établissement national et les identifiants SIMEN. Ils sont exigés pour "
                    + "l'export Planète, l'attribution d'un IEN provisoire et les documents officiels destinés au "
                    + "ministère.",
                actions: [{ label: 'Renseigner le code SIMEN', url: '/parametres?tab=integration-etatique' }],
                state: 'todo'
            }
        ],

        init() {
            this.eligible = !!(window.auth
                && window.auth.isAuthenticated()
                && this.ALLOWED_ROLES.includes(window.auth.role));
            if (!this.eligible) return;

            // Fire-and-forget : l'assistant ne doit jamais retarder le rendu de la page.
            this.refresh().then(() => {
                try {
                    if (this.percent < 100 && localStorage.getItem(this.INTRO_KEY) !== '1') {
                        this.open = true;
                    }
                    localStorage.setItem(this.INTRO_KEY, '1');
                } catch (_) {
                    // localStorage indisponible (navigation privée stricte) : pas d'ouverture auto,
                    // rien de bloquant — le bouton reste accessible.
                }
            });
        },

        // ------------------------------------------------------------------ Détection de l'état réel

        /**
         * Interroge la base via les mêmes routes que les écrans. `Promise.allSettled` : une route qui
         * échoue (réseau, 403 pour un rôle donné) laisse simplement son étape sur 'todo' — un
         * assistant d'aide ne bloque jamais et ne ment jamais en cochant par défaut.
         */
        async refresh() {
            if (!this.eligible || this.checking) return;
            this.checking = true;

            const results = await Promise.allSettled([
                window.api.get('/school-years'),
                window.api.get('/classrooms'),
                window.api.get('/subjects'),
                window.api.get('/finance/fee-categories'),
                window.api.get('/finance/fees'),
                window.api.get('/teachers?page=1&pageSize=1'),
                window.api.get('/students?page=1&pageSize=1'),
                window.api.get('/schools/current'),
                window.api.get('/schools/current/settings')
            ]);
            const value = (i) => (results[i].status === 'fulfilled' ? results[i].value : null);
            const nonEmpty = (v) => Array.isArray(v) && v.length > 0;
            const hasRows = (paginated) => !!paginated
                && ((paginated.totalCount || 0) > 0 || (Array.isArray(paginated.items) && paginated.items.length > 0));

            const years = value(0);
            this.setState('annee', Array.isArray(years) && years.some((y) => y && y.isActive));

            this.setState('pedagogie', nonEmpty(value(1)) && nonEmpty(value(2)));

            const settings = value(8);
            this.isPublicSchool = !!(settings && settings.typeEtablissement === 'Public');
            if (this.isPublicSchool) {
                this.setState('frais', null, /* na */ true);
            } else {
                this.setState('frais', nonEmpty(value(3)) && nonEmpty(value(4)));
            }

            this.setState('enseignants', hasRows(value(5)));
            this.setState('inscription', hasRows(value(6)));

            const school = value(7);
            this.setState('simen', !!(school && school.nationalSchoolCode));

            this.loaded = true;
            this.checking = false;

            // Déplie l'étape courante (première étape applicable non faite et déverrouillée).
            const current = this.firstActionableIndex;
            this.expanded = current >= 0 ? current : null;
        },

        setState(key, done, na = false) {
            const step = this.steps.find((s) => s.key === key);
            if (step) step.state = na ? 'na' : (done ? 'done' : 'todo');
        },

        // ------------------------------------------------------------------ Progression & verrouillage

        get applicableSteps() {
            return this.steps.filter((s) => s.state !== 'na');
        },

        get doneCount() {
            return this.applicableSteps.filter((s) => s.state === 'done').length;
        },

        /** Pourcentage d'avancement du paramétrage — étapes non applicables exclues du calcul. */
        get percent() {
            const total = this.applicableSteps.length;
            return total === 0 ? 0 : Math.round((this.doneCount / total) * 100);
        },

        get allDone() {
            return this.loaded && this.percent === 100;
        },

        /**
         * Parcours SÉQUENTIEL : une étape n'est déverrouillée que si toutes les étapes applicables
         * qui la précèdent sont faites. C'est ce qui empêche d'attaquer l'inscription avant les
         * classes et la grille tarifaire.
         */
        isUnlocked(index) {
            for (let i = 0; i < index; i++) {
                const s = this.steps[i];
                if (s.state === 'na') continue;
                if (s.state !== 'done') return false;
            }
            return true;
        },

        /** Étape « à faire » verrouillée : lisible, mais son action reste inactive. */
        isLocked(index) {
            return this.steps[index].state === 'todo' && !this.isUnlocked(index);
        },

        get firstActionableIndex() {
            return this.steps.findIndex((s, i) => s.state === 'todo' && this.isUnlocked(i));
        },

        badgeFor(step, index) {
            if (step.state === 'done') return { label: 'Complété', cls: 'status-badge-success' };
            if (step.state === 'na') return { label: 'Non applicable', cls: 'status-badge-neutral' };
            if (!this.isUnlocked(index)) return { label: 'Verrouillé', cls: 'status-badge-neutral' };
            return { label: 'À faire', cls: 'status-badge-warning' };
        },

        // ------------------------------------------------------------------ Interactions

        toggle() {
            this.open = !this.open;
            if (this.open) this.refresh();
        },

        close() {
            this.open = false;
        },

        toggleStep(index) {
            if (this.isLocked(index)) return; // verrouillée : non dépliable
            this.expanded = this.expanded === index ? null : index;
        },

        go(url) {
            // Navigation MVC classique : rechargement complet. L'état de l'assistant ne survit pas —
            // c'est voulu, on recalcule l'avancement à la prochaine ouverture.
            window.location.assign(url);
        }
    }));
});
