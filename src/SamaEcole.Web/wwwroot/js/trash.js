/**
 * Corbeille et restauration — composant partagé des écrans qui archivent une donnée réutilisable.
 *
 * Conception soft delete 2026-10-01 §3.2 : une suppression est logique, et recréer un élément dont l'identité
 * n'existe que parmi les supprimés est refusé en 409 ARCHIVED_ENTITY_EXISTS. L'utilisateur doit alors
 * RESTAURER, pas dupliquer. Ce fichier porte les deux moitiés du parcours :
 *   - le panneau « Éléments supprimés » (`trashPanel`), qui liste la corbeille et restaure un élément ;
 *   - `openFor()`, appelé depuis le bandeau d'erreur d'un formulaire de création, qui ouvre ce panneau sur
 *     l'élément en doublon.
 *
 * Dépend de api.js et ui-components.js (toast), à charger avant. Les règles métier (tenant, rôles,
 * dépendances, conflit d'identité active) vivent côté API : l'écran n'en refait aucune, il affiche le message
 * du serveur.
 */
(function () {
    function formatDate(iso) {
        if (!iso) return '';
        const date = new Date(iso);
        return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString('fr-FR');
    }

    function formatDay(isoDay) {
        if (!isoDay) return '';
        const date = new Date(`${isoDay}T00:00:00`);
        return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString('fr-FR');
    }

    /**
     * Une entrée par type d'élément restaurable. `list`/`restore` sont les routes de l'API ; `label` et `detail`
     * disent comment présenter une ligne de la corbeille (le DTO diffère d'une entité à l'autre).
     */
    const KINDS = {
        buildings: {
            title: 'Bâtiments supprimés',
            noun: 'Bâtiment',
            list: '/buildings/deleted',
            restore: (id) => `/buildings/${id}/restore`,
            label: (item) => item.name,
            detail: (item) => item.description || ''
        },
        rooms: {
            title: 'Salles supprimées',
            noun: 'Salle',
            list: '/rooms/deleted',
            restore: (id) => `/rooms/${id}/restore`,
            label: (item) => item.name,
            detail: () => ''
        },
        classrooms: {
            title: 'Classes supprimées',
            noun: 'Classe',
            list: '/classrooms/deleted',
            restore: (id) => `/classrooms/${id}/restore`,
            label: (item) => item.name,
            detail: (item) => item.level || ''
        },
        'fee-categories': {
            title: 'Catégories de frais supprimées',
            noun: 'Catégorie',
            list: '/finance/fee-categories/deleted',
            restore: (id) => `/finance/fee-categories/${id}/restore`,
            label: (item) => item.name,
            detail: () => ''
        },
        mentions: {
            title: 'Mentions supprimées',
            noun: 'Mention',
            list: '/grades/mentions/deleted',
            restore: (id) => `/grades/mentions/${id}/restore`,
            label: (item) => item.label,
            detail: (item) => (item.minAverage === undefined || item.minAverage === null ? '' : `Seuil ${item.minAverage}`)
        },
        'school-years': {
            title: 'Années scolaires supprimées',
            noun: 'Année scolaire',
            list: '/school-years/deleted',
            restore: (id) => `/school-years/${id}/restore`,
            label: (item) => item.label,
            detail: (item) => `${formatDay(item.startDate)} → ${formatDay(item.endDate)}`
        }
    };

    /**
     * Phrase de succès d'une restauration. Seule l'année scolaire renvoie un compte rendu (trimestres et
     * affectations revenus, affectations laissées supprimées) ; les autres répondent 204.
     */
    function restoreMessage(kindKey, label, result) {
        let message = `« ${label} » a été restauré.`;

        if (kindKey === 'school-years' && result && typeof result === 'object') {
            const parts = [];
            if (result.restoredTerms) parts.push(`${result.restoredTerms} période(s)`);
            if (result.restoredAssignments) parts.push(`${result.restoredAssignments} affectation(s) d'enseignant`);
            if (parts.length > 0) message += ` Sont revenus avec elle : ${parts.join(' et ')}.`;
            if (result.skippedAssignments) {
                message += ` ${result.skippedAssignments} affectation(s) restent supprimées car leur enseignant, leur classe ou leur matière a été supprimé depuis.`;
            }
            message += " L'année n'est pas réactivée : activez-la si nécessaire.";
        }

        return message;
    }

    /**
     * Appelé depuis le bandeau « existe dans les éléments supprimés » d'un formulaire de création : demande au
     * panneau du type indiqué de s'ouvrir et de mettre en évidence l'élément portant ce nom.
     */
    function openFor(kindKey, match) {
        window.dispatchEvent(new CustomEvent('trash:open', { detail: { kind: kindKey, match: match || '' } }));
    }

    /** À appeler après une suppression réussie : le panneau ouvert ou compté se met à jour. */
    function notifyChanged(kindKey) {
        window.dispatchEvent(new CustomEvent('trash:changed', { detail: { kind: kindKey } }));
    }

    function normalize(value) {
        return String(value || '').trim().toLowerCase();
    }

    window.softDeleteTrash = { KINDS, formatDate, formatDay, restoreMessage, openFor, notifyChanged, normalize };

    document.addEventListener('alpine:init', () => {
        Alpine.data('trashPanel', (kindKey) => ({
            kindKey,
            kind: KINDS[kindKey],
            items: [],
            open: false,
            loading: false,
            loaded: false,
            forbidden: false,
            error: '',
            notice: '',
            restoringId: null,
            highlight: '',

            init() {
                this.load();

                window.addEventListener('trash:changed', (event) => {
                    if (!event.detail || event.detail.kind === this.kindKey) this.load();
                });

                window.addEventListener('trash:open', (event) => {
                    if (!event.detail || event.detail.kind !== this.kindKey) return;
                    this.highlight = event.detail.match || '';
                    this.open = true;
                    this.notice = '';
                    this.load().then(() => {
                        if (this.$el && this.$el.scrollIntoView) this.$el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    });
                });
            },

            get count() {
                return this.items.length;
            },

            // Masqué tant qu'il n'y a rien à restaurer ET que l'utilisateur ne l'a pas ouvert depuis un conflit.
            get visible() {
                return !this.forbidden && this.loaded && (this.count > 0 || this.open);
            },

            labelOf(item) {
                return this.kind.label(item);
            },

            detailOf(item) {
                return this.kind.detail(item);
            },

            deletedOn(item) {
                return formatDate(item.deletedAt);
            },

            isHighlighted(item) {
                return this.highlight !== '' && normalize(this.labelOf(item)) === normalize(this.highlight);
            },

            toggle() {
                this.open = !this.open;
                if (!this.open) {
                    this.highlight = '';
                    this.notice = '';
                    this.error = '';
                }
            },

            async load() {
                this.loading = true;
                try {
                    this.items = await window.api.get(this.kind.list) || [];
                    this.error = '';
                } catch (err) {
                    // 403 : le rôle ne gère pas cet élément (ex. Finance sans délégation) → pas de panneau, pas de bruit.
                    if (err && (err.status === 403 || err.handledGlobally)) {
                        this.forbidden = true;
                    } else {
                        this.error = window.api.toMessage(err, 'Impossible de charger les éléments supprimés.');
                    }
                } finally {
                    this.loading = false;
                    this.loaded = true;
                }
            },

            async restore(item) {
                if (this.restoringId) return;

                this.restoringId = item.id;
                this.error = '';
                this.notice = '';
                const label = this.labelOf(item);

                try {
                    const result = await window.api.post(this.kind.restore(item.id));
                    this.items = this.items.filter((i) => i.id !== item.id);
                    this.notice = restoreMessage(this.kindKey, label, result);
                    if (window.toast && window.toast.success) window.toast.success(`« ${label} » restauré.`);
                    window.dispatchEvent(new CustomEvent('trash:restored', { detail: { kind: this.kindKey, id: item.id, result } }));
                } catch (err) {
                    // 409 ACTIVE_ENTITY_CONFLICT, PARENT_ENTITY_ARCHIVED, chevauchement d'année… : le serveur explique.
                    this.error = window.api.toMessage(err, 'La restauration a échoué.');
                    if (err && err.status === 404) await this.load();
                } finally {
                    this.restoringId = null;
                }
            }
        }));
    });
})();
