/**
 * Seuils du conseil de classe (Évolution N°7) — Paramètres › Notation & mentions.
 *
 * Aucun calcul ici : les seuils sont des nombres sur /20 que le serveur valide (ordre, bornes) et applique
 * (CouncilRules). Écriture Directeur seul ; `canEdit` ne fait que masquer, le serveur reste juge (403).
 */
document.addEventListener('alpine:init', () => {
    // Valeurs de la spécification MEN (SchoolSettingsDefaults) — « Revenir aux valeurs du Ministère ».
    const DEFAULTS = {
        felicitationsMin: 14, honorRollMin: 12, encouragementsMin: 12,
        eliminatoryGrade: 5, promotionMin: 10, repeatMin: 8.5
    };

    const FIELDS = [
        { key: 'felicitationsMin', label: 'Félicitations dès', hint: 'Moyenne générale sur 20' },
        { key: 'honorRollMin', label: "Tableau d'honneur dès", hint: 'Sans aucune note éliminatoire' },
        { key: 'encouragementsMin', label: 'Encouragements dès', hint: 'Moyenne générale sur 20' },
        { key: 'eliminatoryGrade', label: 'Note éliminatoire sous', hint: 'Moyenne de matière strictement inférieure' },
        { key: 'promotionMin', label: 'Admis en classe supérieure dès', hint: 'Moyenne annuelle sur 20' },
        { key: 'repeatMin', label: 'Autorisé à redoubler dès', hint: 'En dessous : exclu' }
    ];

    const toText = (value) => (value === null || value === undefined ? '' : String(value).replace('.', ','));
    const toNumber = (text) => {
        const value = Number(String(text ?? '').trim().replace(',', '.'));
        return Number.isFinite(value) ? value : null;
    };

    Alpine.data('councilRulesPanel', () => ({
        fields: FIELDS,
        form: Object.fromEntries(FIELDS.map((f) => [f.key, toText(DEFAULTS[f.key])])),
        isSaving: false,
        error: null,
        notice: null,

        get canEdit() {
            return window.auth.role === 'Directeur';
        },

        async init() {
            try {
                this.apply(await window.api.get('/report-cards/council-rules'));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Impossible de charger les seuils du conseil de classe.');
            }
        },

        apply(rules) {
            if (!rules) return;
            for (const field of FIELDS) this.form[field.key] = toText(rules[field.key]);
        },

        payload() {
            const body = {};
            for (const field of FIELDS) body[field.key] = toNumber(this.form[field.key]);
            return body;
        },

        resetDefaults() {
            this.apply(DEFAULTS);
            this.notice = null;
        },

        async save() {
            const body = this.payload();
            if (Object.values(body).some((v) => v === null)) {
                this.error = 'Chaque seuil doit être un nombre (ex. 12 ou 8,5).';
                return;
            }
            this.isSaving = true;
            this.error = null;
            this.notice = null;
            try {
                this.apply(await window.api.put('/report-cards/council-rules', body));
                this.notice = 'Seuils enregistrés.';
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'enregistrement des seuils.");
            } finally {
                this.isSaving = false;
            }
        }
    }));
});
