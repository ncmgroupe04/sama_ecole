/**
 * Onglet Paramètres › Notifications SMS (offre Premium).
 *
 * Les commutateurs sont enregistrés par le MÊME PUT /schools/current/settings que le reste de
 * l'écran Paramètres : ils vivent sur SchoolSettings. Le corps est donc RELU avant envoi puis
 * renvoyé complet — un PUT partiel remettrait à leurs valeurs par défaut tous les autres réglages
 * (barème, formats de matricule, délégations), que cet onglet n'affiche même pas.
 *
 * Le SOLDE est en lecture seule : il n'est pas dans le corps du PUT (voir
 * UpdateSchoolSettingsCommand), seul le Super Admin crédite.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('smsSettingsView', () => ({
        settings: { smsOnAttendanceAlert: false, smsOnDuesReminder: false, smsOnPaymentReceipt: false },
        creditBalance: null,

        entries: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        pendingCount: 0,

        isLoading: false,
        isSaving: false,
        saveError: null,
        saveSuccess: false,

        async load() {
            this.isLoading = true;
            try {
                const settings = await window.api.get('/schools/current/settings');
                this.settings.smsOnAttendanceAlert = settings.smsOnAttendanceAlert;
                this.settings.smsOnDuesReminder = settings.smsOnDuesReminder;
                this.settings.smsOnPaymentReceipt = settings.smsOnPaymentReceipt;
                this.creditBalance = settings.smsCreditBalance;

                const history = await window.api.get(`/sms/history?page=${this.page}&pageSize=${this.pageSize}`);
                this.entries = history.items;
                this.totalCount = history.totalCount;
                this.creditBalance = history.creditBalance;
                this.pendingCount = history.pendingCount;
            } catch (err) {
                // 403 FEATURE_NOT_IN_PLAN : l'école n'a pas l'option. Le bandeau d'incitation est
                // déjà affiché par featureGate — inutile d'empiler un second message d'erreur.
                if (err.code !== 'FEATURE_NOT_IN_PLAN') {
                    this.saveError = err.message || "Chargement des notifications SMS impossible.";
                }
            } finally {
                this.isLoading = false;
            }
        },

        async save() {
            this.isSaving = true;
            this.saveError = null;
            this.saveSuccess = false;

            try {
                // Relecture AVANT envoi : le PUT remplace l'intégralité des réglages, il faut donc
                // renvoyer ceux que cet onglet ne montre pas, inchangés.
                const current = await window.api.get('/schools/current/settings');

                await window.api.put('/schools/current/settings', {
                    ...current,
                    smsOnAttendanceAlert: this.settings.smsOnAttendanceAlert,
                    smsOnDuesReminder: this.settings.smsOnDuesReminder,
                    smsOnPaymentReceipt: this.settings.smsOnPaymentReceipt
                });

                this.saveSuccess = true;
            } catch (err) {
                this.saveError = err.message || "Enregistrement impossible.";
            } finally {
                this.isSaving = false;
            }
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleString('fr-FR');
        },

        triggerLabel(trigger) {
            return {
                AttendanceAlert: 'Assiduité', DuesReminder: 'Relance impayé',
                PaymentReceipt: 'Reçu de paiement', ReportCard: 'Bulletin',
                Manual: 'Envoi manuel'
            }[trigger] || trigger;
        },

        // « Envoyé » et « Livré » sont DEUX choses distinctes, et l'école doit pouvoir les
        // distinguer : « Envoyé » signifie accepté par l'opérateur, « Livré » que le téléphone du
        // parent l'a bien reçu (accusé de réception). Les confondre ferait conclure à tort qu'un
        // parent a été prévenu.
        statusLabel(status) {
            return {
                Pending: 'En attente', Sent: 'Envoyé', Delivered: 'Livré',
                Failed: 'Échec', InsufficientCredit: 'Solde épuisé'
            }[status] || status;
        },

        statusBadge(status) {
            const base = 'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ';
            return base + ({
                Pending: 'bg-slate-100 text-slate-600',
                Sent: 'bg-sky-100 text-sky-700',
                Delivered: 'bg-success-bg text-success',
                Failed: 'bg-danger-bg text-danger',
                InsufficientCredit: 'bg-amber-100 text-amber-700'
            }[status] || 'bg-slate-100 text-slate-600');
        }
    }));
});
