/**
 * Configuration des sessions de pointage (Directeur) — modale de l'onglet « Pointage » de /internat. Composant Alpine
 * `attendanceSessionsConfig`, ouvert par l'événement `open-attendance-sessions` et qui annonce chaque changement par
 * `attendance-sessions-changed` (l'onglet relit alors ses sessions).
 *
 * La Direction crée, nomme, active et ordonne les sessions : « Appel de nuit », « Déjeuner », « Appel de la Halqa »… Rien n'est imposé. Les modèles
 * (GET /boarding/attendance/session-templates) préremplissent le formulaire ; la Direction les modifie ou les ignore.
 *
 * Données : GET /boarding/attendance/sessions (toutes), GET /boarding/attendance/session-templates, POST/PUT /boarding/attendance/sessions[/{id}],
 * PUT /boarding/attendance/sessions/order (la liste COMPLÈTE dans l'ordre voulu), DELETE /boarding/attendance/sessions/{id}.
 *
 * Règles reflétées (le serveur reste le juge) : un nom par école (casse ignorée) ; au moins un jour ; l'audience ne change plus dès qu'un pointage
 * existe ; une session déjà pointée ne se supprime pas (on la désactive).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('attendanceSessionsConfig', () => ({
        isOpen: false,
        loading: false,
        error: '',
        notice: '',
        sessions: [],
        templates: [],
        busy: false,
        form: { open: false, id: null, hasRecords: false, name: '', audience: 'Interns', days: [1, 2, 3, 4, 5, 6, 7], timeLabel: '', isActive: true, error: '', saving: false },
        removal: { open: false, session: null, error: '', deleting: false },

        helpers() { return window.internatHelpers; },
        logic() { return window.attendanceLogic; },

        get audienceChoices() { return this.logic().AUDIENCES.map((a) => ({ value: a.value, label: a.label })); },
        get dayChoices() { return this.logic().DAYS; },
        /** L'audience d'une session qui porte déjà des pointages est verrouillée (le serveur refuserait). */
        get audienceLocked() { return !!this.form.id && this.form.hasRecords; },
        /** Les modèles dont le nom est déjà pris ne sont plus proposés : un nom par école. */
        get availableTemplates() {
            const taken = new Set(this.sessions.map((s) => s.name.trim().toLowerCase()));
            return this.templates.filter((t) => !taken.has(t.name.trim().toLowerCase()));
        },

        async open() {
            this.isOpen = true;
            this.notice = '';
            await this.reload();
        },

        close() {
            this.isOpen = false;
            this.form.open = false;
            this.removal.open = false;
        },

        async reload() {
            this.loading = true;
            this.error = '';
            try {
                const [sessions, templates] = await Promise.all([
                    window.api.get('/boarding/attendance/sessions'),
                    window.api.get('/boarding/attendance/session-templates')
                ]);
                this.sessions = Array.isArray(sessions) ? sessions : [];
                this.templates = Array.isArray(templates) ? templates : [];
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Les sessions n\'ont pas pu être chargées.');
            } finally {
                this.loading = false;
            }
        },

        announce() {
            window.dispatchEvent(new CustomEvent('attendance-sessions-changed'));
        },

        audienceLabel(session) { return this.logic().audienceLabel(session.audience); },
        daysSummary(session) { return this.logic().daysSummary(session.days); },

        // ------------------------------------------------------------------ Formulaire

        newSession() {
            this.form = { open: true, id: null, hasRecords: false, name: '', audience: 'Interns', days: [1, 2, 3, 4, 5, 6, 7], timeLabel: '', isActive: true, error: '', saving: false };
        },

        /** Préremplit le formulaire avec un modèle : la Direction peut tout modifier avant d'enregistrer. */
        useTemplate(template) {
            this.form = {
                open: true, id: null, hasRecords: false, name: template.name, audience: template.audience, days: [1, 2, 3, 4, 5, 6, 7],
                timeLabel: template.timeLabel || '', isActive: true, error: '', saving: false
            };
        },

        editSession(session) {
            this.form = {
                open: true, id: session.id, hasRecords: !!session.hasRecords, name: session.name, audience: session.audience,
                days: [...session.days], timeLabel: session.timeLabel || '', isActive: !!session.isActive, error: '', saving: false
            };
        },

        closeForm() {
            this.form.open = false;
        },

        toggleDay(day) {
            const days = this.form.days.includes(day) ? this.form.days.filter((d) => d !== day) : [...this.form.days, day];
            this.form.days = days.sort((a, b) => a - b);
        },

        bodyOf(form) {
            return {
                name: form.name.trim(),
                audience: form.audience,
                days: [...form.days].sort((a, b) => a - b),
                timeLabel: form.timeLabel && form.timeLabel.trim() !== '' ? form.timeLabel.trim() : null,
                isActive: !!form.isActive
            };
        },

        async saveForm() {
            if (this.form.saving) return;
            const problem = this.logic().validateSession(this.form);
            if (problem) {
                this.form.error = problem;
                return;
            }

            this.form.saving = true;
            this.form.error = '';
            try {
                const body = this.bodyOf(this.form);
                if (this.form.id) await window.api.put(`/boarding/attendance/sessions/${this.form.id}`, body);
                else await window.api.post('/boarding/attendance/sessions', body);

                this.form.open = false;
                this.notice = this.form.id ? 'Session modifiée.' : 'Session créée.';
                await this.reload();
                this.announce();
            } catch (err) {
                this.form.error = this.helpers().errorMessage(err, 'La session n\'a pas pu être enregistrée.');
            } finally {
                this.form.saving = false;
            }
        },

        // ------------------------------------------------------------------ Activation, ordre, suppression

        /** Active ou désactive une session sans ouvrir le formulaire. */
        async toggleActive(session) {
            if (this.busy) return;
            this.busy = true;
            this.error = '';
            try {
                await window.api.put(`/boarding/attendance/sessions/${session.id}`, this.bodyOf({
                    name: session.name, audience: session.audience, days: session.days, timeLabel: session.timeLabel, isActive: !session.isActive
                }));
                await this.reload();
                this.announce();
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'La session n\'a pas pu être modifiée.');
            } finally {
                this.busy = false;
            }
        },

        /** Ordre des identifiants après avoir déplacé la session de `delta` places (−1 vers le haut, +1 vers le bas). */
        reordered(session, delta) {
            const ids = this.sessions.map((s) => s.id);
            const from = ids.indexOf(session.id);
            const to = from + delta;
            if (from < 0 || to < 0 || to >= ids.length) return null;
            ids.splice(to, 0, ids.splice(from, 1)[0]);
            return ids;
        },

        async move(session, delta) {
            const orderedIds = this.reordered(session, delta);
            if (!orderedIds || this.busy) return;
            this.busy = true;
            this.error = '';
            try {
                await window.api.put('/boarding/attendance/sessions/order', { orderedIds });
                await this.reload();
                this.announce();
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'L\'ordre n\'a pas pu être enregistré.');
                await this.reload();
            } finally {
                this.busy = false;
            }
        },

        askDelete(session) {
            this.removal = { open: true, session, error: '', deleting: false };
        },

        cancelDelete() {
            this.removal.open = false;
        },

        async confirmDelete() {
            if (!this.removal.session || this.removal.deleting) return;
            this.removal.deleting = true;
            this.removal.error = '';
            try {
                await window.api.delete(`/boarding/attendance/sessions/${this.removal.session.id}`);
                this.removal.open = false;
                this.notice = 'Session supprimée.';
                await this.reload();
                this.announce();
            } catch (err) {
                // Une session déjà pointée se désactive : le message du serveur le dit, on le reformule pour le contexte.
                this.removal.error = err && err.status === 409
                    ? 'Des pointages existent pour cette session : désactivez-la plutôt que de la supprimer.'
                    : this.helpers().errorMessage(err, 'La session n\'a pas pu être supprimée.');
            } finally {
                this.removal.deleting = false;
            }
        }
    }));
});
