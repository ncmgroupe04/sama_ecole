/*
 * Assistant d'Onboarding — écran /onboarding/select-profile.
 *
 * Le Directeur choisit son PROFIL et sa TRANCHE d'effectif en un seul envoi
 * (POST /onboarding/select-profile) : la souscription passe de PendingOnboarding à Active et le quota de la
 * tranche s'applique tout de suite. Ce script remplace l'ancien assistant à clic unique
 * (POST /schools/current/settings/establishment-profile, qui ne portait pas la tranche).
 *
 * Au chargement, l'état de la souscription est lu (GET /onboarding/subscription — route qui reste ouverte
 * pendant l'Onboarding) :
 *   - déjà Active (ou autre statut que PendingOnboarding) → plus rien à configurer, on renvoie à
 *     l'atterrissage habituel ;
 *   - PendingOnboarding mais rôle ≠ Directeur → message d'attente (seul le Directeur peut valider) ;
 *   - sinon → le formulaire.
 *
 * Les identifiants de profil et de tranche sont ceux de l'API (ProfileType, StudentQuotaTier) — jamais
 * ceux de l'ancien enum ProfileEtablissement.
 */
document.addEventListener('alpine:init', () => {
    // Plafonds affichés : MIROIR de StudentQuotaDefaults (Domain). Le serveur reste maître — c'est lui qui
    // pose les chiffres à partir de la tranche, ceux-ci ne servent qu'à l'affichage.
    const TIERS = [
        { id: 'Tier_100', label: '100', hint: "élèves (tolérance jusqu'à 110)" },
        { id: 'Tier1_150', label: '150', hint: "élèves (tolérance jusqu'à 160)" },
        { id: 'Tier_250', label: '250', hint: "élèves (tolérance jusqu'à 260)" },
        { id: 'Tier2_400', label: '400', hint: "élèves (tolérance jusqu'à 420)" }
        // Au-delà de 400 élèves : offre « Grand Groupe », sur devis — jamais choisie ici (refusée en 422 par l'API).
    ];

    const PROFILES = [
        {
            id: 'EnseignementGeneral', icon: 'chart-multiple', tag: 'Multi-cycles', recommended: true,
            title: 'Enseignement Général',
            description: 'Le socle académique national complet — de la Maternelle au Lycée, Séries, coefficients, bulletins.',
            bullets: ['Maternelle, Primaire, Collège & Lycée', 'Séries L/S & coefficients', 'Notes, bulletins & emplois du temps', 'Suivi des enseignants']
        },
        {
            id: 'Elementaire', icon: 'book', tag: 'Cycle Primaire',
            title: 'Élémentaire / Primaire',
            description: 'Cycles Maternelle (PS/MS/GS) et Élémentaire (CI au CM2), sans Collège ni Lycée.',
            bullets: ['Compositions mensuelles & trimestrielles', 'Préparation CFEE / Entrée en 6ᵉ', 'Pas de Séries ni de Secondaire']
        },
        {
            id: 'FrancoArabe', icon: 'globe', tag: 'Bilingue',
            title: 'Franco-Arabe',
            description: 'Le socle académique, enrichi du bilinguisme et des disciplines arabes/islamiques.',
            bullets: ['Disciplines arabes & islamiques', 'Bulletins bilingues', 'Socle académique complet']
        },
        {
            id: 'InternatDaara', icon: 'bed', tag: 'Vie collective',
            title: 'Internat / Daara Moderne',
            description: 'Le socle académique, complété par la vie collective et le suivi coranique.',
            bullets: ['Dortoirs & hébergement', 'Suivi coranique par Hizb, Halqas & Oustaz', 'Pension & tuteurs de sortie']
        },
        {
            id: 'DaaraMemorisation', icon: 'target', tag: 'Hifz',
            title: 'Daara — Mémorisation',
            description: 'La mémorisation du Coran au centre : suivi des Hizb par Halqa et vie collective, sans notes ni bulletins.',
            bullets: ['Suivi coranique par Hizb, Halqas & Oustaz', 'Dortoirs, hébergement & sorties', 'Scolarités, caisse & rapports financiers']
        },
        {
            id: 'ComptabiliteRapports', icon: 'sparkle', tag: 'Gestion essentielle',
            title: 'Comptabilité & Rapports',
            description: "L'essentiel pour gérer les inscriptions, les encaissements et les rapports financiers, sans notes ni bulletins.",
            bullets: ['Inscriptions & annuaire des élèves', 'Caisse, frais & rapports financiers', 'Import / export Excel']
        }
    ];

    Alpine.data('onboardingWizard', () => ({
        profiles: PROFILES,
        tiers: TIERS,

        // loading | ready | waitDirector | loadError
        state: 'loading',
        profile: null,
        tier: null,
        isSubmitting: false,
        error: null,

        get canSubmit() {
            return this.profile !== null && this.tier !== null && !this.isSubmitting;
        },

        async load() {
            this.error = null;
            this.state = 'loading';

            try {
                const subscription = await window.api.get('/onboarding/subscription');

                if (subscription.status !== 'PendingOnboarding') {
                    window.location.replace(window.auth.defaultLandingForRole());
                    return;
                }

                this.state = window.auth.role === 'Directeur' ? 'ready' : 'waitDirector';
            } catch (err) {
                this.error = (err && err.message) || 'Impossible de charger votre configuration. Réessayez.';
                this.state = 'loadError';
            }
        },

        async submit() {
            if (!this.canSubmit) return;

            this.error = null;
            this.isSubmitting = true;

            try {
                const subscription = await window.api.post('/onboarding/select-profile', {
                    profile: this.profile,
                    tier: this.tier
                });

                // Mémorise le profil pour l'atterrissage (auth.js › defaultLandingForRole) dès cette
                // redirection, sans attendre un chargement des réglages.
                window.auth.rememberProfile(subscription.profileType);
                window.location.assign(window.auth.defaultLandingForRole());
            } catch (err) {
                // 409 : le choix a déjà été fait ailleurs (autre onglet) — plus rien à configurer.
                if (err && err.code === 'ONBOARDING_ALREADY_COMPLETED') {
                    window.location.replace(window.auth.defaultLandingForRole());
                    return;
                }

                this.error = (err && err.message) || "Impossible d'enregistrer votre choix. Réessayez.";
                this.isSubmitting = false;
            }
        }
    }));
});
