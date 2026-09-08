/**
 * Module Infrastructures (/infrastructures) — gestion physique des locaux : Bâtiments et leurs
 * Salles, une hiérarchie à 2 niveaux INDÉPENDANTE des classes pédagogiques (voir classrooms.js).
 * GET /buildings renvoie déjà chaque bâtiment avec ses salles imbriquées (GetBuildingsWithRoomsQuery) :
 * pas de second appel pour charger les salles d'un bâtiment donné.
 */
document.addEventListener('alpine:init', () => {
    const ROOM_TYPE_LABELS = {
        SalleDeClasse: 'Salle de classe',
        Laboratoire: 'Laboratoire',
        Bureau: 'Bureau',
        Autre: 'Autre'
    };

    Alpine.data('buildingsView', () => ({
        buildings: [],
        isLoading: false,
        error: null,
        search: '',

        // Créer, corriger ou archiver un bâtiment/une salle : réservé au Directeur et au Secrétariat
        // côté serveur (BuildingsController.ManageRoles/RoomsController.ManageRoles) — confort
        // d'affichage, la protection réelle est côté API.
        canManage: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // ---------------------------------------------------------------- Bâtiments
        isCreateBuildingOpen: false,
        isSavingBuilding: false,
        newBuilding: { name: '', description: '' },
        createBuildingErrors: {},

        editingBuilding: null, // { id, name, description, rowVersion }
        isSavingEditBuilding: false,
        editBuildingErrors: {},

        deletingBuilding: null, // { id, name, rowVersion }
        isDeletingBuilding: false,
        deleteBuildingError: null,

        // ---------------------------------------------------------------- Salles
        isCreateRoomOpen: false,
        isSavingRoom: false,
        newRoom: { name: '', capacity: 30, type: 'SalleDeClasse', buildingId: null },
        createRoomErrors: {},

        editingRoom: null, // { id, name, capacity, type, rowVersion }
        isSavingEditRoom: false,
        editRoomErrors: {},

        deletingRoom: null, // { id, name, rowVersion }
        isDeletingRoom: false,
        deleteRoomError: null,

        init() {
            // Deep-link « badge de bâtiment » : un raccourci (ici l'en-tête d'une carte bâtiment,
            // demain un emplacement de salle depuis l'Inventaire ou l'emploi du temps) renvoie vers
            // /infrastructures?q=<nom>. La grille se limite alors à ce bâtiment et à ses salles.
            const q = new URLSearchParams(window.location.search).get('q');
            if (q) this.search = q;
            this.loadBuildings();
        },

        get filteredBuildings() {
            const q = this.search.trim().toLowerCase();
            if (!q) return this.buildings;
            return this.buildings.filter((b) =>
                b.name.toLowerCase().includes(q) ||
                (b.description || '').toLowerCase().includes(q) ||
                b.rooms.some((r) => r.name.toLowerCase().includes(q)));
        },

        get totalRooms() {
            return this.buildings.reduce((sum, b) => sum + b.rooms.length, 0);
        },

        get totalCapacity() {
            return this.buildings.reduce((sum, b) => sum + b.rooms.reduce((s, r) => s + (r.capacity || 0), 0), 0);
        },

        roomTypeLabel(type) {
            return ROOM_TYPE_LABELS[type] || type;
        },

        async loadBuildings() {
            this.isLoading = true;
            this.error = null;
            try {
                this.buildings = await window.api.get('/buildings');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des bâtiments.');
            } finally {
                this.isLoading = false;
            }
        },

        // ------------------------------------------------------------ Bâtiments : création

        openCreateBuilding() {
            this.newBuilding = { name: '', description: '' };
            this.createBuildingErrors = {};
            this.isCreateBuildingOpen = true;
        },

        closeCreateBuilding() {
            this.isCreateBuildingOpen = false;
        },

        async submitCreateBuilding() {
            this.isSavingBuilding = true;
            this.createBuildingErrors = {};
            try {
                await window.api.post('/buildings', this.newBuilding);
                this.isCreateBuildingOpen = false;
                await this.loadBuildings();
            } catch (err) {
                this.createBuildingErrors = window.api.toFieldErrors(err, 'Erreur lors de la création du bâtiment.');
            } finally {
                this.isSavingBuilding = false;
            }
        },

        // ------------------------------------------------------------ Bâtiments : édition

        openEditBuilding(building) {
            this.editingBuilding = {
                id: building.id, name: building.name, description: building.description || '', rowVersion: building.rowVersion
            };
            this.editBuildingErrors = {};
        },

        closeEditBuilding() {
            this.editingBuilding = null;
            this.editBuildingErrors = {};
        },

        async submitEditBuilding() {
            if (!this.editingBuilding) return;

            this.isSavingEditBuilding = true;
            this.editBuildingErrors = {};
            try {
                await window.api.put(`/buildings/${this.editingBuilding.id}`, {
                    name: this.editingBuilding.name,
                    description: this.editingBuilding.description,
                    rowVersion: this.editingBuilding.rowVersion
                });
                this.closeEditBuilding();
                await this.loadBuildings();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editBuildingErrors = { global: "Ce bâtiment vient d'être modifié par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadBuildings();
                } else {
                    this.editBuildingErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditBuilding = false;
            }
        },

        // ------------------------------------------------------------ Bâtiments : suppression

        openDeleteBuilding(building) {
            this.deletingBuilding = { id: building.id, name: building.name, rowVersion: building.rowVersion };
            this.deleteBuildingError = null;
        },

        closeDeleteBuilding() {
            this.deletingBuilding = null;
            this.deleteBuildingError = null;
        },

        async confirmDeleteBuilding() {
            if (!this.deletingBuilding) return;

            this.isDeletingBuilding = true;
            this.deleteBuildingError = null;
            try {
                await window.api.delete(`/buildings/${this.deletingBuilding.id}?rowVersion=${this.deletingBuilding.rowVersion}`);
                this.deletingBuilding = null;
                await this.loadBuildings();
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    this.deleteBuildingError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteBuildingError = "Ce bâtiment vient d'être modifié par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.loadBuildings();
                } else {
                    this.deleteBuildingError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingBuilding = false;
            }
        },

        // ------------------------------------------------------------ Salles : création

        openCreateRoom(building) {
            this.newRoom = { name: '', capacity: 30, type: 'SalleDeClasse', buildingId: building.id };
            this.createRoomErrors = {};
            this.isCreateRoomOpen = true;
        },

        closeCreateRoom() {
            this.isCreateRoomOpen = false;
        },

        async submitCreateRoom() {
            this.isSavingRoom = true;
            this.createRoomErrors = {};
            try {
                await window.api.post('/rooms', this.newRoom);
                this.isCreateRoomOpen = false;
                await this.loadBuildings();
            } catch (err) {
                this.createRoomErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la salle.');
            } finally {
                this.isSavingRoom = false;
            }
        },

        // ------------------------------------------------------------ Salles : édition

        openEditRoom(room) {
            this.editingRoom = {
                id: room.id, name: room.name, capacity: room.capacity, type: room.type, rowVersion: room.rowVersion
            };
            this.editRoomErrors = {};
        },

        closeEditRoom() {
            this.editingRoom = null;
            this.editRoomErrors = {};
        },

        async submitEditRoom() {
            if (!this.editingRoom) return;

            this.isSavingEditRoom = true;
            this.editRoomErrors = {};
            try {
                await window.api.put(`/rooms/${this.editingRoom.id}`, {
                    name: this.editingRoom.name,
                    capacity: this.editingRoom.capacity,
                    type: this.editingRoom.type,
                    rowVersion: this.editingRoom.rowVersion
                });
                this.closeEditRoom();
                await this.loadBuildings();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editRoomErrors = { global: "Cette salle vient d'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadBuildings();
                } else {
                    this.editRoomErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditRoom = false;
            }
        },

        // ------------------------------------------------------------ Salles : suppression

        openDeleteRoom(room) {
            this.deletingRoom = { id: room.id, name: room.name, rowVersion: room.rowVersion };
            this.deleteRoomError = null;
        },

        closeDeleteRoom() {
            this.deletingRoom = null;
            this.deleteRoomError = null;
        },

        async confirmDeleteRoom() {
            if (!this.deletingRoom) return;

            this.isDeletingRoom = true;
            this.deleteRoomError = null;
            try {
                await window.api.delete(`/rooms/${this.deletingRoom.id}?rowVersion=${this.deletingRoom.rowVersion}`);
                this.deletingRoom = null;
                await this.loadBuildings();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteRoomError = "Cette salle vient d'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.loadBuildings();
                } else {
                    this.deleteRoomError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingRoom = false;
            }
        }
    }));
});
