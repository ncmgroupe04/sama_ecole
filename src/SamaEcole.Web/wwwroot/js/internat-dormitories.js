/**
 * Onglet « Pavillons » de l'écran Internat (/internat) — création et gestion des pavillons, chambres et lits (lot G, tâche 5).
 *
 * Consomme /api/v1/boarding/{dormitories, rooms, beds} (lot B). Après le lot B, la création de salles de type « Dortoir » n'est plus possible
 * depuis /infrastructures (spec Q1) : cet onglet est le SEUL endroit où l'on crée un pavillon.
 *
 * Droits : la lecture est ouverte au Directeur, au Secrétariat et au Surveillant ; l'écriture est réservée au Directeur et au Secrétariat. Les boutons
 * d'écriture ne sont affichés qu'à ces deux rôles — un CONFORT d'affichage : l'API répond 403 aux autres.
 *
 * Le serveur décide de tout ce qui compte (unicité des noms, capacité 1–40 lits, `RESOURCE_IN_USE` pour un élément non vide, lit occupé…) ; l'écran
 * en traduit les codes (`window.internatHelpers.errorMessage`) mais n'en refait aucune règle. Verrou optimiste : chaque ligne porte son `rowVersion`
 * (xmin), renvoyé tel quel ; un 409 recharge les données au lieu d'écraser la modification d'un autre utilisateur.
 *
 * Dépend de internat-helpers.js (logique pure, testée) et de api.js. La corbeille (restauration) est le panneau partagé `trashPanel` (trash.js).
 */
document.addEventListener('alpine:init', () => {
    const WRITE_ROLES = ['Directeur', 'Secretariat'];
    const DEFAULT_BED_COUNT = 4;

    /** Segment d'URL de l'API (`/boarding/{segment}/{id}`) et clé de corbeille (trash.js › KINDS) de chaque type supprimable. */
    const ROUTE_SEGMENT = { dormitory: 'dormitories', room: 'rooms', bed: 'beds' };
    const TRASH_KIND = { dormitory: 'dormitories', room: 'dormitory-rooms', bed: 'beds' };

    Alpine.data('dormitoriesTab', () => ({
        loading: true,
        error: '',
        notice: '',
        dormitories: [],
        detail: null,          // DormitoryDetailDto du pavillon ouvert, ou null
        detailLoading: false,
        canManage: false,
        genderChoices: window.internatHelpers.GENDER_CHOICES,

        form: { open: false, mode: 'create', id: null, rowVersion: null, values: {}, errors: {}, saving: false, error: '', archivedConflict: false },
        roomForm: { open: false, dormitory: null, values: {}, errors: {}, saving: false, error: '', archivedConflict: false },
        removal: { open: false, kind: null, item: null, saving: false, error: '' },

        async init() {
            this.canManage = WRITE_ROLES.includes(window.auth.role);
            await this.load();
        },

        helpers() { return window.internatHelpers; },

        // ------------------------------------------------------------------ Lecture

        async load() {
            this.loading = true;
            this.error = '';
            try {
                this.dormitories = await window.api.get('/boarding/dormitories');
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement des pavillons.');
            } finally {
                this.loading = false;
            }
        },

        async openDetail(dormitory) {
            this.detailLoading = true;
            this.error = '';
            try {
                this.detail = await window.api.get(`/boarding/dormitories/${dormitory.id}`);
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement du pavillon.');
            } finally {
                this.detailLoading = false;
            }
        },

        closeDetail() { this.detail = null; },

        /** Relit la liste ET le pavillon ouvert : après une écriture, ou un conflit de version. */
        async refresh() {
            const openId = this.detail && this.detail.id;
            await this.load();
            if (openId) await this.openDetail({ id: openId });
        },

        // ------------------------------------------------------------------ Pavillon : création / modification

        openCreate() {
            this.form = {
                open: true, mode: 'create', id: null, rowVersion: null, saving: false, error: '', archivedConflict: false, errors: {},
                values: { name: '', gender: '', supervisorName: '', supervisorPhone: '', notes: '', supervisorUserId: null }
            };
        },

        openEdit(dormitory) {
            this.form = {
                open: true, mode: 'edit', id: dormitory.id, rowVersion: dormitory.rowVersion, saving: false, error: '', archivedConflict: false, errors: {},
                values: {
                    name: dormitory.name, gender: dormitory.gender === 'Mixte' ? '' : dormitory.gender,
                    supervisorName: dormitory.supervisorName || '', supervisorPhone: dormitory.supervisorPhone || '',
                    notes: dormitory.notes || '', supervisorUserId: dormitory.supervisorUserId || null
                }
            };
        },

        closeForm() { this.form.open = false; },

        async saveDormitory() {
            const h = this.helpers();
            const form = this.form;

            form.errors = h.validateDormitory(form.values);
            if (Object.keys(form.errors).length > 0) return;

            form.saving = true;
            form.error = '';
            form.archivedConflict = false;
            try {
                if (form.mode === 'create') {
                    await window.api.post('/boarding/dormitories', h.buildDormitoryBody(form.values));
                } else {
                    await window.api.put(`/boarding/dormitories/${form.id}`, h.buildDormitoryBody(form.values, form.rowVersion));
                }
                this.closeForm();
                this.notice = form.mode === 'create' ? 'Pavillon créé.' : 'Pavillon modifié.';
                await this.refresh();
            } catch (err) {
                form.error = h.errorMessage(err, "Erreur lors de l'enregistrement du pavillon.");
                form.archivedConflict = !!err && err.code === 'ARCHIVED_ENTITY_EXISTS';
                if (err && err.code === 'CONCURRENCY_CONFLICT') await this.refresh();
            } finally {
                form.saving = false;
            }
        },

        /** Bouton « Voir dans les éléments supprimés » du bandeau d'erreur : ouvre la corbeille sur l'élément en doublon. */
        showArchivedDuplicate(kind, name) {
            this.closeForm();
            this.closeRoomForm();
            if (window.softDeleteTrash) window.softDeleteTrash.openFor(kind, name);
        },

        // ------------------------------------------------------------------ Chambre : création avec ses lits

        openAddRoom(dormitory) {
            this.roomForm = {
                open: true, dormitory, saving: false, error: '', archivedConflict: false, errors: {},
                values: { name: '', bedCount: DEFAULT_BED_COUNT }
            };
        },

        closeRoomForm() { this.roomForm.open = false; },

        async saveRoom() {
            const h = this.helpers();
            const form = this.roomForm;

            form.errors = h.validateRoom(form.values);
            if (Object.keys(form.errors).length > 0) return;

            form.saving = true;
            form.error = '';
            form.archivedConflict = false;
            try {
                await window.api.post('/boarding/rooms', h.buildRoomBody(form.dormitory.id, form.values.name, form.values.bedCount));
                this.closeRoomForm();
                this.notice = 'Chambre créée avec ses lits.';
                await this.refresh();
            } catch (err) {
                form.error = h.errorMessage(err, "Erreur lors de la création de la chambre.");
                form.archivedConflict = !!err && err.code === 'ARCHIVED_ENTITY_EXISTS';
                if (err && err.code === 'CONCURRENCY_CONFLICT') await this.refresh();
            } finally {
                form.saving = false;
            }
        },

        // ------------------------------------------------------------------ Lits

        async addBed(room) {
            this.error = '';
            try {
                await window.api.post('/boarding/beds', { dormitoryRoomId: room.id, bedNumber: null });
                this.notice = 'Lit ajouté.';
                await this.refresh();
            } catch (err) {
                this.error = this.helpers().errorMessage(err, "Erreur lors de l'ajout du lit.");
            }
        },

        /** Bascule Disponible ↔ Maintenance. Un lit occupé n'est jamais basculé ici : il faut d'abord libérer le pensionnaire. */
        canToggleMaintenance(bed) { return this.canManage && bed.status !== 'Occupied'; },

        async toggleMaintenance(bed) {
            if (!this.canToggleMaintenance(bed)) return;

            this.error = '';
            try {
                await window.api.put(`/boarding/beds/${bed.id}/status`, {
                    status: bed.status === 'Maintenance' ? 'Available' : 'Maintenance',
                    rowVersion: bed.rowVersion
                });
                await this.refresh();
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Erreur lors du changement de statut du lit.');
                if (err && (err.code === 'CONCURRENCY_CONFLICT' || err.code === 'RESOURCE_IN_USE')) await this.refresh();
            }
        },

        // ------------------------------------------------------------------ Suppression (logique) — restauration par la corbeille

        openRemoval(kind, item) {
            this.removal = { open: true, kind, item, saving: false, error: '' };
        },

        closeRemoval() { this.removal.open = false; },

        removalLabel() {
            const { kind, item } = this.removal;
            if (!item) return '';
            return kind === 'bed' ? `le lit n° ${item.bedNumber}` : `« ${item.name} »`;
        },

        async confirmRemoval() {
            const removal = this.removal;
            if (!removal.item) return;

            removal.saving = true;
            removal.error = '';
            try {
                await window.api.delete(`/boarding/${ROUTE_SEGMENT[removal.kind]}/${removal.item.id}?rowVersion=${removal.item.rowVersion}`);
                this.closeRemoval();
                if (removal.kind === 'dormitory') this.detail = null;
                this.notice = 'Élément supprimé. Il reste restaurable depuis « éléments supprimés ».';
                if (window.softDeleteTrash) window.softDeleteTrash.notifyChanged(TRASH_KIND[removal.kind]);
                await this.refresh();
            } catch (err) {
                removal.error = this.helpers().errorMessage(err, 'Erreur lors de la suppression.');
                if (err && err.code === 'CONCURRENCY_CONFLICT') await this.refresh();
            } finally {
                removal.saving = false;
            }
        },

        // ------------------------------------------------------------------ Présentation

        occupancyPercent(occupied, capacity) { return this.helpers().occupancyPercent(occupied, capacity); },
        occupancyColor(occupied, capacity) { return this.helpers().occupancyColor(occupied, capacity); },
        genderLabel(gender) { return this.helpers().genderLabel(gender); },
        bedStatusLabel(status) { return this.helpers().bedStatusLabel(status); }
    }));
});
