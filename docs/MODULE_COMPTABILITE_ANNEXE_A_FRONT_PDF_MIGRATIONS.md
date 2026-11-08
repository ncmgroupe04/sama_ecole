# Annexe A — Module Comptabilité / Finance : écrans, JavaScript, documents PDF/Excel, migrations

> Complément de [`MODULE_COMPTABILITE.md`](MODULE_COMPTABILITE.md). Tout le code ci-dessous est repris **ligne pour ligne** des fichiers du dépôt `ncmgroupe04/sama_ecole` (blocs générés automatiquement ; aucune omission).

## Sommaire
* [A.1 Pages Razor (6 écrans)](#a1-pages-razor-6-écrans)
* [A.2 JavaScript (Alpine.js)](#a2-javascript-alpinejs)
* [A.3 Composants Razor transverses (reçu A5)](#a3-composants-razor-transverses)
* [A.4 Documents PDF (QuestPDF)](#a4-documents-pdf-questpdf)
* [A.5 Migrations SQL / EF (RLS, GRANT, contraintes)](#a5-migrations-ef-core--sql-rls-grant-contraintes)

**Conventions front à respecter pour reproduire les écrans :**
* Alpine.js : un composant par écran (`Alpine.data('caisseView', …)`), `x-data` sur la vue, `init()` charge les données.
* Client HTTP `api` (`wwwroot/js/api.js`, chargé par `_Layout`) : préfixe `/api/v1`, jeton JWT, normalisation des erreurs (`code`, `message`, `details`) ; les composants lisent `error.code` pour réagir aux 409 métier.
* Résilience réseau (`network-guard.js`) : `window.networkGuard.newIdempotencyKey()` fournit la **clé d'idempotence** envoyée avec chaque encaissement ; un retry après coupure rejoue la même clé.
* Les montants sont formatés en FCFA, sans décimale, séparateur de milliers « espace ».
* Les politiques côté serveur font foi : l'UI ne fait que masquer/désactiver (jamais autoriser).

## A.1 Pages Razor (6 écrans)

### A.1.1 Caisse (`/caisse`)

#### `src/SamaEcole.Web/Views/Caisse/Index.cshtml`

````html
@{
 ViewData["Title"] = "Caisse";
 var paymentMethodOptions = new List<SelectFieldOption> {
 new("Cash", "Espèces"), new("Cheque", "Chèque"), new("Transfer", "Virement"),
 new("MobileMoney", "Mobile Money (Wave / Orange Money)")
 };
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
 <script src="~/js/payment-cancel.js" asp-append-version="true"></script>
 <script src="~/js/caisse.js" asp-append-version="true"></script>
}

@* Impression du seul reçu : même convention que /inscriptions (docs/design-references/README.md §1). *@
<style>
    /* A5 PAYSAGE, marge 10 mm — mêmes cotes que le PDF QuestPDF (PageSizes.A5.Landscape() +
       Margin(10, Unit.Millimetre)) : zone utile de 190 × 128 mm de part et d'autre.
       À l'écran la marge est un padding de la feuille, à l'impression elle est portée par @@page.
       Le contenu occupe donc EXACTEMENT la même boîte dans les deux cas, et le débordement se voit
       avant d'imprimer — l'indicateur de tenue en page le mesure et l'affiche. */
    .receipt-a5 {
        width: 210mm;
        height: 148mm;
        padding: 10mm;
        font-family: 'Inter', 'Plus Jakarta Sans', system-ui, -apple-system, sans-serif;
        display: flex;
        flex-direction: column;
        /* Aucun bloc ne se laisse comprimer : un dépassement doit être VISIBLE et mesurable,
           jamais absorbé par un écrasement silencieux du texte. */
        /* Filet bas appliqué À L'IMPRESSION seulement (voir @@media print plus bas).
           Déclaré ICI parce que checkReceiptFit() le RELIT sur l'élément : l'indicateur de
           tenue en page mesure la feuille À L'ÉCRAN, où ce filet n'existe pas encore, et
           surestimerait donc de 4 mm la place réellement imprimable. Une seule source de
           vérité : changer la valeur ici corrige du même coup le CSS et l'indicateur. */
        --receipt-print-bottom-inset: 4mm;
        --receipt-tabular: tabular-nums;
        font-variant-numeric: var(--receipt-tabular);
        -webkit-print-color-adjust: exact;
                print-color-adjust: exact;
    }

    .receipt-a5 > * { flex-shrink: 0; }

    @@page {
        size: A5 landscape;
        margin: 10mm;
    }

    @@media print {
        /* L'application entière disparaît, seule la pièce reste. visibility (et non display) préserve
           la mise en page des ancêtres, sans quoi le reçu remonterait dans un conteneur effondré. */
        body * { visibility: hidden !important; }
        .no-print, button, .btn, .nav-bar, header, nav, aside, footer { display: none !important; }

        html, body { margin: 0 !important; padding: 0 !important; height: auto !important; background: #fff !important; }

        #receipt-printable, #receipt-printable * { visibility: visible !important; }

        #receipt-printable {
            position: absolute !important; left: 0 !important; top: 0 !important;
            /* La marge est portée par @@page : la feuille imprimée est la zone utile seule. */
            width: 190mm !important;
            /* Les trois bornes à 128 mm : sans `min-height`, la classe utilitaire `min-h-[148mm]`
               de la feuille l'emporte en impression et le bloc de signatures (poussé en bas par
               `justify-between`) déborde de ~20 mm sur une 2e page. */
            min-height: 128mm !important;
            height: 128mm !important;
            max-height: 128mm !important;
            /* Filet bas de 4 mm : le pied (signatures + mention) ne touche plus le bord de la
               feuille. Pris sur la zone utile, sans risque : le contenu ne la remplit pas.
               Aligné sur /inscriptions, qui portait déjà ce filet — les deux reçus A5 partagent la
               même feuille et le même pied signé, ils doivent s'imprimer à l'identique. */
            margin: 0 !important; padding: 0 0 var(--receipt-print-bottom-inset) !important;
            box-shadow: none !important; border: 0 !important; border-radius: 0 !important;
            /* Dernier garde-fou : rien ne peut déclencher une 2e page. L'indicateur de tenue en page
               signale le cas À L'ÉCRAN avant l'impression, pour qu'il soit corrigé et non subi. */
            overflow: hidden !important;
            page-break-inside: avoid !important; break-inside: avoid !important;
            page-break-after: avoid !important; page-break-before: avoid !important;
        }

        #receipt-printable * { page-break-inside: avoid !important; break-inside: avoid !important; }
    }
</style>

<div x-data="caisseView()">
 <!-- Titre -->
 <div>
 <h1 class="page-title">Caisse</h1>
 <p class="page-subtitle">
 Recherchez un élève pour consulter son solde et encaisser un versement sur son inscription.
 </p>
 </div>

 <!-- Rôle non autorisé : le service Secrétariat compose le montant dû, il n'encaisse pas (règle #4). -->
 <div x-show="!canRecordPayment" x-cloak class="mt-6 card py-12 text-center">
 <p class="text-slate-500 font-medium">Seuls le Directeur, le Secrétariat et le service Finance peuvent encaisser un paiement.</p>
 </div>

 <div x-show="canRecordPayment" x-cloak>

 <!-- ============================= SESSION DE CAISSE (Volume 1 §15.1) =============================
      Aucun encaissement n'est possible côté serveur hors d'une session ouverte (RecordPaymentCommandHandler,
      422 sinon) : la recherche d'élève et le formulaire restent masqués tant qu'elle ne l'est pas. -->
 <div class="mt-6">
     <div x-show="!isLoadingSession && !currentSession" x-cloak class="card p-6 border-l-4 border-warning">
         <h2 class="text-sm font-semibold text-slate-900">Aucune session de caisse ouverte</h2>
         <p class="text-sm text-slate-500 mt-1">Déclarez le fonds de caisse initial pour commencer les encaissements d'aujourd'hui.</p>
         <form x-on:submit.prevent="submitOpenSession()" novalidate class="mt-4 flex flex-wrap items-end gap-3">
             <div>
                 <label for="open-session-balance" class="form-label">Fonds de caisse initial (FCFA)</label>
                 <input id="open-session-balance" type="number" min="0" x-model="openSessionForm.openingBalance" class="input-field mt-1 w-48">
             </div>
             <button type="submit" :disabled="isOpeningSession" class="btn-modal-primary group">
                 <span x-show="!isOpeningSession">Ouvrir la session de caisse</span>
                 <span x-show="isOpeningSession">Ouverture…</span>
             </button>
         </form>
         <p x-show="openSessionError" x-cloak class="field-error mt-2" x-text="openSessionError"></p>
     </div>

     <div x-show="currentSession" x-cloak>
         <div class="flex flex-wrap items-center justify-between gap-3 mb-3">
             <p class="text-sm text-slate-500">
                 <span class="font-semibold text-success">Session de caisse ouverte</span>
                 depuis <span x-text="currentSession ? formatTime(currentSession.openedAt) : ''"></span>
             </p>
             <button type="button" x-on:click="openCloseSessionModal()" class="btn-modal-secondary group shrink-0">
                 Clôturer la caisse
             </button>
         </div>
         <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
             <stat-card label="Fonds de caisse initial" icon="wallet" accent="slate">
                 <span x-text="formatMoney(currentSession ? currentSession.openingBalance : 0)"></span>
             </stat-card>
             <stat-card label="Encaissé aujourd'hui" icon="wallet" accent="primary" emphasis>
                 <span x-text="formatMoney(currentSession ? currentSession.totalCollected : 0)"></span>
             </stat-card>
             <stat-card label="Versements" icon="checkmark-circle" accent="slate">
                 <span x-text="currentSession ? currentSession.paymentsCount : 0"></span>
             </stat-card>
         </div>
     </div>
 </div>

 <!-- ============================= RECHERCHE + SOLDE + FORMULAIRE ============================= -->
 <div x-show="!showReceipt && currentSession" x-cloak class="mt-6 grid grid-cols-1 lg:grid-cols-3 gap-6">

 <!-- Colonne recherche élève -->
 <div class="lg:col-span-1 card p-6">
 <label for="caisse-student-search" class="form-label">Rechercher un élève</label>
 <input id="caisse-student-search" type="text" x-model="studentSearch" placeholder="Nom ou matricule… (2 caractères min.)"
 x-on:input.debounce.300ms="searchStudents()" class="input-field">

 <div x-show="studentSearchError" x-cloak class="mt-2 rounded-xl border-l-4 border-danger bg-danger-bg p-3 text-sm text-danger" x-text="studentSearchError"></div>

 <div class="mt-2 border border-slate-200 rounded-xl divide-y divide-slate-100 max-h-96 overflow-y-auto">
 <template x-for="s in students" :key="s.id">
 <button type="button" x-on:click="selectStudent(s)"
 class="w-full text-left px-3 py-2 hover:bg-slate-50 transition-colors duration-150"
 :class="selectedStudent && selectedStudent.id === s.id ? 'bg-primary-50' : ''">
 <span class="flex items-center justify-between gap-2">
 <span class="text-sm text-slate-900" x-text="s.fullName"></span>
 <span class="text-xs font-mono text-slate-500 shrink-0" x-text="s.matricule"></span>
 </span>
 @* Signal visible SANS ouvrir la fiche : ce que le tuteur vient régler à la caisse, muni de la
 fiche du secrétariat, c'est le MONTANT ÉCHU (frais d'inscription + uniforme + 1er mois), JAMAIS
 le cumul annuel — qui effraie sans informer. `dueNowTotal` est calculé serveur à partir des
 échéances de l'engagement initial. amountPaid = 0 => l'élève tout juste inscrit n'a encore rien
 versé : c'est le cas qui doit sauter aux yeux. *@
 <span x-show="s.hasActiveEnrollment && s.remainingBalance > 0 && s.dueNowTotal > 0" x-cloak
 class="mt-1 inline-flex items-center gap-1"
 :class="s.amountPaid === 0 ? 'status-badge-warning' : 'status-badge-neutral'">
 <span x-show="s.amountPaid === 0" x-cloak>Aucun versement · </span>Montant échu : <span x-text="formatMoney(s.dueNowTotal)"></span>
 </span>
 <span x-show="s.hasActiveEnrollment && s.remainingBalance > 0 && s.dueNowTotal <= 0" x-cloak
 class="mt-1 inline-flex items-center gap-1 status-badge-neutral">
 Frais du jour réglés · reste <span x-text="formatMoney(s.remainingBalance)"></span>
 </span>
 <span x-show="s.hasActiveEnrollment && s.totalDue > 0 && s.remainingBalance <= 0" x-cloak
 class="mt-1 inline-flex items-center gap-1 status-badge-success">
 Soldé
 </span>
 <span x-show="!s.hasActiveEnrollment" x-cloak
 class="mt-1 inline-flex items-center gap-1 status-badge-neutral">
 Aucune inscription active
 </span>
 </button>
 </template>
 <div x-show="isSearchingStudents" x-cloak class="px-3 py-4 text-sm text-slate-400 text-center">Recherche…</div>
 <div x-show="!isSearchingStudents && studentsLoaded && students.length === 0" x-cloak class="px-3 py-4 text-sm text-slate-400 text-center">
 Aucun élève ne correspond.
 </div>
 <div x-show="!isSearchingStudents && !studentsLoaded && !studentSearchError && studentSearch.trim().length === 0" x-cloak class="px-3 py-4 text-sm text-slate-400 text-center">
 Tapez un nom ou un matricule pour rechercher.
 </div>
 </div>
 </div>

 <!-- Colonne solde + encaissement -->
 <div class="lg:col-span-2">

 <!-- Aucun élève sélectionné -->
 <div x-show="!selectedStudent" x-cloak class="card p-12 text-center">
 <p class="text-sm text-slate-400">Sélectionnez un élève pour afficher son solde.</p>
 </div>

 <!-- Chargement du solde -->
 <div x-show="selectedStudent && isLoadingBalance" x-cloak class="card p-12 flex items-center justify-center">
 <svg aria-hidden="true" class="animate-spin h-8 w-8 text-primary" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>
 </div>

 <!-- Élève sans inscription active : rien à encaisser -->
 <div x-show="selectedStudent && !isLoadingBalance && balanceError" x-cloak class="rounded-xl border-l-4 border-warning bg-warning-bg p-6">
 <p class="text-sm font-medium text-warning">Aucune inscription active pour cet élève.</p>
 <p class="text-xs text-warning mt-1">Vérifiez qu'il est inscrit pour l'année scolaire en cours (<a href="/inscriptions" class="underline font-medium">Inscriptions</a>).</p>
 </div>

 <!-- Solde + formulaire -->
 <div x-show="selectedStudent && !isLoadingBalance && balance" x-cloak class="card p-6 space-y-6">

 <!-- Récapitulatif -->
 <div>
 <div class="flex items-center justify-between">
 <h2 class="text-sm font-semibold text-slate-900 uppercase tracking-wider">Solde de l'inscription</h2>
 <badge x-show="balance && balance.status === 'Paid'" x-cloak variant="success" dot>Soldé</badge>
 <badge x-show="balance && balance.status === 'Partial'" x-cloak variant="warning" dot>Solde partiel</badge>
 </div>
 <p class="text-sm text-slate-500 mt-1">
 <span x-text="balance ? balance.studentFullName : ''"></span> ·
 <span x-text="balance ? balance.matricule : ''"></span> ·
 <span x-text="balance ? balance.classroomName : ''"></span> ·
 <span x-text="balance ? balance.schoolYearLabel : ''"></span>
 </p>

 <dl class="mt-4 grid grid-cols-1 gap-4 text-center border-t border-slate-100 pt-4 sm:grid-cols-3">
 <div>
 <dt class="text-xs text-slate-500">Total dû</dt>
 <dd class="text-base font-semibold text-slate-900" x-text="formatMoney(balance ? balance.totalDue : 0)"></dd>
 </div>
 <div>
 <dt class="text-xs text-slate-500">Déjà réglé</dt>
 <dd class="text-base font-semibold text-slate-900" x-text="formatMoney(balance ? balance.amountPaid : 0)"></dd>
 </div>
 <div>
 <dt class="text-xs text-slate-500">Reste à payer</dt>
 <dd class="text-base font-bold text-primary" x-text="formatMoney(balance ? balance.remainingBalance : 0)"></dd>
 </div>
 </dl>

 @* Rappel bien visible : après un [Annuler] dans le guichet rapide, l'élève reste
    sélectionné — ce bouton rouvre la pop-up directement, cases contiguës déjà cochées,
    sans re-rechercher l'élève. Masqué quand la pop-up est déjà ouverte, ou qu'il n'y a
    plus rien d'échu à encaisser. *@
 <button type="button" x-show="dueNowTotal > 0 && !quickPay.open" x-cloak
 x-on:click="openQuickPay()"
 class="mt-4 w-full inline-flex items-center justify-center gap-2 rounded-xl bg-primary px-4 py-3 text-sm font-semibold text-white shadow-sm hover:bg-primary-600 focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-1 transition-colors">
 <span aria-hidden="true" class="text-base leading-none">⚡</span>
 Encaisser les frais dus (<span x-text="formatMoney(dueNowTotal)"></span>)
 </button>
 </div>

 <!-- Inscription déjà soldée : pas de formulaire -->
 <div x-show="balance && balance.remainingBalance <= 0" x-cloak class="rounded-xl border-l-4 border-success bg-success-bg p-6">
 <p class="text-sm font-medium text-success">Cette inscription est intégralement réglée.</p>
 </div>

 <!-- Conflit de concurrence : un autre caissier a encaissé entre-temps -->
 <div x-show="conflictError" x-cloak class="rounded-xl border-l-4 border-danger bg-danger-bg p-6">
 <p class="text-sm font-medium text-danger">Le solde a changé entre-temps (un autre encaissement a eu lieu).</p>
 <button type="button" x-on:click="reloadBalance()" class="mt-2 text-sm font-medium text-danger underline">Recharger le solde</button>
 </div>

 <!-- Échéancier détaillé (Frais d'inscription + mensualités) -->
 <div x-show="balance && balance.installments && balance.installments.length > 0" x-cloak class="border-t border-slate-100 pt-5 space-y-4">
     <h3 class="text-xs font-semibold text-slate-900 uppercase tracking-wider">Échéancier détaillé</h3>
     @* overflow-x-auto (et non -hidden) : 7 colonnes, elles ne tiennent pas sur un téléphone —
        défilement horizontal assumé plutôt que colonnes tronquées. min-w garde les colonnes lisibles. *@
     <div class="rounded-2xl border border-slate-100 shadow-sm overflow-x-auto">
         <table class="w-full min-w-[680px] text-xs text-left border-collapse">
             <thead>
                 <tr class="table-head">
                     <th class="py-2.5 px-3">Désignation</th>
                     <th class="py-2.5 px-3">Échéance</th>
                     <th class="py-2.5 px-3 text-right">Montant</th>
                     <th class="py-2.5 px-3 text-right">Payé</th>
                     <th class="py-2.5 px-3 text-right">Reste</th>
                     <th class="py-2.5 px-3 text-center">Statut</th>
                     <th class="py-2.5 px-3 text-right">Action</th>
                 </tr>
             </thead>
             <tbody class="divide-y divide-slate-100">
                 <template x-for="inst in (balance?.installments ?? [])" :key="inst.id">
                     <tr class="hover:bg-slate-50/60 transition-colors">
                         <td class="py-2.5 px-3 font-medium text-slate-800" x-text="inst.designation"></td>
                         <td class="py-2.5 px-3 text-slate-500" x-text="formatDateOnly(inst.dueDate)"></td>
                         <td class="py-2.5 px-3 text-right font-medium text-slate-900 amount" x-text="formatMoney(inst.amount)"></td>
                         <td class="py-2.5 px-3 text-right text-slate-600 amount" x-text="formatMoney(inst.amountPaid)"></td>
                         <td class="py-2.5 px-3 text-right font-bold text-slate-900 amount" x-text="formatMoney(inst.remainingDue)"></td>
                         <td class="py-2.5 px-3 text-center">
                             <span class="status-badge" :class="statusBadgeClass(inst.status)">
                                 <span class="status-dot" aria-hidden="true"></span>
                                 <span x-text="statusLabel(inst.status)"></span>
                             </span>
                         </td>
                         <td class="py-2.5 px-3 text-right">
                             <button x-show="inst.remainingDue > 0" type="button" x-on:click="payInstallment(inst)"
                                     class="text-xs font-semibold text-primary hover:underline py-1 px-2 rounded hover:bg-primary-50 transition-colors">
                                 Régler
                             </button>
                         </td>
                     </tr>
                 </template>
             </tbody>
         </table>
     </div>
 </div>

 <!-- Historique des versements antérieurs -->
 <div x-show="balance && balance.paymentsHistory && balance.paymentsHistory.length > 0" x-cloak class="border-t border-slate-100 pt-5 space-y-4">
     <h3 class="text-xs font-semibold text-slate-900 uppercase tracking-wider">Historique des versements</h3>
     <div class="rounded-2xl border border-slate-100 shadow-sm overflow-x-auto">
         <table class="w-full min-w-[560px] text-xs text-left border-collapse">
             <thead>
                 <tr class="table-head">
                     <th class="py-2 px-3">N° Reçu</th>
                     <th class="py-2 px-3">Date</th>
                     <th class="py-2 px-3">Mode</th>
                     <th class="py-2 px-3 text-right">Montant encaissé</th>
                     <th class="py-2 px-3 text-center">Document</th>
                 </tr>
             </thead>
             <tbody class="divide-y divide-slate-100">
                 <template x-for="pay in (balance?.paymentsHistory ?? [])" :key="pay.paymentId">
                     <tr class="hover:bg-slate-50/60 transition-colors">
                         <td class="py-2 px-3 font-mono font-medium text-primary" x-text="pay.receiptNumber"></td>
                         <td class="py-2 px-3 text-slate-600" x-text="formatDate(pay.paidAt)"></td>
                         <td class="py-2 px-3 text-slate-600" x-text="methodLabel(pay.method)"></td>
                         <td class="py-2 px-3 text-right font-bold text-slate-900 amount" x-text="formatMoney(pay.amount)"></td>
                         <td class="py-2 px-3 text-center whitespace-nowrap">
                             <button type="button" x-on:click.stop="previewReceipt(pay.paymentId, pay.receiptNumber)"
                                     title="Aperçu et impression du reçu PDF"
                                     class="inline-flex items-center justify-center gap-1.5 px-2.5 py-1 rounded-lg border border-primary-200 bg-primary-50/60 hover:bg-primary-100 text-primary-800 text-xs font-semibold shadow-2xs transition-all whitespace-nowrap shrink-0">
                                 <icon name="document-text" class="w-3.5 h-3.5 text-primary-600 shrink-0" />
                                 <span class="whitespace-nowrap">Aperçu / Imprimer</span>
                             </button>
                             <button type="button" x-show="canCancelPayment && isPaymentCancellable(pay)" x-cloak x-on:click.stop="openCancelPayment(pay)"
                                     title="Annuler ce paiement saisi par erreur (le reçu est conservé, marqué annulé)"
                                     class="ml-1 inline-flex items-center px-2.5 py-1 rounded-lg border border-rose-200 text-rose-600 hover:bg-rose-50 text-xs font-semibold transition-colors whitespace-nowrap shrink-0">
                                 Annuler
                             </button>
                         </td>
                     </tr>
                 </template>
             </tbody>
         </table>
     </div>
 </div>

    <!-- Bandeau de restauration de brouillon d'encaissement -->
    <div x-show="hasDraft" x-cloak class="mt-4 rounded-xl border border-amber-300 bg-amber-50 p-4 text-amber-900 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 shadow-2xs">
        <div class="flex items-center gap-3">
            <icon name="alert-circle" class="w-5 h-5 text-amber-600 shrink-0" />
            <p class="text-xs sm:text-sm">
                <strong class="font-semibold">Encaissement en attente :</strong> Une saisie de caisse non validée ou interrompue hors ligne a été retrouvée.
            </p>
        </div>
        <div class="flex items-center gap-2 shrink-0 self-end sm:self-center">
            <button type="button" x-on:click="restoreDraft()" class="px-3 py-1.5 rounded-xl bg-amber-600 hover:bg-amber-700 text-white text-xs font-semibold shadow-2xs transition-colors">
                Restaurer
            </button>
            <button type="button" x-on:click="clearDraft()" class="px-3 py-1.5 rounded-xl bg-amber-100 hover:bg-amber-200 text-amber-800 text-xs font-semibold transition-colors">
                Effacer
            </button>
        </div>
    </div>

 <!-- Formulaire d'encaissement -->
 <form x-show="balance && balance.remainingBalance > 0" x-cloak x-on:submit.prevent="submit()" novalidate class="space-y-5 border-t border-slate-100 pt-5">

 <div x-show="formErrors.global" x-cloak class="rounded-xl border-l-4 border-danger bg-danger-bg p-3 text-sm text-danger" x-text="formErrors.global"></div>

 <div class="form-grid">
 <div>
 <label for="caisse-amount" class="form-label form-label-required">Montant versé (FCFA)</label>
 <input id="caisse-amount" type="number" min="1" x-model.number="form.amount" placeholder="ex : 25000" class="input-field" :class="formErrors.amount && 'input-field-error'">
 <p x-show="formErrors.amount" x-cloak x-text="formErrors.amount" class="field-error"></p>
 @* Toutes les échéances déjà arrivées à échéance (inscription, uniforme, mensualité(s) échue(s)…)
 EN UN SEUL montant : c'est ce que le secrétariat a programmé à l'inscription, et qui doit
 partir en UN SEUL versement — donc un seul reçu — plutôt qu'un "Régler" par ligne de
 l'échéancier ci-dessus, qui fragmentait ce même montant en plusieurs reçus séparés.
 Masqué quand il vaut exactement le solde intégral (rien à distinguer des deux boutons). *@
 <button type="button" x-show="dueNowTotal > 0 && dueNowTotal < (balance ? balance.remainingBalance : 0)" x-cloak
 x-on:click="fillDueNow()" class="mt-1 block text-xs font-semibold text-primary hover:underline">
 Régler les échéances dues (<span x-text="formatMoney(dueNowTotal)"></span>)
 </button>
 <button type="button" x-on:click="fillFullBalance()" class="mt-1 block text-xs text-primary hover:underline">
 Régler le solde intégral (<span x-text="formatMoney(balance ? balance.remainingBalance : 0)"></span>)
 </button>
 </div>
 <div>
 <label for="caisse-method" class="form-label form-label-required">Moyen de paiement</label>
 <select-field id="caisse-method" model="form.method" options="paymentMethodOptions" />
 </div>
 </div>

 <div x-show="sendState === 'retrying' || sendState === 'failed'" x-cloak class="rounded-xl border-l-4 border-warning bg-warning-bg p-3 text-sm text-warning" x-text="sendStateLabel()"></div>

 <div class="pt-2 flex justify-end border-t border-slate-100">
 <button type="submit" :disabled="!canSubmit()" class="btn-modal-primary mt-4 group">
 <icon x-show="!isSubmitting" name="checkmark-circle" class="w-5 h-5" />
 <span x-text="sendState === 'retrying' ? 'En attente d\'envoi…' : (isSubmitting ? 'Encaissement…' : 'Encaisser le versement')"></span>
 </button>
 </div>
 </form>
 </div>
 </div>
 </div>

 <!-- ============================= REÇU ============================= -->
 <div x-show="showReceipt" x-cloak class="mt-6">

 <!-- Barre d'actions (non imprimée) -->
 <div class="no-print flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-4">
 <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
 <span class="flex items-center gap-2 text-success">
 <icon name="checkmark-circle" class="w-6 h-6" />
 <span class="text-sm font-medium">Paiement encaissé.</span>
 </span>

 @* Tenue en page, MESURÉE sur le rendu réel — pas une promesse. L'impression rogne un
 débordement (overflow:hidden) ; ce badge existe pour qu'il soit vu avant, et corrigé. *@
 <span x-show="receiptFitMm !== null && receiptFitMm >= -0.2" x-cloak
 class="rounded-full border border-success bg-success-bg px-2.5 py-0.5 text-xs font-medium text-success"
 x-text="'Tient sur une page · ' + (receiptFitMm ?? 0).toFixed(1) + ' mm libres'"></span>
 <span x-show="receiptFitMm !== null && receiptFitMm < -0.2" x-cloak
 class="rounded-full border border-warning bg-warning-bg px-2.5 py-0.5 text-xs font-medium text-warning"
 x-text="'Débordement : ' + Math.abs(receiptFitMm ?? 0).toFixed(1) + ' mm — le bas sera rogné à l\'impression'"></span>
 </div>
 <div class="flex flex-wrap items-center gap-2">
 <button type="button" x-on:click="closeReceipt()" class="btn-modal-secondary group">
 Retour (Fermer)
 </button>
 <button type="button" x-on:click="startNewPayment()" class="btn-modal-secondary group">
 Nouvel encaissement
 </button>
 <button type="button" x-on:click="downloadPdf()" class="btn-modal-secondary group">
 <icon name="document-text" class="w-5 h-5 text-primary-600" />
 Aperçu PDF officiel (A5)
 </button>
 <button type="button" x-on:click="printReceipt()" class="btn-modal-primary group">
 <icon name="print" class="w-5 h-5" />
 Imprimer
 </button>
 </div>
 </div>

        <!-- Document imprimable A5 PAYSAGE : sobre, moderne, aux normes officielles -->
        @* La feuille n'est MONTÉE que lorsque le reçu est chargé.
           `x-show` ne masque qu'à l'affichage : Alpine évalue quand même toutes les liaisons
           qu'il contient dès le montage de la vue, et `receipt` y vaut encore null — d'où les
           « Cannot read properties of null (reading 'receiptNumber' / 'amount') » au chargement
           à froid de /caisse. `x-if` ne rend rien tant que la condition est fausse : c'est la
           seule forme qui protège le document ENTIER, sans semer un `receipt?.` par ligne. Cette
           garde reste ICI, en clair — jamais dans <receipt-a5> (TagHelpers/ReceiptA5TagHelper.cs) :
           voir sa doc pour la raison (garde-fou NULL_DEREF_DEBT, qui lit le texte source des vues). *@
        <template x-if="receipt">
            <receipt-a5 signatory="Le Caissier">
                @* A. En-tête : logo à GAUCHE du nom (partie commune, dans receipt-a5), badges à
                   droite (propres à ce document, délimitées par des marqueurs HTML — pas des Tag
                   Helpers enfants, voir la doc de ReceiptA5TagHelper). « PAYÉ » en vert — c'est ICI,
                   et nulle part ailleurs, que le vert est légitime : le reçu de caisse est la seule
                   pièce qui atteste d'un encaissement (docs/design-references/README.md §1ter). *@
                <!--badges-->
                    <span class="rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-[9px] font-semibold uppercase tracking-wider text-emerald-700">Payé</span>
                    <span class="rounded-full border border-slate-200 bg-slate-100 px-2.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-slate-700 whitespace-nowrap">Reçu&nbsp;n°&nbsp;<span x-text="receipt.receiptNumber"></span></span>
                    <span class="rounded-full border border-slate-200 bg-slate-100 px-2.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide text-slate-700 whitespace-nowrap" x-text="formatDate(receipt.paidAt)"></span>
                <!--/badges-->

                <!-- B. Cartouche d'identité (grille 2 × 2) -->
                <div class="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 rounded-lg border border-slate-200 bg-slate-50 px-4 py-2.5">
                    <div>
                        <div class="text-[9px] font-semibold uppercase tracking-wider text-slate-500">Élève</div>
                        <div class="text-xs font-semibold text-slate-900" x-text="receipt.studentFullName"></div>
                    </div>
                    <div>
                        <div class="text-[9px] font-semibold uppercase tracking-wider text-slate-500">Matricule</div>
                        <div class="text-xs font-semibold text-slate-900 whitespace-nowrap" x-text="receipt.matricule"></div>
                    </div>
                    <div>
                        <div class="text-[9px] font-semibold uppercase tracking-wider text-slate-500">Classe &amp; année</div>
                        @* Classe passerelle : même mention qu'en PDF (ClassroomPromotion.DisplayName) —
                           l'aperçu à l'écran et la pièce imprimée ne doivent jamais dire deux choses. *@
                        <div class="text-xs font-semibold text-slate-900"
                             x-text="receipt.classroomName + (receipt.isAcceleratedClass ? ' (Cursus Accéléré Passerelle)' : '') + ' — ' + receipt.schoolYearLabel"></div>
                    </div>
                    <div>
                        <div class="text-[9px] font-semibold uppercase tracking-wider text-slate-500">Mode de règlement</div>
                        <div class="text-xs font-semibold text-slate-900" x-text="methodLabel(receipt.method)"></div>
                    </div>
                </div>

                @* C. Détail du règlement. La ventilation (une ligne par poste imputé) remplace l'unique
                   « Versement reçu ». Le dû annuel, le déjà-réglé et le solde ont DISPARU du document :
                   un reçu n'atteste que de la somme entrée en caisse ce jour-là, jamais d'une dette
                   (règle comptable non négociable, AGENTS.md règle #12 et README §1bis). Ces trois
                   valeurs restent affichées au cadre de gauche, à l'usage du caissier. *@
                <div class="mt-3">
                    <div class="text-[9px] font-bold uppercase tracking-widest text-slate-500">
                        Détail du règlement
                        <span x-show="periodHint()" x-cloak class="font-medium normal-case tracking-normal text-slate-400" x-text="periodHint()"></span>
                    </div>

                    <table class="mt-1.5 w-full text-[11px]">
                        <thead>
                            <tr class="bg-slate-100 text-slate-700 border-b border-slate-200">
                                <th class="text-left py-1 px-2 text-[9px] font-semibold uppercase tracking-wider">Désignation / Service</th>
                                <th x-show="hasVentilation()" x-cloak class="text-left py-1 px-2 text-[9px] font-semibold uppercase tracking-wider">Période / Note</th>
                                <th class="text-right py-1 px-2 text-[9px] font-semibold uppercase tracking-wider whitespace-nowrap">Montant réglé</th>
                            </tr>
                        </thead>
                        <tbody>
                            @* Forme VENTILÉE — une ligne par poste imputé. *@
                            <template x-if="hasVentilation()">
                                <template x-for="line in receipt.lines" :key="line.designation">
                                    <tr class="border-b border-slate-200">
                                        <td class="py-1 px-2 text-slate-900" x-text="line.designation"></td>
                                        @* Libellé de ligne, à défaut période du versement, à défaut CELLULE VIDE —
                                           jamais un tiret ni une période devinée. Même cascade qu'en PDF. *@
                                        <td class="py-1 px-2 text-slate-500" x-text="lineLabel(line)"></td>
                                        <td class="py-1 px-2 text-right text-slate-900 amount" x-text="formatMoney(line.amount)"></td>
                                    </tr>
                                </template>
                            </template>

                            @* Forme UNIQUE — repli quand rien n'a été ventilé, ou quand le détail saisi ne
                               totalise pas le montant encaissé : un tableau qui ne balance pas est un faux. *@
                            <template x-if="!hasVentilation()">
                                <tr class="border-b border-slate-200">
                                    <td class="py-1 px-2 text-slate-900">Versement reçu</td>
                                    <td class="py-1 px-2 text-right text-slate-900 amount" x-text="formatMoney(receipt.amount)"></td>
                                </tr>
                            </template>

                            <tr class="border-t border-slate-300">
                                <td class="pt-1.5 px-2 text-[9px] font-bold uppercase tracking-wider text-slate-700">Total versé</td>
                                <td x-show="hasVentilation()" x-cloak></td>
                                <td class="pt-1.5 px-2 text-right text-sm font-bold text-emerald-700 amount whitespace-nowrap"
                                    x-text="formatMoney(receipt.amount)"></td>
                            </tr>
                        </tbody>
                    </table>
                </div>

                <!-- D. Mention obligatoire (AGENTS.md règle #12) -->
                <p class="mt-3 text-center text-[11px] italic text-slate-600">
                    Il est demandé aux parents de garder minutieusement leur reçu après le paiement.
                </p>

                @* E. Pied de page — partie commune (case Fait à…/cachet/deux signatures) dans
                   receipt-a5 (signatory="Le Caissier" ci-dessus) ; seule cette mention varie. *@
                <!--footer-->
                    Ce reçu atteste uniquement des sommes encaissées le <span x-text="formatDate(receipt.paidAt)"></span>. Il ne constitue pas un relevé de compte.
                <!--/footer-->
            </receipt-a5>
        </template>

 </div>

 @* Clôture de la session de caisse (Volume 1 §15.2) : comptage physique obligatoire (ticket JGK-F09),
 puis récapitulatif avec accès direct au rapport — le solde théorique est figé, une session close ne
 se rouvre jamais. *@
 <div class="no-print">
 <modal-shell open="isCloseSessionModalOpen" on-close="closeCloseSessionModal()" title="Clôturer la session de caisse" size="md">
     <template x-if="!closeSessionResult">
         <form x-on:submit.prevent="confirmCloseSession()" novalidate class="space-y-4">
             <p class="text-sm text-slate-600">
                 Fonds initial : <span class="font-medium text-slate-900" x-text="formatMoney(currentSession ? currentSession.openingBalance : 0)"></span><br>
                 Encaissé sur la session (toutes méthodes) : <span class="font-medium text-slate-900" x-text="formatMoney(currentSession ? currentSession.totalCollected : 0)"></span><br>
                 Solde de fermeture théorique : <span class="font-semibold text-slate-900" x-text="formatMoney(currentSession ? (currentSession.openingBalance + currentSession.totalCollected) : 0)"></span>
             </p>

             <div>
                 <label for="close-session-actual-cash" class="form-label form-label-required">Espèces réellement comptées dans le tiroir-caisse (FCFA)</label>
                 <input id="close-session-actual-cash" type="number" min="0" required x-model="closeSessionForm.actualCashAmount" class="input-field mt-1">
                 <p class="text-xs text-slate-400 mt-1">Comptez le numéraire AVANT de saisir ce montant — les espèces attendues ne comprennent que le fonds initial et les versements en espèces, jamais les virements, chèques ou mobile money.</p>
             </div>

             <div x-show="closeSessionNeedsReason" x-cloak>
                 <label for="close-session-reason" class="form-label form-label-required">Motif de l'écart constaté</label>
                 <textarea id="close-session-reason" x-model="closeSessionForm.discrepancyReason" rows="2" placeholder="ex : fonds de caisse mal compté à l'ouverture, appoint rendu en trop…" class="input-field mt-1"></textarea>
             </div>

             <p class="text-xs text-slate-400">Une session clôturée ne peut plus être rouverte ni re-clôturée.</p>
             <div x-show="closeSessionError" x-cloak class="form-error-banner" x-text="closeSessionError"></div>
             <div class="flex items-center justify-end gap-3 pt-2">
                 <button type="button" x-on:click="closeCloseSessionModal()" class="btn-modal-secondary group">Annuler</button>
                 <button type="submit" :disabled="isClosingSession" class="btn-modal-primary group">
                     <span x-show="!isClosingSession">Clôturer</span>
                     <span x-show="isClosingSession">Clôture…</span>
                 </button>
             </div>
         </form>
     </template>
     <template x-if="closeSessionResult">
         <div class="space-y-4">
             <p class="text-sm text-success font-medium">Session clôturée.</p>
             <p class="text-sm text-slate-600 space-y-0.5">
                 <span class="block">Espèces attendues : <span class="font-medium text-slate-900" x-text="closeSessionResult ? formatMoney(closeSessionResult.expectedCashAmount) : ''"></span></span>
                 <span class="block">Espèces comptées : <span class="font-medium text-slate-900" x-text="closeSessionResult ? formatMoney(closeSessionResult.actualCashAmount) : ''"></span></span>
                 <span class="block">
                     Écart :
                     <span class="font-semibold" :class="closeSessionResult && closeSessionResult.discrepancyAmount !== 0 ? 'text-danger' : 'text-success'"
                           x-text="closeSessionResult ? formatMoney(closeSessionResult.discrepancyAmount) : ''"></span>
                 </span>
                 <span x-show="closeSessionResult && closeSessionResult.discrepancyReason" x-cloak class="block text-slate-500">Motif : <span x-text="closeSessionResult ? closeSessionResult.discrepancyReason : ''"></span></span>
             </p>
             <div class="flex items-center justify-end gap-3 pt-2">
                 <button type="button" x-on:click="closeCloseSessionModal()" class="btn-modal-secondary group">Fermer</button>
                 <button type="button" x-on:click="downloadClosingReport()" class="btn-modal-primary group">
                     Rapport de clôture (PDF)
                 </button>
             </div>
         </div>
     </template>
 </modal-shell>
 </div>

 @* ============================= GUICHET RAPIDE (pop-up d'encaissement) =============================
    S'ouvre dès la sélection d'un élève ayant un montant échu (voir selectStudent). Passage rapide au
    guichet : le caissier coche/décoche les frais dus, valide, un SEUL reçu est généré. [Annuler]
    referme sans rien encaisser et laisse le panneau détaillé (échéancier ligne à ligne, solde
    intégral, historique) accessible pour les cas particuliers. *@
 <div class="no-print">
 @* dismissible="false" : un clic à côté ou un Échap ne ferme PAS ce guichet — seuls [Annuler]
    et la croix ✕ le font, pour ne pas perdre la saisie sur une fausse manœuvre. *@
 <modal-shell open="quickPay.open" size="2xl" dismissible="false" on-close="closeQuickPay()" title="Encaissement des frais dus">
 <div class="space-y-5">

 <!-- En-tête élève -->
 <div class="rounded-lg border border-slate-200 bg-slate-50 px-4 py-3 grid grid-cols-2 gap-x-6 gap-y-2 text-sm">
 <div>
 <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Élève</div>
 <div class="font-semibold text-slate-900" x-text="balance ? balance.studentFullName : ''"></div>
 </div>
 <div>
 <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Matricule</div>
 <div class="font-semibold text-slate-900" x-text="balance ? balance.matricule : ''"></div>
 </div>
 <div>
 <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Classe</div>
 <div class="font-semibold text-slate-900" x-text="balance ? balance.classroomName : ''"></div>
 </div>
 <div>
 <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Année scolaire</div>
 <div class="font-semibold text-slate-900" x-text="balance ? balance.schoolYearLabel : ''"></div>
 </div>
 </div>

 <!-- Frais échus, une case à cocher par ligne (toutes cochées par défaut) -->
 <div>
 <h3 class="text-xs font-semibold text-slate-900 uppercase tracking-wider mb-2">Frais échus à ce jour</h3>
 <div class="rounded-xl border border-slate-100 shadow-sm overflow-x-auto">
 <table class="w-full min-w-[380px] text-sm text-left">
 <thead>
 <tr class="table-head">
 <th class="py-2 px-3 w-10"></th>
 <th class="py-2 px-3">Désignation</th>
 <th class="py-2 px-3 text-right">Montant</th>
 </tr>
 </thead>
 <tbody class="divide-y divide-slate-100">
 <template x-for="(row, idx) in quickPayRows()" :key="row.id">
 <tr class="hover:bg-slate-50/60 transition-colors">
 <td class="py-2 px-3">
 <input type="checkbox" x-model="quickPay.checked[idx]" x-on:change="onQuickRowToggle(idx)"
 :aria-label="'Régler ' + row.designation"
 class="h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary cursor-pointer" />
 </td>
 <td class="py-2 px-3 font-medium" :class="quickPay.checked[idx] ? 'text-slate-800' : 'text-slate-400 line-through'" x-text="row.designation"></td>
 <td class="py-2 px-3 text-right font-semibold amount" :class="quickPay.checked[idx] ? 'text-slate-900' : 'text-slate-400 line-through'" x-text="formatMoney(row.remainingDue)"></td>
 </tr>
 </template>
 <tr x-show="quickPayRows().length === 0" x-cloak>
 <td colspan="3" class="py-3 px-3 text-sm text-slate-400 text-center">Aucun frais échu à encaisser.</td>
 </tr>
 </tbody>
 <tfoot>
 <tr class="border-t-2 border-slate-200 bg-slate-50">
 <td></td>
 <td class="py-2.5 px-3 text-xs font-bold uppercase tracking-wider text-slate-700">Total à encaisser</td>
 <td class="py-2.5 px-3 text-right text-base font-bold text-primary amount" x-text="formatMoney(quickPayTotal())"></td>
 </tr>
 </tfoot>
 </table>
 </div>
 <p class="mt-2 text-xs text-slate-400">Décocher une ligne décoche aussi les suivantes : les frais se règlent dans l'ordre (inscription, uniforme, puis mensualités). Les lignes non cochées restent « En attente » pour un encaissement ultérieur.</p>
 </div>

 <!-- Règlement -->
 <form x-on:submit.prevent="submitQuickPay()" novalidate class="space-y-4 border-t border-slate-100 pt-4">
 <div x-show="formErrors.global" x-cloak class="rounded-xl border-l-4 border-danger bg-danger-bg p-3 text-sm text-danger" x-text="formErrors.global"></div>
 <div x-show="conflictError" x-cloak class="rounded-xl border-l-4 border-danger bg-danger-bg p-3 text-sm text-danger">
 Le solde a changé entre-temps (un autre encaissement a eu lieu).
 <button type="button" x-on:click="closeQuickPay(); reloadBalance()" class="underline font-medium">Recharger le solde</button>
 </div>

 <div class="form-grid">
 <div>
 <label for="quickpay-amount" class="form-label form-label-required">Montant perçu (FCFA)</label>
 <input id="quickpay-amount" type="number" min="1" x-model.number="quickPay.amount" class="input-field" :class="formErrors.amount && 'input-field-error'">
 <p x-show="formErrors.amount" x-cloak x-text="formErrors.amount" class="field-error"></p>
 <p class="mt-1 text-xs text-slate-400">Pré-rempli avec le total coché. Un montant différent est encaissé tel quel, sur un reçu à ligne unique.</p>
 </div>
 <div>
 <label for="quickpay-method" class="form-label form-label-required">Mode de règlement</label>
 <select-field id="quickpay-method" model="quickPay.method" options="paymentMethodOptions" />
 </div>
 </div>

 <div x-show="sendState === 'retrying' || sendState === 'failed'" x-cloak class="rounded-xl border-l-4 border-warning bg-warning-bg p-3 text-sm text-warning" x-text="sendStateLabel()"></div>

 <div class="flex items-center justify-end gap-3 pt-1">
 <button type="button" x-on:click="closeQuickPay()" class="btn-modal-secondary group">Annuler</button>
 <button type="submit" :disabled="isSubmitting || quickPayTotal() <= 0" class="btn-modal-primary group">
 <icon x-show="!isSubmitting" name="checkmark-circle" class="w-5 h-5" />
 <span x-text="isSubmitting ? 'Encaissement…' : ('Valider et imprimer le reçu (' + formatMoney(quickPayTotal()) + ')')"></span>
 </button>
 </div>
 </form>
 </div>
 </modal-shell>
 </div>

 @* Fenêtre de confirmation affichée juste après l'encaissement (avant le reçu) — même parcours que
 /inscriptions : annoncer que le paiement est validé, puis laisser le choix entre imprimer le reçu
 ou terminer. *@
 <div class="no-print">
 <modal-shell open="showConfirmDialog" size="md" on-close="finishFromDialog()" title="Paiement validé">
 <confirm-dialog-body close-action="finishFromDialog()" close-label="Terminer"
 new-action="showReceiptFromDialog()" new-label="Imprimer le reçu">
 Le versement de <span class="font-medium text-slate-900 amount" x-text="receipt ? formatMoney(receipt.amount) : ''"></span>
 a bien été encaissé — reçu n° <span class="font-medium text-slate-900" x-text="receipt ? receipt.receiptNumber : ''"></span>.
 </confirm-dialog-body>
 </modal-shell>

 <!-- Modale de prévisualisation PDF et actions (Ticket JGK-F02 / Axe 4) -->
 @await Html.PartialAsync("_PdfPreviewModal")
 </div>

 @* Annulation d'un versement saisi par erreur : motif choisi dans une liste (payment-cancel.js). *@
 <div class="no-print">
 <partial name="_CancelPaymentModal" />
 </div>
</div>
````


### A.1.2 Frais scolaires et dépenses (`/frais`)

#### `src/SamaEcole.Web/Views/Fees/Index.cshtml`

````html
@{
 ViewData["Title"] = "Finance - Paramétrage des frais";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
 <script src="~/js/guided-delete.js" asp-append-version="true"></script>
 <script src="~/js/fees.js" asp-append-version="true"></script>
}

<div x-data="feesView()">
 <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
 <div>
 <h1 class="page-title">Finance</h1>
 <p class="page-subtitle">
 Gestion des paramètres de frais et des décaissements.
 </p>
 </div>
 </div>

 <!-- Navigation principale : Paramétrage des frais vs Décaissements -->
 <div class="mt-6 tab-nav" role="tablist" aria-label="Navigation Frais">
    <button type="button" role="tab" x-on:click="activeTab = 'fees'"
            class="tab-btn" :class="activeTab === 'fees' ? 'tab-btn-active' : ''">
        Paramétrage des Frais
    </button>
    <button type="button" role="tab" x-on:click="activeTab = 'disbursements'"
            class="tab-btn" :class="activeTab === 'disbursements' ? 'tab-btn-active' : ''">
        Décaissements
    </button>
 </div>

 <!-- ONGLET 1: Paramétrage des Frais -->
 <div x-show="activeTab === 'fees'" x-cloak>
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mt-6">
        <div>
            <h2 class="text-lg font-bold text-slate-900">Paramétrage des frais</h2>
        </div>
        <div class="flex items-center gap-2" x-show="canModifyFees" x-cloak>
        <button x-on:click="openCreateCategory()" class="btn-modal-secondary group">
        <icon name="plus" class="w-5 h-5 transition-transform duration-200 group-hover:rotate-90" />
        Catégorie
        </button>
        <button x-on:click="openApplyStandard()" :disabled="!selectedCategoryId" class="btn-modal-primary group">
        <icon name="wallet" class="w-5 h-5" />
        Appliquer un montant standard
        </button>
        </div>
    </div>

 @await Html.PartialAsync("_ErrorBanner")

 <!-- État vide : aucune catégorie -->
 <div x-show="!isLoading && categories.length === 0" x-cloak class="card mt-6">
 <empty-state icon="wallet" title="Aucune catégorie de frais n'est configurée."
 hint="Créez une catégorie (inscription, mensualité, cantine…) pour commencer." />
 </div>

 <!-- Grille -->
 <div x-show="categories.length > 0" x-cloak class="card mt-6 relative">

 <!-- Onglets de catégories : barre d'onglets partagée (.tab-nav-scroll / .tab-btn / .tab-btn-active,
 Styles/input.css). Chaque catégorie est un COMPOSÉ (libellé + corbeille), donc `.tab-btn` est
 posé sur le conteneur — moins le padding, gardé sur le libellé cliquable — et tabClass() n'y
 bascule que `tab-btn-active` (pastille bleu Unikol plein, texte blanc). -->
 <div class="p-4">
 <nav class="tab-nav-scroll" role="tablist" aria-label="Catégories de frais">
 <template x-for="category in categories" :key="category.id">
 <div class="tab-btn group !p-0" :class="tabClass(category.id)">
 <button type="button" role="tab" x-on:click="selectedCategoryId = category.id"
 class="flex items-center gap-2 rounded-lg px-3.5 py-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-1">
 <span x-text="category.name"></span>
 @* bg-white + anneau : le badge neutre (bg-gray-100) disparaîtrait sur la piste slate-100 ;
 blanc, il reste lisible aussi bien sur la pastille active bleue que sur la piste. *@
 <badge x-show="category.isRecurring" x-cloak variant="neutral" class="bg-white text-[10px] uppercase tracking-wide text-slate-600 ring-1 ring-inset ring-slate-200">Mensuel</badge>
 </button>
 <button x-show="canDeleteFees" x-cloak type="button" x-on:click="openDeleteCategory(category)"
 class="mr-2 p-1 text-current opacity-60 hover:opacity-100 hover:text-danger rounded-lg transition-all"
 title="Supprimer la catégorie">
 <icon name="trash-2" class="w-4 h-4" />
 </button>
 </div>
 </template>
 </nav>
 </div>

 <!-- Synthèse de la catégorie sélectionnée + recherche/filtre -->
 <div class="p-6 bg-slate-50/50 flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-slate-100">
 <div class="flex gap-6">
 <stat-card size="compact" label="Classes" icon="building" accent="primary">
 <span x-text="classrooms.length"></span>
 </stat-card>
 <stat-card size="compact" label="Montants définis" icon="wallet">
 <span :class="definedCount === classrooms.length ? 'text-success' : 'text-warning'"
 x-text="definedCount + ' / ' + classrooms.length"></span>
 </stat-card>
 </div>

 <div class="flex items-center gap-3">
 <select-field model="levelFilter" aria-label="Filtrer par niveau" class="!my-0 w-40" placeholder="Tous les niveaux"
 options-expr="[{value:'', label:'Tous les niveaux'}].concat(knownLevels.map(level => ({value: level, label: level})))" />

 <div class="relative w-full sm:w-56 flex items-center">
 <span aria-hidden="true" class="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
 <icon name="search" class="w-4 h-4 text-slate-400" />
 </span>
 <input type="search" x-model="search" placeholder="Rechercher une classe…"
 aria-label="Rechercher une classe"
 class="input-field !my-0 pl-9 pr-3 w-full" />
 </div>
 </div>
 </div>

 <!-- Aucune classe -->
 <div x-show="classrooms.length === 0" x-cloak>
 <empty-state icon="building" title="Aucune classe n'est encore définie."
 hint="Créez vos classes avant de paramétrer leurs frais." />
 </div>

 <!-- Tableau des classes -->
 <div x-show="classrooms.length > 0" x-cloak class="overflow-x-auto">
 <table class="w-full text-left border-collapse">
 <thead>
 <tr class="table-head">
 <sort-th column="name" sort-key="sortKey" sort-dir="sortDir" on-sort="toggleSort('name')" class="py-3 px-4">Classe</sort-th>
 <sort-th column="level" sort-key="sortKey" sort-dir="sortDir" on-sort="toggleSort('level')" class="py-3 px-4">Niveau</sort-th>
 <sort-th column="amount" sort-key="sortKey" sort-dir="sortDir" on-sort="toggleSort('amount')" class="py-3 px-4 text-right">Montant</sort-th>
 <th class="py-3 px-4 text-right">Actions</th>
 </tr>
 </thead>
 <tbody class="divide-y divide-slate-100">
 <tr class="hover:bg-slate-50 transition-colors duration-150" x-show="rows.length === 0" x-cloak>
 <td colspan="4">
 <empty-state icon="search" title="Aucune classe ne correspond à la recherche."
 hint="Essayez un autre nom, niveau ou filtre." />
 </td>
 </tr>

 <template x-for="row in rows" :key="row.classroom.id">
 <tr class="hover:bg-slate-50 transition-colors duration-150">
 <td class="py-3 px-4 font-medium text-slate-900" x-text="row.classroom.name"></td>
 <td class="py-3 px-4"><badge variant="neutral" x-text="row.classroom.level"></badge></td>
 <td class="py-3 px-4 text-right font-bold" :class="row.fee ? 'text-slate-900' : 'text-slate-300'">
 <span x-show="row.fee" x-text="formatMoney(row.fee ? row.fee.amount : null)"></span>
 <span x-show="!row.fee" x-cloak class="text-xs font-medium text-slate-400 italic">non défini</span>
 </td>
 <td class="py-3 px-4 text-right whitespace-nowrap">
 <row-actions show="row.fee">
 <row-action icon="clock" label="Historique" on-click="openHistory(row)" />
 <row-action icon="pencil" label="Modifier" on-click="openEdit(row)" show="canModifyFees" />
 <row-action icon="trash-2" label="Supprimer" on-click="openDeleteFee(row)" variant="danger" show="canDeleteFees" />
 </row-actions>
 <span x-show="canModifyFees && !row.fee" x-cloak class="text-xs text-slate-400 italic">
 Appliquez un standard
 </span>
 </td>
 </tr>
 </template>
 </tbody>
 </table>
 </div>
 </div>

 <div x-show="canDeleteFees" x-cloak>
 @await Html.PartialAsync("_TrashPanel", "fee-categories")
 </div>

 <!-- Création d'une catégorie de frais -->
 <modal-shell size="lg" open="isCategoryOpen" title="Nouvelle catégorie de frais">
 <modal-subtitle>Inscription, mensualité, cantine, transport… la liste est libre.</modal-subtitle>

 <div x-show="categoryErrors.global" x-cloak class="form-error-banner">
  <span x-text="categoryErrors.global"></span>
  <button type="button" x-show="categoryErrors.archivedConflict" x-cloak class="mt-2 block font-semibold underline"
          x-on:click="isCategoryOpen = false; window.softDeleteTrash.openFor('fee-categories', newCategory.name)">Voir dans les éléments supprimés</button>
 </div>
 <form x-on:submit.prevent="submitCreateCategory()" novalidate class="space-y-4">
 <div>
 <label for="cat-name" class="form-label form-label-required">Nom de la catégorie</label>
 <input id="cat-name" type="text" x-model="newCategory.name" required maxlength="60" placeholder="ex : Mensualité" class="input-field" :class="categoryErrors.name && 'input-field-error'">
 <p x-show="categoryErrors.name" x-cloak x-text="categoryErrors.name" class="field-error"></p>
 </div>
 <div class="flex items-start gap-2">
 <input id="cat-recurring" type="checkbox" x-model="newCategory.isRecurring" class="checkbox-field mt-1">
 <label for="cat-recurring" class="text-sm text-slate-700">
 Frais mensuel (dû chaque mois)
 <span class="block text-xs text-slate-400">Décochez pour un frais ponctuel : inscription, uniforme…</span>
 </label>
 </div>
 @* Frais optionnels : masquée pour une pension (incompatible, 422 côté serveur) — le module Internat a
    son propre choix. Décochée par défaut : un frais reste obligatoire tant qu'on ne le déclare pas facultatif. *@
 <div class="flex items-start gap-2" x-show="!newCategory.isBoardingFee">
 <input id="cat-optional" type="checkbox" x-model="newCategory.isOptional" class="checkbox-field mt-1">
 <label for="cat-optional" class="text-sm text-slate-700">
 Frais optionnel (au choix de la famille)
 <span class="block text-xs text-slate-400">Cochez pour l'uniforme, la tenue de sport… : l'inscription proposera une case à cocher. Laissez décoché pour l'inscription et la mensualité, dues par tous.</span>
 </label>
 </div>
 @* Masqué si le module Internat est désactivé (retour utilisateur du 29/09/2026) : un profil
    Général/Élémentaire/Simplifié n'a pas d'internat, cocher cette case n'y aurait aucun effet
    visible (écran Internat/Inscriptions, tous deux déjà masqués pour ces profils). *@
 <div class="flex items-start gap-2" x-show="$store.schoolConfig.internatEnabled" x-cloak>
 <input id="cat-boarding" type="checkbox" x-model="newCategory.isBoardingFee" class="checkbox-field mt-1">
 <label for="cat-boarding" class="text-sm text-slate-700">
 Frais d'internat (module Internat)
 <span class="block text-xs text-slate-400">Proposable automatiquement à l'inscription d'un élève Interne/Demi-pensionnaire (écran Internat, Inscriptions).</span>
 </label>
 </div>
 <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
 <button type="button" x-on:click="isCategoryOpen = false" class="btn-modal-secondary group">Annuler</button>
 <button type="submit" :disabled="isSavingCategory" class="btn-modal-primary group">
 <span x-text="isSavingCategory ? 'Enregistrement...' : 'Créer'"></span>
 </button>
 </div>
 </form>
 </modal-shell>

 <!-- Confirmation : catégorie de frais ajoutée -->
 <modal-shell open="showCategoryAddedDialog" size="md" on-close="showCategoryAddedDialog = false" title="Catégorie ajoutée">
 <confirm-dialog-body close-action="showCategoryAddedDialog = false"
 new-action="showCategoryAddedDialog = false; openCreateCategory()" new-label="Nouvelle catégorie">
 La catégorie <span class="font-medium text-slate-900" x-text="addedCategoryName"></span> a bien été ajoutée et est désormais disponible dans la grille.
 </confirm-dialog-body>
 </modal-shell>

 <!-- Modale : appliquer un montant standard -->
 <modal-shell open="isApplyOpen" size="lg">
 <modal-title>Montant standard — <span class="text-primary-100" x-text="selectedCategory ? selectedCategory.name : ''"></span></modal-title>
 @* Le décompte suit le périmètre choisi : il doit dire ce qui va RÉELLEMENT être écrit, pas le
 total de l'école — c'est le seul garde-fou avant un geste qui touche plusieurs classes. *@
 <modal-subtitle>
 Ce montant s'appliquera à
 <span class="font-semibold" x-text="applyTargetClassrooms.length"></span>
 classe(s) en une fois
 <span x-show="applyForm.level" x-cloak>— niveau <span class="font-semibold" x-text="applyForm.level"></span></span><span x-show="!applyForm.level">, toute l'école</span>.
 </modal-subtitle>

 @* Plus de compte-rendu vert affiché DANS la modale : au clic sur « Appliquer », la modale se
 ferme immédiatement et un Toast résume l'action (voir submitApplyStandard dans fees.js). *@
 <div x-show="applyErrors.global" x-cloak class="form-error-banner" x-text="applyErrors.global"></div>

 <form x-on:submit.prevent="submitApplyStandard()" novalidate class="space-y-4">
 @* Périmètre : « Toutes les classes » par défaut, puis les niveaux RÉELLEMENT présents dans
 l'école (knownLevels, déduit des classes) — la nomenclature des niveaux est libre, aucune
 liste figée ici. Cibler un niveau laisse les autres cycles intacts. *@
 <div>
 <label class="form-label">Périmètre d'application</label>
 <select-field model="applyForm.level" aria-label="Périmètre d'application" placeholder="Toutes les classes"
 options-expr="[{value:'', label:'Toutes les classes'}].concat(knownLevels.map(level => ({value: level, label: level})))" />
 <p x-show="applyErrors.level" x-cloak x-text="applyErrors.level" class="field-error"></p>
 </div>
 <div>
 <label for="apply-amount" class="form-label form-label-required">Montant (FCFA)</label>
 <input id="apply-amount" type="number" x-model.number="applyForm.amount" required min="0" step="500" placeholder="ex : 15000" class="input-field" :class="applyErrors.amount && 'input-field-error'">
 <p x-show="applyErrors.amount" x-cloak x-text="applyErrors.amount" class="field-error"></p>
 </div>
 <div class="flex items-start gap-2">
 <input id="apply-overwrite" type="checkbox" x-model="applyForm.overwriteExisting" class="checkbox-field mt-1">
 <label for="apply-overwrite" class="text-sm text-slate-700">
 Écraser les montants déjà définis
 <span class="block text-xs text-slate-400">Décoché, les exceptions ajustées classe par classe sont préservées.</span>
 </label>
 </div>
 <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
 <button type="button" x-on:click="isApplyOpen = false" class="btn-modal-secondary group">Annuler</button>
 <button type="submit" :disabled="isApplying" class="btn-modal-primary group">
 <span x-text="isApplying ? 'Application...' : 'Appliquer'"></span>
 </button>
 </div>
 </form>
 </modal-shell>

 <!-- Modale : modifier une exception -->
 <modal-shell open="editing" on-close="closeEdit()" size="md">
 <modal-title>Montant — <span class="text-primary-100" x-text="editing ? editing.classroomName : ''"></span></modal-title>
 <modal-subtitle><span x-text="selectedCategory ? selectedCategory.name : ''"></span></modal-subtitle>

 <div x-show="editErrors.global" x-cloak class="mb-4 rounded-xl border-l-4 border-warning bg-warning-bg p-3 text-sm text-warning" x-text="editErrors.global"></div>

 @* `editing` vaut null tant que la modale n'a pas été ouverte : sans ce <template x-if>, Alpine
    évaluait `editing.amount` dès le chargement de l'écran (avant tout clic sur "Modifier"),
    jetant une TypeError en boucle sur /frais — corrigé au passage (retour utilisateur du
    29/09/2026), même garde que la modale d'édition de salle (Views/Buildings/Index.cshtml). *@
 <template x-if="editing">
 <form x-on:submit.prevent="submitEdit()" novalidate class="space-y-4">
 <div>
 <label for="edit-amount" class="form-label form-label-required">Montant (FCFA)</label>
 <input id="edit-amount" type="number" x-model.number="editing.amount" required min="0" step="500"
 x-effect="if (editing) $nextTick(() => $el.focus())" class="input-field">
 <p x-show="editErrors.amount" x-cloak x-text="editErrors.amount" class="field-error"></p>
 </div>
 <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
 <button type="button" x-on:click="closeEdit()" class="btn-modal-secondary group">Annuler</button>
 <button type="submit" :disabled="isSavingEdit" class="btn-modal-primary group">
 <span x-text="isSavingEdit ? 'Enregistrement...' : 'Enregistrer'"></span>
 </button>
 </div>
 </form>
 </template>
 </modal-shell>

 <!-- Modale : historique -->
 <modal-shell open="historyFor" on-close="closeHistory()" size="lg">
 <modal-title>Historique — <span class="text-primary-100" x-text="historyFor ? historyFor.classroomName : ''"></span></modal-title>

 <div x-show="isLoadingHistory" class="py-8 text-center text-sm text-slate-400">Chargement…</div>

 <div x-show="!isLoadingHistory && historyEntries.length === 0" x-cloak class="py-8 text-center text-sm text-slate-400">
 Aucune modification enregistrée.
 </div>

 <ul x-show="!isLoadingHistory && historyEntries.length > 0" x-cloak class="max-h-80 divide-y divide-slate-100 overflow-y-auto">
 <template x-for="(entry, index) in historyEntries" :key="index">
 <li class="flex items-start justify-between gap-4 py-3">
 <div>
 <p class="text-sm text-slate-900">
 <span x-show="entry.oldAmount !== null" x-cloak>
 <span class="text-slate-400 line-through amount" x-text="formatMoney(entry.oldAmount)"></span>
 <span class="text-slate-400">→</span>
 </span>
 <span class="font-semibold amount" x-text="formatMoney(entry.newAmount)"></span>
 <span x-show="entry.oldAmount === null" x-cloak class="ml-1 text-xs text-slate-400">(définition initiale)</span>
 </p>
 <p class="mt-0.5 text-xs text-slate-500" x-text="'par ' + entry.changedByName"></p>
 </div>
 <span class="whitespace-nowrap text-xs text-slate-400" x-text="formatDate(entry.changedAt)"></span>
 </li>
 </template>
 </ul>
 </modal-shell>

 <partial name="_GuidedDeleteModal" />

 <!-- Confirmation : suppression d'une ligne de barème -->
 <modal-shell open="deletingFee" on-close="closeDeleteFee()" size="md">
 <modal-title>Supprimer ce montant</modal-title>
 <modal-subtitle>
 <span class="font-semibold text-slate-900" x-text="deletingFee ? deletingFee.classroomName : ''"></span>
 n'aura plus de montant défini pour cette catégorie tant qu'un nouveau ne sera pas fixé.
 </modal-subtitle>

 <div x-show="deleteFeeError" x-cloak class="form-error-banner" x-text="deleteFeeError"></div>

 <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
 <button type="button" x-on:click="closeDeleteFee()" class="btn-modal-secondary group">Annuler</button>
 <button type="button" x-on:click="confirmDeleteFee()" :disabled="isDeletingFee" class="btn-modal-danger">
 <span x-text="isDeletingFee ? 'Suppression...' : 'Supprimer'"></span>
 </button>
 </div>
 </modal-shell>

 <!-- Confirmation : catégorie supprimée -->
 <modal-shell open="showCategoryDeletedDialog" size="md" on-close="showCategoryDeletedDialog = false" title="Catégorie supprimée">
 <confirm-dialog-body close-action="showCategoryDeletedDialog = false">
 La catégorie <span class="font-medium text-slate-900" x-text="deletedCategoryName"></span> a bien été supprimée.
 </confirm-dialog-body>
 </modal-shell>

 <!-- Confirmation : montant modifié -->
 <modal-shell open="showFeeEditedDialog" size="md" on-close="showFeeEditedDialog = false" title="Montant modifié">
 <confirm-dialog-body close-action="showFeeEditedDialog = false">
 Le montant de <span class="font-medium text-slate-900" x-text="editedFeeClassroomName"></span> est désormais de <span class="font-medium text-slate-900 amount" x-text="formatMoney(editedFeeAmount)"></span>.
 </confirm-dialog-body>
 </modal-shell>

    <!-- Confirmation : montant supprimé -->
    <modal-shell open="showFeeDeletedDialog" size="md" on-close="showFeeDeletedDialog = false" title="Montant supprimé">
    <confirm-dialog-body close-action="showFeeDeletedDialog = false">
    Le montant de <span class="font-medium text-slate-900" x-text="deletedFeeClassroomName"></span> a bien été supprimé.
    </confirm-dialog-body>
    </modal-shell>
 </div>

 <!-- ONGLET 2: Décaissements -->
 <div x-show="activeTab === 'disbursements'" x-cloak>
    <div class="flex items-center justify-between mt-6">
        <h2 class="text-lg font-bold text-slate-900">Historique des décaissements</h2>
        <button type="button" x-on:click="openCreateDisbursement()" class="btn-modal-primary group">
            <icon name="plus" class="w-5 h-5 transition-transform duration-200 group-hover:rotate-90" />
            Nouveau Décaissement
        </button>
    </div>

    <!-- Table Décaissements -->
    <div class="card mt-6 relative">
        <div class="overflow-x-auto">
            <table class="w-full text-left border-collapse">
                <thead>
                    <tr class="table-head">
                        <th class="py-3 px-4">Date</th>
                        <th class="py-3 px-4">Bénéficiaire</th>
                        <th class="py-3 px-4">Motif</th>
                        <th class="py-3 px-4">Catégorie</th>
                        <th class="py-3 px-4 text-right">Montant</th>
                        <th class="py-3 px-4">Moyen</th>
                        <th class="py-3 px-4 text-right">Actions</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    <tr x-show="isLoadingDisbursements" x-cloak>
                        <td colspan="7" class="py-12 text-center text-slate-500">Chargement...</td>
                    </tr>
                    <tr x-show="!isLoadingDisbursements && disbursements.length === 0" x-cloak>
                        <td colspan="7" class="py-12 text-center text-slate-500">Aucun décaissement enregistré.</td>
                    </tr>
                    <template x-for="d in disbursements" :key="d.id">
                        <tr class="transition-colors" :class="disbursementRowClass(d)">
                            <td class="py-3 px-4 text-sm text-slate-900" x-text="formatDateOnly(d.date)"></td>
                            <td class="py-3 px-4 text-sm font-medium text-slate-900" :class="disbursementState(d) === 'reversed' && 'line-through text-slate-400'" x-text="d.beneficiary"></td>
                            <td class="py-3 px-4 text-sm text-slate-500">
                                <span :class="disbursementState(d) === 'reversed' && 'line-through text-slate-400'" x-text="d.reason"></span>
                                @* Écriture comptable : jamais supprimée, annulée par une contre-écriture qui la référence. *@
                                <span x-show="disbursementState(d) === 'reversal'" x-cloak class="mt-0.5 block text-xs text-rose-600" x-text="reversalNote(d)"></span>
                                <span x-show="disbursementState(d) === 'reversed'" x-cloak class="mt-0.5 block text-xs text-slate-400">Annulé par une contre-écriture.</span>
                            </td>
                            <td class="py-3 px-4 text-sm text-slate-900">
                                <badge variant="neutral" x-text="d.category"></badge>
                                <badge variant="danger" x-show="disbursementState(d) === 'reversal'" x-cloak class="ml-1">Annulation</badge>
                                <badge variant="warning" x-show="disbursementState(d) === 'reversed'" x-cloak class="ml-1">Annulé</badge>
                            </td>
                            <td class="py-3 px-4 text-sm font-bold text-right amount"
                                :class="disbursementState(d) === 'reversal' ? 'text-rose-600' : (disbursementState(d) === 'reversed' ? 'text-slate-400 line-through' : 'text-slate-900')"
                                x-text="formatMoney(d.amount)"></td>
                            <td class="py-3 px-4 text-sm text-slate-500" x-text="d.paymentMethod"></td>
                            <td class="py-3 px-4 text-right">
                                <button type="button" x-show="canReverseDisbursement(d)" x-cloak x-on:click="openDeleteDisbursement(d)" class="text-danger hover:text-red-700 transition-colors p-1" title="Annuler ce décaissement" aria-label="Annuler ce décaissement">
                                    <icon name="trash" class="w-5 h-5" />
                                </button>
                            </td>
                        </tr>
                    </template>
                </tbody>
            </table>
        </div>
    </div>

    <!-- Modale Nouveau Décaissement -->
    <modal-shell open="creatingDisbursement" on-close="closeCreateDisbursement()" size="md">
        <form x-on:submit.prevent="submitCreateDisbursement()" novalidate>
            <modal-title>Nouveau Décaissement</modal-title>
            <modal-subtitle>Enregistrer une dépense ou un paiement sortant.</modal-subtitle>
            
            <div x-show="createDisbursementError" x-cloak class="form-error-banner" x-text="createDisbursementError"></div>

            <div class="space-y-4 mt-4">
                <div>
                    <label class="form-label" for="disb_date">Date</label>
                    <date-field id="disb_date" model="disbursementForm.date" />
                </div>
                <div>
                    <label class="form-label" for="disb_beneficiary">Bénéficiaire</label>
                    <input type="text" id="disb_beneficiary" x-model="disbursementForm.beneficiary" required maxlength="150" class="input-field" placeholder="Nom du fournisseur ou employé..." />
                </div>
                <div>
                    <label class="form-label" for="disb_reason">Motif</label>
                    <input type="text" id="disb_reason" x-model="disbursementForm.reason" required maxlength="255" class="input-field" placeholder="Achat de fournitures..." />
                </div>
                <div>
                    <label class="form-label" for="disb_category">Catégorie</label>
                    <select-field id="disb_category" model="disbursementForm.category" options-expr="[{value:'Salaires', label:'Salaires'}, {value:'Maintenance', label:'Maintenance'}, {value:'Fournitures', label:'Fournitures'}, {value:'ChargesFixes', label:'Charges Fixes'}, {value:'Divers', label:'Divers'}]" />
                </div>
                <div>
                    <label class="form-label" for="disb_amount">Montant</label>
                    <input type="number" id="disb_amount" x-model.number="disbursementForm.amount" required min="1" step="1" class="input-field font-mono" placeholder="Ex: 50000" />
                </div>
                <div>
                    <label class="form-label" for="disb_method">Mode de paiement</label>
                    <select-field id="disb_method" model="disbursementForm.paymentMethod" options-expr="[{value:'Cash', label:'Espèces'}, {value:'Cheque', label:'Chèque'}, {value:'Transfer', label:'Virement'}, {value:'MobileMoney', label:'Mobile Money'}]" />
                </div>
            </div>

            <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
                <button type="button" x-on:click="closeCreateDisbursement()" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" :disabled="isSubmittingDisbursement" class="btn-modal-primary group">
                    <span x-text="isSubmittingDisbursement ? 'Enregistrement...' : 'Enregistrer'"></span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- Modale Supprimer Décaissement -->
    <modal-shell open="deletingDisbursement" on-close="closeDeleteDisbursement()" size="md">
        <modal-title>Annuler ce décaissement</modal-title>
        <modal-subtitle>
            Voulez-vous vraiment annuler le décaissement pour <span class="font-semibold text-slate-900" x-text="deletingDisbursement ? deletingDisbursement.beneficiary : ''"></span> ?
        </modal-subtitle>
        <p class="mt-3 text-sm leading-relaxed text-slate-500">
            Le décaissement est une écriture comptable : il n'est pas supprimé. Une ligne d'annulation, de montant opposé,
            est ajoutée à la date du jour ; le solde, la trésorerie et la TVA déductible se corrigent d'eux-mêmes et
            l'historique reste complet.
        </p>

        <div x-show="deleteDisbursementError" x-cloak class="form-error-banner" x-text="deleteDisbursementError"></div>
        
        <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
            <button type="button" x-on:click="closeDeleteDisbursement()" class="btn-modal-secondary group">Fermer</button>
            <button type="button" x-on:click="confirmDeleteDisbursement()" :disabled="isDeletingDisbursement" class="btn-modal-danger">
                <span x-text="isDeletingDisbursement ? 'Annulation...' : 'Oui, annuler'"></span>
            </button>
        </div>
    </modal-shell>
 </div>
</div>

````


### A.1.3 Paie (`/paie`)

#### `src/SamaEcole.Web/Views/Payroll/Index.cshtml`

````html
@{
    ViewData["Title"] = "Paie";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
    <script src="~/js/payroll.js" asp-append-version="true"></script>
}

<div x-data="payrollView()">
    <div>
        <h1 class="page-title">Paie</h1>
        <p class="page-subtitle">Contrats du personnel, fiches de paie et déclarations fiscales.</p>
    </div>

    <!-- Onglets -->
    <div class="mt-6">
        <nav class="tab-nav-scroll" role="tablist" aria-label="Onglets Paie">
            <button type="button" role="tab" x-on:click="tab = 'contrats'"
                    class="tab-btn" :class="tabClass('contrats')">
                Contrats
            </button>
            <button type="button" role="tab" x-on:click="tab = 'fiches'"
                    class="tab-btn" :class="tabClass('fiches')">
                Fiches de paie
            </button>
            <button type="button" role="tab" x-on:click="tab = 'fiscalite'"
                    class="tab-btn" :class="tabClass('fiscalite')">
                Déclarations fiscales
            </button>
        </nav>
    </div>

    @await Html.PartialAsync("_ErrorBanner")

    <!-- ============================= ONGLET CONTRATS ============================= -->
    <div x-show="tab === 'contrats'" x-cloak class="mt-6">
        <div class="card">
            <div class="card-header">
                <h2 class="text-sm font-semibold text-slate-700">Contrats du personnel</h2>
                <button type="button" x-on:click="openContractModal()" class="btn-modal-primary group">
                    <icon name="plus" class="w-4 h-4 transition-transform duration-200 group-hover:rotate-90" />
                    Nouveau contrat
                </button>
            </div>
            <div class="overflow-x-auto">
                <table class="w-full text-left border-collapse">
                    <thead>
                        <tr class="table-head">
                            <th class="py-3 px-4">Employé</th>
                            <th class="py-3 px-4">Qualité</th>
                            <th class="py-3 px-4">Type</th>
                            <th class="py-3 px-4 text-right">Salaire de base</th>
                            <th class="py-3 px-4 text-right">Taux horaire</th>
                            <th class="py-3 px-4 text-right">Prime transport</th>
                            <th class="py-3 px-4">Moyen de paiement</th>
                            <th class="py-3 px-4">Statut</th>
                            <th class="py-3 px-4 text-right">Actions</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        <tr x-show="!isLoading && contracts.length === 0" x-cloak>
                            <td colspan="8">
                                <empty-state icon="document" title="Aucun contrat enregistré."
                                    hint="Ajoutez un contrat pour pouvoir générer une fiche de paie." />
                            </td>
                        </tr>
                        <template x-for="contract in contracts" :key="contract.id">
                            <tr class="hover:bg-slate-50 transition-colors">
                                <td class="py-3 px-4 font-medium text-slate-900" x-text="contract.employeeFullName"></td>
                                <td class="py-3 px-4 text-slate-600" x-text="contract.employeeRole"></td>
                                <td class="py-3 px-4 text-slate-600">
                                    <span x-text="contractTypeLabel(contract.type)"></span>
                                    <span x-show="contract.applyStatutoryDeductions === false" x-cloak
                                          class="ml-1 inline-block rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800"
                                          title="Aucune retenue ni cotisation : le salaire brut est payé tel quel.">Sans retenues</span>
                                </td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(contract.baseSalary)"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(contract.hourlyRate)"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(contract.transportAllowance)"></td>
                                <td class="py-3 px-4">
                                    <span class="text-slate-600" x-text="payoutMethodLabel(contract.payoutMethod)"></span>
                                    <p x-show="contract.payoutAccountReference" x-cloak class="text-xs text-slate-400 font-mono" x-text="contract.payoutAccountReference"></p>
                                </td>
                                <td class="py-3 px-4">
                                    <span x-show="!contract.endDate" class="status-badge-success"><span class="status-dot" aria-hidden="true"></span>Actif</span>
                                    <span x-show="contract.endDate" x-cloak class="status-badge-neutral"><span class="status-dot" aria-hidden="true"></span><span x-text="'Clôturé le ' + formatDate(contract.endDate)"></span></span>
                                </td>
                                <td class="py-3 px-4 text-right">
                                    <div class="flex items-center justify-end gap-1">
                                        <button type="button" x-show="contract.type === 'Vacataire'" x-cloak
                                                x-on:click="openHourRecordsModal(contract)" title="Voir/Ajouter des heures"
                                                aria-label="Voir ou ajouter des heures"
                                                class="p-1.5 rounded-lg text-slate-400 hover:text-primary-600 hover:bg-primary-50 transition-colors">
                                            <icon name="clock" class="w-4 h-4" />
                                        </button>
                                        <button type="button" x-on:click="downloadWorkCertificate(contract)" :disabled="downloadingCertificateId === contract.id"
                                                title="Attestation de travail" aria-label="Télécharger l'attestation de travail"
                                                class="p-1.5 rounded-lg text-slate-400 hover:text-blue-600 hover:bg-blue-50 disabled:opacity-60 transition-colors">
                                            <icon name="document-text" class="w-4 h-4" x-show="downloadingCertificateId !== contract.id" />
                                            <icon name="loader" class="w-4 h-4 animate-spin" x-show="downloadingCertificateId === contract.id" x-cloak />
                                        </button>
                                        <button type="button" x-show="!contract.endDate" x-cloak
                                                x-on:click="openEditContractModal(contract)" title="Modifier le salaire/taux horaire"
                                                aria-label="Modifier le contrat"
                                                class="p-1.5 rounded-lg text-slate-400 hover:text-primary-600 hover:bg-primary-50 transition-colors">
                                            <icon name="pencil" class="w-4 h-4" />
                                        </button>
                                        <button type="button" x-show="!contract.endDate" x-cloak
                                                x-on:click="openCloseContractModal(contract)" title="Clôturer le contrat"
                                                aria-label="Clôturer le contrat"
                                                class="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-rose-50 transition-colors">
                                            <icon name="block" class="w-4 h-4" />
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>
            </div>
        </div>
    </div>

    <!-- ============================= ONGLET FICHES DE PAIE ============================= -->
    <div x-show="tab === 'fiches'" x-cloak class="mt-6">
        <div class="card">
            <div class="filter-bar lg:items-end lg:justify-between">
                <div class="flex flex-wrap items-end gap-3">
                    <div>
                        <label class="form-label-sm">Mois</label>
                        <select x-model="filterMonth" class="input-field mt-0 font-semibold">
                            <option value="" class="font-semibold">Tous</option>
                            <template x-for="m in 12" :key="m">
                                <option :value="m" x-text="monthLabel(m)" class="font-semibold"></option>
                            </template>
                        </select>
                    </div>
                    <div>
                        <label class="form-label-sm">Année</label>
                        <input type="number" x-model="filterYear" class="input-field mt-0 w-28" />
                    </div>
                    <button type="button" x-on:click="applyPayslipFilters()" class="btn-modal-secondary group">
                        <icon name="chart-multiple" class="w-4 h-4" />
                        Filtrer
                    </button>
                </div>
                <button type="button" x-on:click="openPayslipModal()" class="btn-modal-primary group">
                    <icon name="plus" class="w-4 h-4 transition-transform duration-200 group-hover:rotate-90" />
                    Générer une fiche
                </button>
            </div>
            <div class="overflow-x-auto">
                <table class="w-full text-left border-collapse">
                    <thead>
                        <tr class="table-head">
                            <th class="py-3 px-4">Employé</th>
                            <th class="py-3 px-4">Période</th>
                            <th class="py-3 px-4 text-right">Brut</th>
                            <th class="py-3 px-4 text-right">Net à payer</th>
                            <th class="py-3 px-4">Statut</th>
                            <th class="py-3 px-4 text-right">Actions</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        <tr x-show="!isLoading && payslips.length === 0" x-cloak>
                            <td colspan="6">
                                <empty-state icon="document" title="Aucune fiche de paie sur cette période."
                                    hint="Générez une fiche pour un contrat existant." />
                            </td>
                        </tr>
                        <template x-for="payslip in payslips" :key="payslip.id">
                            <tr class="hover:bg-slate-50 transition-colors">
                                <td class="py-3 px-4 font-medium text-slate-900">
                                    <span x-text="payslip.employeeFullName"></span>
                                    <span x-show="payslip.deductionsApplied === false" x-cloak
                                          class="ml-1 inline-block rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800"
                                          title="Aucune retenue ni cotisation : net = brut + prime de transport.">Sans retenues</span>
                                </td>
                                <td class="py-3 px-4 text-slate-600" x-text="monthLabel(payslip.month) + ' ' + payslip.year"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(payslip.grossSalary)"></td>
                                <td class="py-3 px-4 text-right font-semibold text-slate-900 amount" x-text="formatAmount(payslip.netSalary)"></td>
                                <td class="py-3 px-4">
                                    <span x-show="payslip.status === 'Generated'" class="status-badge-warning"><span class="status-dot" aria-hidden="true"></span>À payer</span>
                                    <span x-show="payslip.status === 'Paid'" x-cloak class="status-badge-success"><span class="status-dot" aria-hidden="true"></span><span x-text="'Payée le ' + formatDate(payslip.paidAt)"></span></span>
                                    <span x-show="payslip.status === 'Cancelled'" x-cloak class="status-badge-neutral" :title="payslip.cancellationReason"><span class="status-dot" aria-hidden="true"></span>Annulée</span>
                                </td>
                                <td class="py-3 px-4 text-right">
                                    <div class="flex flex-wrap items-center justify-end gap-2">
                                        <button type="button" x-show="payslip.status === 'Generated'" x-cloak
                                                x-on:click="openPayslipAction(payslip, 'pay')" class="btn-modal-primary group">Payer</button>
                                        <button type="button" x-show="canCancelPayslip && payslip.status !== 'Cancelled'" x-cloak
                                                x-on:click="openPayslipAction(payslip, 'regenerate')" class="btn-modal-secondary group">Corriger</button>
                                        <button type="button" x-show="canCancelPayslip && payslip.status !== 'Cancelled'" x-cloak
                                                x-on:click="openPayslipAction(payslip, 'cancel')" class="btn-modal-secondary group">Annuler</button>
                                        <button type="button" x-on:click="printPayslip(payslip.id)" :disabled="printingPayslipId === payslip.id"
                                                class="btn-modal-secondary inline-flex items-center gap-2 disabled:opacity-60 group">
                                            <icon name="print" class="w-4 h-4" x-show="printingPayslipId !== payslip.id" />
                                            <icon name="loader" class="w-4 h-4 animate-spin" x-show="printingPayslipId === payslip.id" x-cloak />
                                            <span x-text="printingPayslipId === payslip.id ? 'Génération...' : (payslip.status === 'Paid' ? 'Reçu PDF' : 'Bulletin PDF')"></span>
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>
            </div>
        </div>
    </div>

    <!-- ============================= ONGLET DÉCLARATIONS FISCALES ============================= -->
    <div x-show="tab === 'fiscalite'" x-cloak class="mt-6">
        <div class="card">
            <div class="filter-bar lg:items-end lg:justify-between">
                <div class="flex flex-wrap items-end gap-3">
                    <div>
                        <label class="form-label-sm">Année</label>
                        <input type="number" x-model="filterTaxYear" class="input-field mt-0 w-28" />
                    </div>
                    <button type="button" x-on:click="applyTaxFilters()" class="btn-modal-secondary group">
                        <icon name="chart-multiple" class="w-4 h-4" />
                        Filtrer
                    </button>
                </div>
                <button type="button" x-on:click="openTaxModal()" class="btn-modal-primary group">
                    <icon name="plus" class="w-4 h-4 transition-transform duration-200 group-hover:rotate-90" />
                    Générer une déclaration
                </button>
            </div>
            <div class="overflow-x-auto">
                <table class="w-full text-left border-collapse">
                    <thead>
                        <tr class="table-head">
                            <th class="py-3 px-4">Période</th>
                            <th class="py-3 px-4 text-right">IPRES</th>
                            <th class="py-3 px-4 text-right">CSS</th>
                            <th class="py-3 px-4 text-right">VRS</th>
                            <th class="py-3 px-4 text-right">BRS</th>
                            <th class="py-3 px-4 text-right">Total dû à l'État</th>
                        </tr>
                    </thead>
                    <tbody class="divide-y divide-slate-100">
                        <tr x-show="!isLoading && taxDeclarations.length === 0" x-cloak>
                            <td colspan="6">
                                <empty-state icon="document" title="Aucune déclaration sur cette année."
                                    hint="Générez une déclaration pour un mois où des fiches de paie existent." />
                            </td>
                        </tr>
                        <template x-for="declaration in taxDeclarations" :key="declaration.id">
                            <tr class="hover:bg-slate-50 transition-colors">
                                <td class="py-3 px-4 font-medium text-slate-900" x-text="monthLabel(declaration.month) + ' ' + declaration.year"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(declaration.totalIpres)"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(declaration.totalCss)"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(declaration.totalVrs)"></td>
                                <td class="py-3 px-4 text-right text-slate-600 amount" x-text="formatAmount(declaration.totalBrs)"></td>
                                <td class="py-3 px-4 text-right font-semibold text-slate-900 amount" x-text="formatAmount(declaration.totalDueToState)"></td>
                            </tr>
                        </template>
                    </tbody>
                </table>
            </div>
        </div>
    </div>

    <!-- ===================== Modale : Nouveau contrat ===================== -->
    <modal-shell size="60" open="isContractModalOpen" title="Nouveau contrat">
        <form x-on:submit.prevent="submitContract()" novalidate class="space-y-5">
            <div>
                <label class="form-label">Type d'employé</label>
                <div class="mt-1 flex gap-4">
                    <label class="flex items-center gap-2 text-sm">
                        <input type="radio" class="radio-field" x-model="contractForm.employeeKind" value="teacher" /> Enseignant
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input type="radio" class="radio-field" x-model="contractForm.employeeKind" value="user" /> Utilisateur (non-enseignant)
                    </label>
                    <label class="flex items-center gap-2 text-sm">
                        <input type="radio" class="radio-field" x-model="contractForm.employeeKind" value="external" /> Personnel sans compte (gardien…)
                    </label>
                </div>
            </div>

            <div x-show="contractForm.employeeKind === 'teacher'">
                <label class="form-label">Enseignant <span class="text-red-500">*</span></label>
                <select-field model="contractForm.teacherId" placeholder="Sélectionnez un enseignant..."
                    options-expr="teachers.map(t => ({value: t.id, label: t.fullName + ' (' + t.matricule + ')'}))" />
            </div>
            <div x-show="contractForm.employeeKind === 'user'">
                <label class="form-label">Utilisateur <span class="text-red-500">*</span></label>
                <select-field model="contractForm.userId" placeholder="Sélectionnez un utilisateur..."
                    options-expr="users.map(u => ({value: u.id, label: u.fullName + ' (' + u.role + ')'}))" />
                <p class="mt-1 text-xs text-slate-400" x-show="users.length === 0">Aucun utilisateur accessible (réservé au Directeur).</p>
            </div>

            <div x-show="contractForm.employeeKind === 'external'" x-cloak class="space-y-3">
                <div class="form-grid">
                    <div>
                        <label class="form-label">Nom complet <span class="text-red-500">*</span></label>
                        <input type="text" maxlength="150" x-model="contractForm.externalFullName" placeholder="ex : Moussa Diop" class="input-field mt-1" />
                    </div>
                    <div>
                        <label class="form-label">Fonction</label>
                        <input type="text" maxlength="100" x-model="contractForm.externalJobTitle" placeholder="ex : Gardien" class="input-field mt-1" />
                    </div>
                </div>
                <div>
                    <label class="form-label">Téléphone</label>
                    <input type="text" maxlength="30" x-model="contractForm.externalPhone" placeholder="ex : 77 000 00 00" class="input-field mt-1" />
                </div>
                <p class="text-xs text-slate-500">Cette personne n'aura pas de compte : elle est payée et reçoit son reçu, sans jamais se connecter.</p>
            </div>

            <div>
                <label class="form-label">Type de contrat</label>
                <select x-model="contractForm.type" class="input-field mt-1 font-semibold">
                    <option value="Permanent" class="font-semibold">Permanent</option>
                    <option value="Vacataire" class="font-semibold">Vacataire (horaire)</option>
                </select>
            </div>

            <div class="form-grid">
                <div x-show="contractForm.type === 'Permanent'">
                    <label class="form-label">Salaire de base (FCFA)</label>
                    <input type="number" min="0" x-model="contractForm.baseSalary" class="input-field mt-1" />
                </div>
                <div x-show="contractForm.type === 'Vacataire'">
                    <label class="form-label">Taux horaire (FCFA)</label>
                    <input type="number" min="0" x-model="contractForm.hourlyRate" class="input-field mt-1" />
                </div>
                <div>
                    <label class="form-label">Prime de transport (FCFA)</label>
                    <input type="number" min="0" x-model="contractForm.transportAllowance" class="input-field mt-1" />
                </div>
            </div>

            @* Deux régimes dans une même école : avec retenues (IPRES, CSS, VRS/CFCE, BRS — le calcul historique) ou sans
               retenues (ex. Daara : le salaire brut est payé tel quel). Se règle par personne, donc par contrat. *@
            <div class="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                <label class="flex items-start gap-3 cursor-pointer">
                    <input type="checkbox" x-model="contractForm.applyStatutoryDeductions"
                           class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary" />
                    <span>
                        <span class="block text-sm font-medium text-slate-900">Appliquer les retenues légales (IPRES, CSS, VRS/CFCE, BRS)</span>
                        <span class="block text-xs text-slate-500 mt-0.5"
                              x-text="contractForm.applyStatutoryDeductions
                                  ? 'Les cotisations et retenues sont calculées sur la fiche de paie.'
                                  : 'Paie sans retenues : la personne reçoit son salaire brut plus sa prime de transport, sans aucune déduction.'"></span>
                    </span>
                </label>
            </div>

            <div class="form-grid">
                <div>
                    <label class="form-label">Moyen de paiement</label>
                    <select-field model="contractForm.payoutMethod" options-expr="payoutMethodOptions" />
                </div>
                <div x-show="contractForm.payoutMethod !== 'Cash'" x-cloak>
                    <label class="form-label">Référence du compte</label>
                    <input type="text" maxlength="50" x-model="contractForm.payoutAccountReference" placeholder="ex : numéro de compte, numéro Wave/Orange Money" class="input-field mt-1" />
                </div>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isContractModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isSavingContract">
                    <span x-show="!isSavingContract">Enregistrer</span>
                    <span x-show="isSavingContract">Enregistrement...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Modifier un contrat (Volume 1 §14.1) ===================== -->
    <modal-shell size="60" open="isEditContractModalOpen" title="Modifier le contrat">
        <form x-on:submit.prevent="submitEditContract()" novalidate class="space-y-5">
            <div class="form-grid">
                <div x-show="editContractForm.type === 'Permanent'">
                    <label class="form-label">Salaire de base (FCFA)</label>
                    <input type="number" min="0" x-model="editContractForm.baseSalary" class="input-field mt-1" />
                </div>
                <div x-show="editContractForm.type === 'Vacataire'">
                    <label class="form-label">Taux horaire (FCFA)</label>
                    <input type="number" min="0" x-model="editContractForm.hourlyRate" class="input-field mt-1" />
                </div>
                <div>
                    <label class="form-label">Prime de transport (FCFA)</label>
                    <input type="number" min="0" x-model="editContractForm.transportAllowance" class="input-field mt-1" />
                </div>
            </div>

            <div class="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                <label class="flex items-start gap-3 cursor-pointer">
                    <input type="checkbox" x-model="editContractForm.applyStatutoryDeductions"
                           class="mt-0.5 h-4 w-4 rounded border-slate-300 text-primary focus:ring-primary" />
                    <span>
                        <span class="block text-sm font-medium text-slate-900">Appliquer les retenues légales (IPRES, CSS, VRS/CFCE, BRS)</span>
                        <span class="block text-xs text-slate-500 mt-0.5">
                            Concerne les prochaines fiches de paie. Les fiches déjà générées ne changent pas.
                        </span>
                    </span>
                </label>
            </div>

            <div class="form-grid">
                <div>
                    <label class="form-label">Moyen de paiement</label>
                    <select-field model="editContractForm.payoutMethod" options-expr="payoutMethodOptions" />
                </div>
                <div x-show="editContractForm.payoutMethod !== 'Cash'" x-cloak>
                    <label class="form-label">Référence du compte</label>
                    <input type="text" maxlength="50" x-model="editContractForm.payoutAccountReference" placeholder="ex : numéro de compte, numéro Wave/Orange Money" class="input-field mt-1" />
                </div>
            </div>

            <div>
                <label class="form-label">Motif <span class="text-red-500">*</span></label>
                <textarea x-model="editContractForm.reason" rows="2" placeholder="Ex. Augmentation annuelle, révision du taux horaire…" class="input-field mt-1"></textarea>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isEditContractModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isSavingEditContract">
                    <span x-show="!isSavingEditContract">Enregistrer</span>
                    <span x-show="isSavingEditContract">Enregistrement...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Clôturer un contrat (Volume 1 §14.1) ===================== -->
    <modal-shell size="60" open="isCloseContractModalOpen" title="Clôturer le contrat">
        <form x-on:submit.prevent="submitCloseContract()" novalidate class="space-y-5">
            <p class="text-base font-medium leading-relaxed text-slate-700">
                <span x-text="closeContractForm.employeeFullName" class="font-semibold text-slate-900"></span> —
                le contrat ne sera pas supprimé, mais restera clôturé : aucune nouvelle fiche de paie ne
                pourra plus être générée à partir de lui.
            </p>

            <div>
                <label class="form-label">Date de clôture <span class="text-red-500">*</span></label>
                <date-field model="closeContractForm.endDate" required="true" aria-label="Date de clôture" class="mt-1" />
            </div>

            <div>
                <label class="form-label">Motif <span class="text-red-500">*</span></label>
                <textarea x-model="closeContractForm.reason" rows="2" placeholder="Ex. Fin de contrat, départ de l'établissement…" class="input-field mt-1"></textarea>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isCloseContractModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isClosingContract">
                    <span x-show="!isClosingContract">Clôturer</span>
                    <span x-show="isClosingContract">Clôture...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Payer / Corriger / Annuler une fiche (Volume 1 §14.5 et §14.6) ===================== -->
    <modal-shell size="60" open="isPayslipActionOpen" title="Fiche de paie">
        <form x-on:submit.prevent="submitPayslipAction()" novalidate class="space-y-5">
            <p class="text-base font-medium leading-relaxed text-slate-700">
                <span class="font-semibold text-slate-900" x-text="payslipAction.employeeFullName"></span> —
                <span x-text="payslipAction.periodLabel"></span> · net
                <span class="font-semibold text-slate-900 amount" x-text="formatAmount(payslipAction.netSalary)"></span>
            </p>

            <p x-show="payslipAction.kind === 'pay'" class="text-sm text-slate-600">
                Le règlement crée une dépense « Salaires » en trésorerie et en comptabilité, et le bulletin sert de reçu à faire signer.
            </p>
            <p x-show="payslipAction.kind === 'regenerate'" x-cloak class="text-sm text-slate-600">
                La fiche actuelle est annulée (conservée, marquée annulée) et une nouvelle est calculée sur le contrat tel qu'il est maintenant.
                Si le contrat est faux, corrigez-le d'abord dans l'onglet Contrats.
                <span x-show="payslipAction.status === 'Paid'">Le paiement déjà enregistré est contre-passé : il faudra payer la nouvelle fiche.</span>
            </p>
            <p x-show="payslipAction.kind === 'cancel'" x-cloak class="text-sm text-slate-600">
                La fiche est annulée sans être supprimée.
                <span x-show="payslipAction.status === 'Paid'">Le paiement déjà enregistré est contre-passé en trésorerie.</span>
            </p>

            <div x-show="payslipAction.kind === 'pay'" class="form-grid">
                <div>
                    <label class="form-label">Moyen de règlement</label>
                    <select-field model="payslipAction.paymentMethod" options-expr="payslipPaymentMethodOptions" />
                </div>
                <div>
                    <label class="form-label">Date du règlement</label>
                    <date-field model="payslipAction.date" required="true" aria-label="Date du règlement" class="mt-1" />
                </div>
            </div>

            <div x-show="payslipAction.kind !== 'pay'" x-cloak>
                <label class="form-label">Motif <span class="text-red-500">*</span></label>
                <textarea x-model="payslipAction.reason" rows="2" placeholder="Ex. Erreur de montant sur le contrat…" class="input-field mt-1"></textarea>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isPayslipActionOpen = false" class="btn-modal-secondary group">Fermer</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isSubmittingPayslipAction">
                    <span x-show="!isSubmittingPayslipAction" x-text="payslipAction.kind === 'pay' ? 'Enregistrer le paiement' : (payslipAction.kind === 'regenerate' ? 'Annuler et régénérer' : 'Annuler la fiche')"></span>
                    <span x-show="isSubmittingPayslipAction">Traitement...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Générer une fiche de paie ===================== -->
    <modal-shell size="60" open="isPayslipModalOpen" title="Générer une fiche de paie">
        <form x-on:submit.prevent="submitPayslip()" novalidate class="space-y-5">
            <div>
                <label class="form-label">Contrat <span class="text-red-500">*</span></label>
                <select-field model="payslipForm.employeeContractId" placeholder="Sélectionnez un contrat..." on-change="onPayslipContractChanged()"
                    options-expr="contracts.filter(c => !c.endDate).map(c => ({value: c.id, label: c.employeeFullName + ' — ' + contractTypeLabel(c.type)}))" />
            </div>

            <div class="form-grid">
                <div>
                    <label class="form-label">Mois</label>
                    <select x-model="payslipForm.month" x-on:change="onPayslipContractChanged()" class="input-field mt-1 font-semibold">
                        <template x-for="m in 12" :key="m">
                            <option :value="m" x-text="monthLabel(m)" class="font-semibold"></option>
                        </template>
                    </select>
                </div>
                <div>
                    <label class="form-label">Année</label>
                    <input type="number" x-model="payslipForm.year" x-on:change="onPayslipContractChanged()" class="input-field mt-1" />
                </div>
            </div>

            <div>
                <label class="form-label">Heures travaillées (contrat Vacataire)</label>
                <input type="number" min="0" x-model="payslipForm.hoursWorked" class="input-field mt-1" />

                <!-- Suggestion d'heures (ticket JGK-K01) — purement consultative, n'écrit jamais le champ toute seule. -->
                <div x-show="payslipContractIsVacataire" x-cloak class="mt-2">
                    <button type="button" x-on:click="loadSuggestedHours()" :disabled="isLoadingSuggestion" class="btn-modal-secondary text-xs py-1.5 px-3">
                        <span x-show="!isLoadingSuggestion">Suggérer les heures depuis le pointage</span>
                        <span x-show="isLoadingSuggestion">Calcul en cours…</span>
                    </button>

                    <p x-show="suggestionError" x-cloak class="field-error mt-1" x-text="suggestionError"></p>

                    <div x-show="suggestedHours" x-cloak class="mt-2 rounded-xl border-l-4 border-primary bg-primary-50 p-3 text-sm">
                        <p class="text-slate-700">
                            Heures pointées ce mois-ci : <span class="font-semibold" x-text="suggestedHours ? suggestedHours.suggestedHours : 0"></span> h.
                        </p>
                        <button type="button" x-on:click="applySuggestedHours()" class="mt-1 text-xs font-medium text-primary hover:underline">
                            Utiliser cette suggestion
                        </button>
                        <div x-show="suggestedHours && suggestedHours.discrepancies.length > 0" x-cloak class="mt-2 pt-2 border-t border-primary-100">
                            <p class="text-xs font-medium text-slate-600">Écarts avec l'emploi du temps planifié (informatif) :</p>
                            <ul class="mt-1 space-y-0.5">
                                <template x-for="d in (suggestedHours ? suggestedHours.discrepancies : [])" :key="d.date">
                                    <li class="text-xs text-slate-500">
                                        <span x-text="formatDate(d.date)"></span> — pointé <span x-text="d.declaredHours"></span> h, planifié <span x-text="d.scheduledHours"></span> h
                                    </li>
                                </template>
                            </ul>
                        </div>
                    </div>
                </div>
            </div>
            <div>
                <label class="form-label">Prime de transport (FCFA)</label>
                <input type="number" min="0" x-model="payslipForm.transportAllowance" class="input-field mt-1" />
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isPayslipModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isGeneratingPayslip">
                    <span x-show="!isGeneratingPayslip">Générer</span>
                    <span x-show="isGeneratingPayslip">Génération...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Générer une déclaration fiscale ===================== -->
    <modal-shell size="60" open="isTaxModalOpen" title="Générer une déclaration fiscale">
        <form x-on:submit.prevent="submitTaxDeclaration()" novalidate class="space-y-5">
            <p class="text-base font-medium leading-relaxed text-slate-700">Agrège les charges sociales de toutes les fiches de paie du mois choisi.</p>
            <div class="form-grid">
                <div>
                    <label class="form-label">Mois</label>
                    <select x-model="taxForm.month" class="input-field mt-1 font-semibold">
                        <template x-for="m in 12" :key="m">
                            <option :value="m" x-text="monthLabel(m)" class="font-semibold"></option>
                        </template>
                    </select>
                </div>
                <div>
                    <label class="form-label">Année</label>
                    <input type="number" x-model="taxForm.year" class="input-field mt-1" />
                </div>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isTaxModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isGeneratingTax">
                    <span x-show="!isGeneratingTax">Générer</span>
                    <span x-show="isGeneratingTax">Génération...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    <!-- ===================== Modale : Fiche heures Vacataire ===================== -->
    <modal-shell size="60" open="isHourRecordsModalOpen" title="Fiche heures — Vacataire">
        <modal-subtitle><span x-text="hourRecordsContract ? hourRecordsContract.employeeFullName : ''"></span></modal-subtitle>

        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end">
            <div>
                <label class="form-label-sm">Mois</label>
                <select x-model="hourRecordsFilter.month" x-on:change="loadHourRecords()" class="input-field mt-0 font-semibold">
                    <template x-for="m in 12" :key="m">
                        <option :value="m" x-text="monthLabel(m)" class="font-semibold"></option>
                    </template>
                </select>
            </div>
            <div>
                <label class="form-label-sm">Année</label>
                <input type="number" x-model="hourRecordsFilter.year" x-on:change="loadHourRecords()" class="input-field mt-0 w-28" />
            </div>
            <button type="button" x-on:click="previewHourRecordSheet()" :disabled="downloadingHourRecordSheet" class="btn-modal-secondary group">
                <icon name="download" class="w-4 h-4" />
                <span x-text="downloadingHourRecordSheet ? 'Génération…' : 'Fiche heures (PDF)'"></span>
            </button>
        </div>

        <div class="mt-5 overflow-x-auto rounded-xl border border-slate-200">
            <table class="w-full text-left border-collapse">
                <thead>
                    <tr class="table-head">
                        <th class="py-2 px-3">Date</th>
                        <th class="py-2 px-3 text-right">Heures</th>
                        <th class="py-2 px-3">Note</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    <tr x-show="hourRecords.length === 0">
                        <td colspan="3" class="py-6 text-center text-sm text-slate-400">Aucune heure enregistrée sur cette période.</td>
                    </tr>
                    <template x-for="record in hourRecords" :key="record.id">
                        <tr>
                            <td class="py-2 px-3 text-slate-600" x-text="record.date"></td>
                            <td class="py-2 px-3 text-right text-slate-600" x-text="record.hours"></td>
                            <td class="py-2 px-3 text-slate-500" x-text="record.note"></td>
                        </tr>
                    </template>
                </tbody>
            </table>
        </div>

        <form x-on:submit.prevent="submitHourRecord()" novalidate class="mt-5 grid grid-cols-1 sm:grid-cols-3 gap-4 items-end border-t border-slate-100 pt-4">
            <div>
                <label class="form-label">Date <span class="text-red-500">*</span></label>
                <date-field model="hourRecordForm.date" required="true" aria-label="Date" class="mt-1" />
            </div>
            <div>
                <label class="form-label">Heures <span class="text-red-500">*</span></label>
                <input type="number" min="0" step="0.5" x-model="hourRecordForm.hours" required class="input-field mt-1" />
            </div>
            <div>
                <label class="form-label">Note</label>
                <input type="text" x-model="hourRecordForm.note" class="input-field mt-1" />
            </div>
            <div class="sm:col-span-3 flex justify-end">
                <button type="submit" class="btn-modal-primary group" :disabled="isSavingHourRecord">
                    <span x-show="!isSavingHourRecord">Ajouter les heures</span>
                    <span x-show="isSavingHourRecord">Enregistrement...</span>
                </button>
            </div>
        </form>

        <div class="mt-4 flex justify-end">
            <button type="button" x-on:click="isHourRecordsModalOpen = false" class="btn-modal-secondary group">Fermer</button>
        </div>
    </modal-shell>

    @* Aperçu PDF partagé — voir wwwroot/js/pdf-preview.js *@
    @await Html.PartialAsync("_PdfPreviewModal")
</div>
````


### A.1.4 Fiscalité (`/fiscalite`)

#### `src/SamaEcole.Web/Views/Taxes/Index.cshtml`

````html
@{
    ViewData["Title"] = "Fiscalité";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
    <script src="~/js/taxes.js" asp-append-version="true"></script>
}

<div x-data="taxesView()" class="space-y-6">
    <!-- ===================== En-tête + sélecteur de période ===================== -->
    <div class="page-header">
        <div>
            <h1 class="page-title">Fiscalité</h1>
            <p class="page-subtitle">Déclarations mensuelles VRS / IPRES / CSS / BRS et TVA.</p>
        </div>
        <div class="flex flex-wrap items-end gap-3">
            <div class="w-44">
                <label class="form-label-sm">Mois</label>
                <select-field model="periodMonth" placeholder="Mois" aria-label="Mois de la période fiscale"
                    on-change="onPeriodChange()" class="!mt-0"
                    options-expr="monthOptions()" />
            </div>
            <div class="w-36">
                <label class="form-label-sm">Année fiscale</label>
                <select-field model="periodYear" placeholder="Année" aria-label="Année fiscale"
                    on-change="onYearChange()" class="!mt-0"
                    options-expr="yearOptions()" />
            </div>
        </div>
    </div>

    @await Html.PartialAsync("_ErrorBanner")

    <!-- ===================== Récapitulatif : cartes KPI de la période ===================== -->
    <div>
        <div class="flex items-center justify-between mb-3">
            <h2 class="section-title">Récapitulatif</h2>
            <span class="text-xs font-medium" :class="selectedDeclaration ? 'text-slate-500' : 'text-amber-600'"
                  x-text="selectedDeclaration ? periodLabel() : periodLabel() + ' — aucune déclaration générée'"></span>
        </div>

        @* Les huit cartes de synthèse étaient écrites à la main, avec leur propre gabarit : `font-black`
           là où tout le reste de l'application pose `font-bold`, pastille d'icône `rounded-full` là où
           le design system 2026 pose un conteneur doux, et un libellé en `text-slate-400` qui tombait
           sous le seuil de contraste AA. Elles passent au composant partagé — mêmes montants, même
           ordre, mêmes teintes, mais le même gabarit qu'en Trésorerie et au Tableau de bord. *@

        <!-- Ligne 1 : cotisations sociales & retraite -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <stat-card label="IPRES" icon="shield" accent="primary">
                <span x-text="amountOf('totalIpres')"></span>
                <stat-hint>Retraite — part salariale + patronale</stat-hint>
            </stat-card>

            <stat-card label="CSS" icon="users" accent="blue">
                <span x-text="amountOf('totalCss')"></span>
                <stat-hint>Caisse de Sécurité Sociale</stat-hint>
            </stat-card>

            <stat-card label="VRS" icon="document" accent="slate">
                <span x-text="amountOf('totalVrs')"></span>
                <stat-hint>Versement représentatif de l'impôt</stat-hint>
            </stat-card>

            <stat-card label="BRS" icon="percent" accent="purple">
                <span x-text="amountOf('totalBrs')"></span>
                <stat-hint>Retenue à la source sur salaires</stat-hint>
            </stat-card>
        </div>

        <!-- Ligne 2 : TVA & synthèse État -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mt-4">
            <stat-card label="TVA collectée" icon="arrow-up" accent="warning">
                <span x-text="amountOf('tvaCollected')"></span>
                <stat-hint>Facturée sur la période</stat-hint>
            </stat-card>

            <stat-card label="TVA déductible" icon="arrow-down" accent="success">
                <span x-text="amountOf('tvaDeductible')"></span>
                <stat-hint>Récupérable sur les achats</stat-hint>
            </stat-card>

            <stat-card label="TVA nette" icon="payment" accent="blue">
                <span x-text="amountOf('netTva')"></span>
                <stat-hint>Collectée − déductible</stat-hint>
            </stat-card>

            @* Le seul indicateur de TÊTE de l'écran — c'est le montant que l'établissement versera
               réellement. Il porte donc le liseré d'accent, comme les indicateurs majeurs du tableau
               de bord, plutôt qu'un fond teinté qui n'existe nulle part ailleurs. *@
            <stat-card label="Total dû à l'État" icon="building" accent="primary" emphasis>
                <span x-text="amountOf('totalDueToState')"></span>
                <stat-hint>VRS + BRS + TVA nette</stat-hint>
            </stat-card>
        </div>

        <template x-if="!isLoading && !selectedDeclaration">
            <div class="card p-6 mt-4">
                <empty-state icon="document" title="Aucune déclaration pour cette période."
                    hint="Générez une déclaration pour un mois où des fiches de paie existent." />
            </div>
        </template>
    </div>

    <!-- ===================== Historique des déclarations ===================== -->
    <div class="card">
        <div class="card-header">
            <div>
                <p class="text-sm font-semibold text-slate-900">Historique des déclarations</p>
                <p class="text-xs text-slate-500 mt-0.5">Année fiscale <span class="font-semibold text-slate-700" x-text="periodYear"></span> — filtrée par le sélecteur en haut de page.</p>
            </div>
            <button type="button" x-on:click="openTaxModal()" class="btn-modal-primary group">
                <icon name="plus" class="w-4 h-4 transition-transform duration-200 group-hover:rotate-90" />
                Générer une déclaration
            </button>
        </div>
        <div class="overflow-x-auto">
            <table class="w-full text-left border-collapse">
                <thead>
                    <tr class="table-head">
                        <th class="py-3 px-4">Période</th>
                        <th class="py-3 px-4 text-right">IPRES</th>
                        <th class="py-3 px-4 text-right">CSS</th>
                        <th class="py-3 px-4 text-right">VRS</th>
                        <th class="py-3 px-4 text-right">BRS</th>
                        <th class="py-3 px-4 text-right">TVA nette</th>
                        <th class="py-3 px-4 text-right">Total dû à l'État</th>
                        <th class="py-3 px-4 text-right">État synthétique</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    <tr x-show="!isLoading && taxDeclarations.length === 0" x-cloak>
                        <td colspan="8">
                            <empty-state icon="document" title="Aucune déclaration sur cette année."
                                hint="Générez une déclaration pour un mois où des fiches de paie existent." />
                        </td>
                    </tr>
                    <template x-for="declaration in taxDeclarations" :key="declaration.id">
                        <tr class="hover:bg-slate-50 transition-colors"
                            :class="declaration.month === Number(periodMonth) ? 'bg-primary-50/40' : ''">
                            <td class="py-3 px-4 font-medium text-slate-900" x-text="monthLabel(declaration.month) + ' ' + declaration.year"></td>
                            <td class="py-3 px-4 text-right amount text-slate-600" x-text="formatAmount(declaration.totalIpres)"></td>
                            <td class="py-3 px-4 text-right amount text-slate-600" x-text="formatAmount(declaration.totalCss)"></td>
                            <td class="py-3 px-4 text-right amount text-slate-600" x-text="formatAmount(declaration.totalVrs)"></td>
                            <td class="py-3 px-4 text-right amount text-slate-600" x-text="formatAmount(declaration.totalBrs)"></td>
                            <td class="py-3 px-4 text-right amount text-slate-600" x-text="formatAmount(declaration.netTva)"></td>
                            <td class="py-3 px-4 text-right amount font-semibold text-slate-900" x-text="formatAmount(declaration.totalDueToState)"></td>
                            <td class="py-3 px-4 text-right">
                                <button type="button" x-on:click="downloadDeclarationPdf(declaration.id)" :disabled="downloadingPdfId === declaration.id"
                                        class="btn-modal-secondary inline-flex items-center gap-2 disabled:opacity-60 group">
                                    <icon name="download" class="w-4 h-4" x-show="downloadingPdfId !== declaration.id" />
                                    <icon name="loader" class="w-4 h-4 animate-spin" x-show="downloadingPdfId === declaration.id" x-cloak />
                                    <span x-text="downloadingPdfId === declaration.id ? 'Génération...' : 'PDF'"></span>
                                </button>
                            </td>
                        </tr>
                    </template>
                </tbody>
            </table>
        </div>
    </div>

    <!-- ===================== Modale : Générer une déclaration fiscale ===================== -->
    <modal-shell size="60" open="isTaxModalOpen" title="Générer une déclaration fiscale">
        <form x-on:submit.prevent="submitTaxDeclaration()" novalidate class="space-y-5">
            <p class="text-base font-medium leading-relaxed text-slate-700">Agrège les charges sociales de toutes les fiches de paie du mois choisi, ainsi que la TVA collectée/déductible de la période.</p>
            <div class="form-grid">
                <div>
                    <label class="form-label">Mois</label>
                    <select-field model="taxForm.month" placeholder="Sélectionnez un mois..."
                        options-expr="[{value:1,label:'Janvier'},{value:2,label:'Février'},{value:3,label:'Mars'},{value:4,label:'Avril'},{value:5,label:'Mai'},{value:6,label:'Juin'},{value:7,label:'Juillet'},{value:8,label:'Août'},{value:9,label:'Septembre'},{value:10,label:'Octobre'},{value:11,label:'Novembre'},{value:12,label:'Décembre'}]" />
                </div>
                <div>
                    <label class="form-label">Année</label>
                    <input type="number" x-model="taxForm.year" class="input-field mt-1" />
                </div>
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
                <button type="button" x-on:click="isTaxModalOpen = false" class="btn-modal-secondary group">Annuler</button>
                <button type="submit" class="btn-modal-primary group" :disabled="isGeneratingTax">
                    <span x-show="!isGeneratingTax">Générer</span>
                    <span x-show="isGeneratingTax">Génération...</span>
                </button>
            </div>
        </form>
    </modal-shell>

    @* Aperçu PDF partagé — voir wwwroot/js/pdf-preview.js *@
    @await Html.PartialAsync("_PdfPreviewModal")
</div>
````


### A.1.5 Trésorerie (`/tresorerie`)

#### `src/SamaEcole.Web/Views/Treasury/Index.cshtml`

````html
@{
    ViewData["Title"] = "Trésorerie";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
    <script src="~/js/treasury.js" asp-append-version="true"></script>
}

<div x-data="treasuryView()">
    <div class="page-header">
        <div>
            <h1 class="page-title">Trésorerie</h1>
            <p class="page-subtitle">Position de caisse sur une période : encaissements (Caisse) et décaissements combinés.</p>
        </div>
    </div>

    <!-- Cartes de synthèse -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mt-6">
        <template x-if="isLoading">
            <template x-for="i in 4" :key="i">
                <div class="card p-5 h-[92px] skeleton"></div>
            </template>
        </template>

        <template x-if="!isLoading && data">
            <stat-card label="Total encaissé" icon="wallet" accent="success" emphasis>
                <span x-text="formatAmount(data.totalCollected)"></span>
            </stat-card>
        </template>
        <template x-if="!isLoading && data">
            <stat-card label="Total décaissé" icon="document" accent="danger" emphasis>
                <span x-text="formatAmount(data.totalDisbursed)"></span>
            </stat-card>
        </template>
        <template x-if="!isLoading && data">
            @* Le solde net était le seul indicateur dont la teinte devait suivre la donnée, et c'était
               le seul qui n'y arrivait pas : l'ancien `:accent="… ? 'primary' : 'danger'"` visait un
               attribut du Tag Helper, or celui-ci efface sa propre balise au rendu — la liaison Alpine
               partait avec. Un découvert s'affichait donc en indigo, comme un encaissement. *@
            <stat-card label="Solde net" icon="chart-multiple" accent="primary" emphasis
                       value-class-expr="(data.netPosition ?? 0) >= 0 ? 'text-primary-700' : 'text-rose-700'">
                <span x-text="formatAmount(data.netPosition)"></span>
            </stat-card>
        </template>
        <template x-if="!isLoading && data">
            <stat-card label="Reste à recouvrer" icon="alert-circle" accent="warning" emphasis>
                <span x-text="formatAmount(data.totalOutstandingDebt)"></span>
            </stat-card>
        </template>
    </div>

    @await Html.PartialAsync("_ErrorBanner")

    <div class="card mt-6 relative">
        <!-- Filtre de période -->
        <div class="filter-bar lg:items-end lg:justify-between">
            <div class="flex flex-wrap items-end gap-3">
                <div>
                    <label class="form-label-sm">Du</label>
                    <date-field model="startDate" aria-label="Date de début" class="mt-0" />
                </div>
                <div>
                    <label class="form-label-sm">Au</label>
                    <date-field model="endDate" aria-label="Date de fin" class="mt-0" />
                </div>
                <button type="button" x-on:click="applyFilters()" class="btn-modal-primary group">
                    <icon name="chart-multiple" class="w-4 h-4" />
                    Actualiser
                </button>
                <button type="button" x-on:click="resetFilters()" class="text-sm font-medium text-slate-500 hover:text-slate-900">
                    Réinitialiser
                </button>
            </div>
            <span class="text-sm text-slate-500 sm:self-end" x-show="data" x-text="periodLabel()"></span>
        </div>

        @* Répartitions — chaque ligne porte désormais sa PART sous forme de jauge, en plus du montant.
           Une colonne de quatre montants alignés à droite ne dit pas lequel pèse, et l'oeil doit
           comparer des nombres de longueurs différentes ; la barre le dit d'un coup d'oeil. La jauge
           est relative au TOTAL de sa colonne (encaissé / décaissé), pas au plus gros poste : « 60 %
           des encaissements en espèces » est l'information utile au directeur, pas « deux fois plus
           que le mobile money ». *@
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 p-6 border-b border-slate-200/70">
            <div>
                <h2 class="section-title mb-3">Encaissements par mode de paiement</h2>
                <div class="space-y-3">
                    <template x-if="!isLoading && data && data.collectedByMethod.length === 0">
                        <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                    </template>
                    <template x-for="item in (data ? data.collectedByMethod : [])" :key="item.label">
                        <div>
                            <div class="flex items-baseline justify-between gap-3 mb-1.5">
                                <span class="flex items-baseline gap-2 min-w-0">
                                    <span class="text-sm text-slate-600 truncate" x-text="item.label"></span>
                                    <span class="text-xs font-medium text-slate-400 shrink-0"
                                          x-text="shareLabel(item.amount, data.totalCollected)"></span>
                                </span>
                                <span class="amount text-sm font-semibold text-slate-900" x-text="formatAmount(item.amount)"></span>
                            </div>
                            <div class="progress-track">
                                <div class="progress-fill-success" :style="'width: ' + sharePct(item.amount, data.totalCollected) + '%'"></div>
                            </div>
                        </div>
                    </template>
                </div>
            </div>
            <div>
                <h2 class="section-title mb-3">Décaissements par catégorie</h2>
                <div class="space-y-3">
                    <template x-if="!isLoading && data && data.disbursedByCategory.length === 0">
                        <p class="text-sm text-slate-400">Aucun décaissement sur cette période.</p>
                    </template>
                    <template x-for="item in (data ? data.disbursedByCategory : [])" :key="item.label">
                        <div>
                            <div class="flex items-baseline justify-between gap-3 mb-1.5">
                                <span class="flex items-baseline gap-2 min-w-0">
                                    <span class="text-sm text-slate-600 truncate" x-text="item.label"></span>
                                    <span class="text-xs font-medium text-slate-400 shrink-0"
                                          x-text="shareLabel(item.amount, data.totalDisbursed)"></span>
                                </span>
                                <span class="amount text-sm font-semibold text-slate-900" x-text="formatAmount(item.amount)"></span>
                            </div>
                            <div class="progress-track">
                                <div class="progress-fill-danger" :style="'width: ' + sharePct(item.amount, data.totalDisbursed) + '%'"></div>
                            </div>
                        </div>
                    </template>
                </div>
            </div>
        </div>

        <!-- Fil d'activité récent -->
        <div class="overflow-x-auto">
            <table class="w-full text-left border-collapse">
                <thead>
                    <tr class="table-head">
                        <th class="py-3 px-4">Type</th>
                        <th class="py-3 px-4">Référence</th>
                        <th class="py-3 px-4">Date</th>
                        <th class="py-3 px-4 text-right">Montant</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    <template x-if="isLoading">
                        <template x-for="i in 5" :key="i">
                            <tr><td class="py-3 px-4" colspan="4"><div class="skeleton h-4 w-full"></div></td></tr>
                        </template>
                    </template>

                    <tr x-show="!isLoading && (!data || data.recentTransactions.length === 0)" x-cloak>
                        <td colspan="4">
                            <empty-state icon="document" title="Aucun mouvement sur cette période."
                                hint="Les encaissements de la Caisse et les décaissements enregistrés apparaîtront ici." />
                        </td>
                    </tr>

                    <template x-if="!isLoading && data">
                        <template x-for="(tx, index) in data.recentTransactions" :key="tx.kind + '-' + tx.date + '-' + index">
                            <tr class="hover:bg-slate-50 transition-colors">
                                <td class="py-3 px-4">
                                    @* Le libellé est porté par un ENFANT et non par un x-text sur la pastille
                                       elle-même : Alpine remplace tous les enfants de l'élément qu'il pilote,
                                       ce qui effacerait la puce d'état au premier rendu. *@
                                    <span class="status-badge" :class="kindBadgeClass(tx.kind)">
                                        <span class="status-dot" aria-hidden="true"></span>
                                        <span x-text="tx.kind"></span>
                                    </span>
                                </td>
                                <td class="py-3 px-4 text-slate-600" x-text="tx.label"></td>
                                <td class="py-3 px-4 text-slate-600" x-text="formatDate(tx.date)"></td>
                                <td class="py-3 px-4 text-right amount font-semibold"
                                    :class="tx.kind === 'Encaissement' ? 'text-emerald-700' : 'text-rose-700'"
                                    x-text="formatAmount(tx.amount)"></td>
                            </tr>
                        </template>
                    </template>
                </tbody>
            </table>
        </div>
    </div>
</div>
````


### A.1.6 Rapports financiers (`/rapports/financiers`)

#### `src/SamaEcole.Web/Views/Reports/Financial.cshtml`

````html
@{
    ViewData["Title"] = "Rapports financiers";
}

@* auth.js et api.js sont chargés par _Layout : les inclure ici les exécuterait une seconde fois. *@
@section Scripts {
    <script src="~/js/financial-report.js" asp-append-version="true"></script>
}

<div x-data="financialReportView()">
    <div class="page-header">
        <div>
            <h1 class="page-title">Rapports financiers</h1>
            <p class="page-subtitle">Consolidation des encaissements sur une période, ventilée par cycle, par classe et par mode de paiement.</p>
        </div>
    </div>

    <!-- Cartes de synthèse -->
    <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 mt-6">
        <template x-if="isLoading">
            <template x-for="i in 3" :key="i">
                <div class="card p-5 h-[92px] skeleton"></div>
            </template>
        </template>

        <template x-if="!isLoading && data">
            <stat-card label="Total encaissé" icon="wallet" accent="success">
                <span x-text="formatAmount(data.totalCollected)"></span>
            </stat-card>
        </template>
        <template x-if="!isLoading && data">
            <stat-card label="Paiements enregistrés" icon="payment" accent="primary">
                <span x-text="data.paymentCount"></span>
            </stat-card>
        </template>
        <template x-if="!isLoading && data">
            <stat-card label="Période" icon="document" accent="primary">
                <span class="text-base" x-text="periodLabel()"></span>
            </stat-card>
        </template>
    </div>

    @await Html.PartialAsync("_ErrorBanner")

    <div class="card mt-6 relative">
        <!-- Filtre de période + export comptable -->
        <div class="filter-bar lg:items-end lg:justify-between">
            <div class="flex flex-wrap items-end gap-3">
                <div>
                    <label class="form-label-sm">Du</label>
                    <date-field model="from" aria-label="Date de début" class="mt-0" />
                </div>
                <div>
                    <label class="form-label-sm">Au</label>
                    <date-field model="to" aria-label="Date de fin" class="mt-0" />
                </div>
                <button type="button" x-on:click="applyFilters()" class="btn-modal-primary group">
                    <icon name="chart-multiple" class="w-4 h-4" />
                    Générer
                </button>
                <button type="button" x-on:click="resetFilters()" class="text-sm font-medium text-slate-500 hover:text-slate-900">
                    Réinitialiser
                </button>
            </div>

            @* Export .xlsx : conserve les bornes de période affichées — c'est le MÊME rapport, au format
               comptable (GET /finance/reports/revenue/excel). Le bouton est désactivé plutôt que masqué
               quand la formule ne l'inclut pas : une fonctionnalité invisible ne se vend pas (features.js).
               Le verrou est porté par le composant parent (et non par `featureGate`) pour que exportExcel()
               s'exécute dans la MÊME portée Alpine que `exporting` et `data`. *@
            <div>
                <button type="button" x-on:click="exportExcel()" :disabled="exporting || !data || !featureAllowed"
                        class="btn-modal-secondary group" :title="featureAllowed ? '' : featureLabel">
                    <icon name="download" class="w-4 h-4" />
                    <span x-text="exporting ? 'Export…' : 'Télécharger le rapport financier .xlsx'"></span>
                </button>
                <p x-show="!featureAllowed" x-cloak class="text-xs text-slate-400 mt-1 text-right" x-text="featureLabel"></p>
            </div>
        </div>

        @* Visualisation graphique — même méthode que le tableau de bord financier (Dashboard/Index.cshtml
           et dashboard.js) : SVG inline (astuce stroke-dasharray sur un cercle de circonférence 100,
           r="15.9155") pour le donut, barres CSS en pourcentage pour les séries. Volontairement PAS de
           librairie de graphes (Chart.js ou autre) : cette technique est déjà celle en place partout
           ailleurs dans l'application, sans dépendance JS supplémentaire à charger sur une connexion
           mobile sénégalaise moyenne (Volume 5 §1). *@
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 p-6 border-b border-slate-200/70">
            <!-- Évolution mensuelle des recettes -->
            <div class="lg:col-span-2">
                <h2 class="section-title mb-3">Évolution mensuelle des recettes</h2>
                <template x-if="!isLoading && data && data.byMonth.length === 0">
                    <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                </template>
                <template x-if="!isLoading && data && data.byMonth.length > 0">
                    <div class="flex items-end gap-2 sm:gap-3 h-40 bg-slate-50/80 rounded-xl border border-slate-100 p-4">
                        <template x-for="month in data.byMonth" :key="month.period">
                            <div class="flex-1 h-full flex flex-col items-center justify-end gap-1.5 group">
                                <span class="text-[11px] font-semibold text-slate-600 opacity-0 group-hover:opacity-100 transition-opacity amount"
                                      x-text="formatAmount(month.amount)"></span>
                                <div class="w-full max-w-[2.5rem] rounded-t-md bg-primary-500 transition-all duration-500 ease-out"
                                     :style="'height: ' + monthBarHeight(month.amount) + '%'"></div>
                                <span class="text-[11px] text-slate-500 font-medium" x-text="formatMonth(month.period)"></span>
                            </div>
                        </template>
                    </div>
                </template>
            </div>

            <!-- Répartition par mode de paiement (donut) -->
            <div>
                <h2 class="section-title mb-3">Répartition par mode de paiement</h2>
                <template x-if="!isLoading && data && data.byPaymentMethod.length === 0">
                    <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                </template>
                <template x-if="!isLoading && data && data.byPaymentMethod.length > 0">
                    <div class="flex flex-col sm:flex-row items-center gap-4 bg-slate-50/80 rounded-xl border border-slate-100 p-4">
                        <div class="relative w-24 h-24 shrink-0">
                            @* 7 <circle> fixes plutôt qu'un <template x-for="segment in ..."> : un <template>
                               imbriqué dans un <svg> voit TOUJOURS son contenu analysé en namespace HTML, y
                               compris à cet endroit — un <circle> qui en sortirait ne serait qu'un élément
                               inconnu, invisible sans la moindre erreur visible à l'écran (paymentMethodSegments(),
                               financial-report.js, retourne un tableau de longueur fixe pour cette raison). *@
                            <svg viewBox="0 0 36 36" class="w-full h-full -rotate-90 drop-shadow-sm">
                                <circle cx="18" cy="18" r="15.9155" fill="none" stroke="#E5E7EB" stroke-width="4"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[0].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[0].fraction + ' ' + (100 - paymentMethodSegments()[0].fraction)" :stroke-dashoffset="paymentMethodSegments()[0].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[1].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[1].fraction + ' ' + (100 - paymentMethodSegments()[1].fraction)" :stroke-dashoffset="paymentMethodSegments()[1].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[2].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[2].fraction + ' ' + (100 - paymentMethodSegments()[2].fraction)" :stroke-dashoffset="paymentMethodSegments()[2].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[3].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[3].fraction + ' ' + (100 - paymentMethodSegments()[3].fraction)" :stroke-dashoffset="paymentMethodSegments()[3].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[4].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[4].fraction + ' ' + (100 - paymentMethodSegments()[4].fraction)" :stroke-dashoffset="paymentMethodSegments()[4].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[5].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[5].fraction + ' ' + (100 - paymentMethodSegments()[5].fraction)" :stroke-dashoffset="paymentMethodSegments()[5].offset" class="transition-all duration-500 ease-out"></circle>
                                <circle cx="18" cy="18" r="15.9155" fill="none" :stroke="paymentMethodSegments()[6].color" stroke-width="4" stroke-linecap="round" :stroke-dasharray="paymentMethodSegments()[6].fraction + ' ' + (100 - paymentMethodSegments()[6].fraction)" :stroke-dashoffset="paymentMethodSegments()[6].offset" class="transition-all duration-500 ease-out"></circle>
                            </svg>
                        </div>
                        <ul class="flex-1 w-full grid grid-cols-1 gap-1.5">
                            <template x-for="(item, idx) in data.byPaymentMethod" :key="item.label">
                                <li class="flex items-center justify-between text-xs bg-white px-2.5 py-1.5 rounded-lg shadow-sm border border-slate-100">
                                    <span class="flex items-center gap-1.5 min-w-0">
                                        <span class="w-2.5 h-2.5 rounded-full shrink-0" :style="'background-color: ' + getCategoryColor(idx)"></span>
                                        <span class="text-slate-600 font-medium truncate" x-text="item.label"></span>
                                    </span>
                                    <span class="font-bold text-slate-900 ml-2 shrink-0" x-text="formatPercent(share(item.amount))"></span>
                                </li>
                            </template>
                        </ul>
                    </div>
                </template>
            </div>

            <!-- Répartition du chiffre d'affaires par classe -->
            <div>
                <h2 class="section-title mb-3">Chiffre d'affaires par classe</h2>
                <template x-if="!isLoading && data && data.byClassroom.length === 0">
                    <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                </template>
                <template x-if="!isLoading && data && data.byClassroom.length > 0">
                    <div class="flex flex-col gap-3 bg-slate-50/80 rounded-xl border border-slate-100 p-4">
                        <template x-for="row in topClassrooms()" :key="row.label">
                            <div>
                                <div class="flex justify-between text-xs mb-1">
                                    <span class="text-slate-600 font-medium truncate" x-text="row.label"></span>
                                    <span class="font-bold text-slate-900 shrink-0 ml-2 amount" x-text="formatAmount(row.amount)"></span>
                                </div>
                                <div class="progress-track">
                                    <div class="progress-fill-primary"
                                         :style="'width: ' + pctVal(row.amount, data.byClassroom[0].amount) + '%'"></div>
                                </div>
                            </div>
                        </template>
                        <p x-show="data.byClassroom.length > topClassrooms().length" x-cloak
                           class="text-xs text-slate-400 text-center pt-1">
                            + <span x-text="data.byClassroom.length - topClassrooms().length"></span> autre(s) classe(s) — voir le tableau ci-dessous.
                        </p>
                    </div>
                </template>
            </div>
        </div>

        @* Ventilation par cycle et par mode de paiement — la part de chaque poste était déjà écrite en
           toutes lettres à côté du montant ; elle est désormais AUSSI portée par une jauge. Le
           pourcentage seul oblige à comparer mentalement quatre nombres ; la barre range les postes
           dans l'ordre en un coup d'oeil, sans rien retirer du chiffre exact. `share()` rapporte au
           total encaissé de la période (financial-report.js), donc les barres d'une même colonne se
           comparent entre elles. *@
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6 p-6 border-b border-slate-200/70">
            <div>
                <h2 class="section-title mb-3">Encaissements par cycle</h2>
                <div class="space-y-3">
                    <template x-if="!isLoading && data && data.byCycle.length === 0">
                        <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                    </template>
                    <template x-for="item in (data ? data.byCycle : [])" :key="item.label">
                        <div>
                            <div class="flex items-baseline justify-between gap-3 mb-1.5">
                                <span class="flex items-baseline gap-2 min-w-0">
                                    <span class="text-sm text-slate-600 truncate" x-text="item.label"></span>
                                    <span class="text-xs font-medium text-slate-400 shrink-0" x-text="formatPercent(share(item.amount))"></span>
                                </span>
                                <span class="amount text-sm font-semibold text-slate-900" x-text="formatAmount(item.amount)"></span>
                            </div>
                            <div class="progress-track">
                                <div class="progress-fill-primary" :style="'width: ' + pctVal(item.amount, data.totalCollected) + '%'"></div>
                            </div>
                        </div>
                    </template>
                </div>
            </div>
            <div>
                <h2 class="section-title mb-3">Encaissements par mode de paiement</h2>
                <div class="space-y-3">
                    <template x-if="!isLoading && data && data.byPaymentMethod.length === 0">
                        <p class="text-sm text-slate-400">Aucun encaissement sur cette période.</p>
                    </template>
                    <template x-for="item in (data ? data.byPaymentMethod : [])" :key="item.label">
                        <div>
                            <div class="flex items-baseline justify-between gap-3 mb-1.5">
                                <span class="flex items-baseline gap-2 min-w-0">
                                    <span class="text-sm text-slate-600 truncate" x-text="item.label"></span>
                                    <span class="text-xs font-medium text-slate-400 shrink-0" x-text="formatPercent(share(item.amount))"></span>
                                </span>
                                <span class="amount text-sm font-semibold text-slate-900" x-text="formatAmount(item.amount)"></span>
                            </div>
                            <div class="progress-track">
                                <div class="progress-fill-success" :style="'width: ' + pctVal(item.amount, data.totalCollected) + '%'"></div>
                            </div>
                        </div>
                    </template>
                </div>
            </div>
        </div>

        <!-- Ventilation détaillée par classe -->
        <div class="overflow-x-auto">
            <table class="w-full text-left border-collapse">
                <thead>
                    <tr class="table-head">
                        <th class="py-3 px-4">Classe</th>
                        <th class="py-3 px-4 text-center">Paiements</th>
                        <th class="py-3 px-4 text-right">Part</th>
                        <th class="py-3 px-4 text-right">Montant encaissé</th>
                    </tr>
                </thead>
                <tbody class="divide-y divide-slate-100">
                    <template x-if="isLoading">
                        <template x-for="i in 5" :key="i">
                            <tr><td class="py-3 px-4" colspan="4"><div class="skeleton h-4 w-full"></div></td></tr>
                        </template>
                    </template>

                    <tr x-show="!isLoading && (!data || data.byClassroom.length === 0)" x-cloak>
                        <td colspan="4">
                            <empty-state icon="wallet" title="Aucun encaissement sur cette période."
                                hint="Ajustez les dates ; les paiements enregistrés à la Caisse apparaîtront ici." />
                        </td>
                    </tr>

                    <template x-if="!isLoading && data">
                        <template x-for="row in data.byClassroom" :key="row.label">
                            <tr class="hover:bg-slate-50 transition-colors">
                                <td class="py-3 px-4 font-medium text-slate-900" x-text="row.label"></td>
                                <td class="py-3 px-4 text-center amount text-slate-600" x-text="row.paymentCount"></td>
                                <td class="py-3 px-4 text-right amount text-slate-500" x-text="formatPercent(share(row.amount))"></td>
                                <td class="py-3 px-4 text-right amount font-semibold text-slate-900" x-text="formatAmount(row.amount)"></td>
                            </tr>
                        </template>
                    </template>
                </tbody>
            </table>
        </div>
    </div>
</div>
````


## A.2 JavaScript (Alpine.js)


#### `src/SamaEcole.Web/wwwroot/js/caisse.js`

````javascript
/**
 * Écran Caisse — ticket JGK-F02.
 *
 * Flux en trois temps : rechercher un élève (réutilise GET /students, comme le sélecteur de
 * réinscription de /inscriptions) → afficher le solde de son inscription sur l'année active
 * (GET /finance/students/{id}/balance) → encaisser un versement (POST /finance/payments) et en
 * imprimer le reçu officiel.
 *
 * Le solde affiché n'est qu'un APPERÇU pour guider la saisie : c'est le serveur qui vérifie et fait foi
 * (RecordPaymentCommandHandler). Deux caissiers peuvent ouvrir la même fiche en même temps — si l'un
 * encaisse pendant que l'autre saisit, le second reçoit un 409 (verrou optimiste xmin, AGENTS.md règle
 * #5) : on ne réessaie JAMAIS silencieusement, on affiche le conflit et on force un rechargement du solde.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('caisseView', () => ({
        // Directeur, Finance et Secrétariat tiennent une caisse (ouvrent une session, encaissent, la
        // clôturent). La règle #4 sépare l'encaissement de la santé financière AGRÉGÉE (dashboards,
        // liste globale des paiements), qui reste Directeur/Finance. Confort d'affichage : l'API garde.
        canRecordPayment: ['Directeur', 'Finance', 'Secretariat'].includes(window.auth.role),

        // Annulation d'un versement saisi par erreur : état et méthodes partagés avec la fiche élève
        // (payment-cancel.js). Le modal est Views/Shared/_CancelPaymentModal.cshtml.
        ...window.paymentCancel.mixin(),

        /** Appelé par le mixin une fois le paiement annulé : le solde et l'historique ont changé. */
        async afterPaymentCancelled() {
            await this.reloadBalance();
        },

        students: [],
        studentsLoaded: false,
        isSearchingStudents: false,
        studentSearchError: null,
        studentSearch: '',
        selectedStudent: null,

        isLoadingBalance: false,
        balance: null,
        balanceError: null,
        conflictError: false,

        form: { amount: '', method: 'Cash' },
        formErrors: {},
        isSubmitting: false,

        // Guichet rapide (pop-up « Encaissement des frais dus ») : s'ouvre dès qu'un élève ayant un
        // montant échu est sélectionné. `checked` est aligné index par index sur quickPayRows() ; la
        // sélection est CONTIGUË — décocher une ligne décoche aussi toutes les suivantes, car
        // l'imputation en base suit l'ordre des échéances (InstallmentScheduleCalculator), donc seul
        // un préfixe de la liste peut passer proprement à « Réglé ».
        quickPay: { open: false, method: 'Cash', amount: '', checked: [] },

        // Résilience réseau (ticket JGK-L03) : clé générée à l'ouverture du formulaire d'encaissement
        // (voir startNewPayment/init), rejouée À L'IDENTIQUE par submitWithRetry à chaque tentative —
        // c'est elle qui garantit qu'un retry après coupure ne crée jamais un second paiement (JGK-L01).
        idempotencyKey: null,
        // 'sending' | 'retrying' | 'done' | 'failed' — piloté par onStateChange, affiché près du bouton.
        sendState: null,

        paymentResult: null, // résultat brut du POST (paymentId, receiptNumber…)
        receipt: null,       // reçu complet, chargé après coup pour l'affichage/l'impression

        // Parcours après encaissement : une fenêtre de confirmation (« Paiement validé ») s'affiche
        // d'abord ; le reçu ne s'affiche que si l'utilisateur choisit de l'imprimer/consulter.
        showConfirmDialog: false,
        showReceipt: false,

        // Marge restante sur la feuille A5, en mm (null tant que le reçu n'est pas rendu).
        // Négative = le contenu déborde ; l'impression le rognerait silencieusement.
        receiptFitMm: null,
        hasDraft: false,

        // ---------------------------------------------------------------- Session de caisse (Volume 1 §15.1)
        //
        // RecordPaymentCommandHandler refuse tout encaissement (422) hors d'une session de caisse
        // ouverte pour l'utilisateur courant — c'était déjà vrai côté serveur, mais rien ne l'exposait
        // à l'écran avant ce câblage (27/08/2026) : la recherche d'élève et le formulaire restent
        // masqués tant qu'aucune session n'est ouverte (voir x-show de la grille dans la vue).
        currentSession: null, // { sessionId, openingBalance, openedAt, totalCollected, paymentsCount }
        isLoadingSession: false,

        openSessionForm: { openingBalance: 0 },
        isOpeningSession: false,
        openSessionError: null,

        isCloseSessionModalOpen: false,
        isClosingSession: false,
        closeSessionError: null,
        // { sessionId, openingBalance, totalCollected, expectedClosingBalance, expectedCashAmount,
        //   actualCashAmount, discrepancyAmount, discrepancyReason }
        closeSessionResult: null,

        // Comptage physique obligatoire (ticket JGK-F09).
        closeSessionForm: { actualCashAmount: '', discrepancyReason: '' },
        // Révélé après un premier essai en 422 : la caisse ne tombe pas juste, un motif est requis.
        // Jamais affiché par anticipation — seul le serveur connaît les espèces attendues (fonds
        // initial + encaissements EN ESPÈCES uniquement), impossible à recalculer fiablement ici.
        closeSessionNeedsReason: false,

        async loadCurrentSession() {
            this.isLoadingSession = true;
            try {
                this.currentSession = await window.api.get('/finance/sessions/current');
            } catch {
                // silence-volontaire: 403 possible si le rôle n'a pas accès (canRecordPayment filtre
                // déjà l'appelant réel) ; dans tous les cas, l'absence de session se traite comme
                // "pas encore ouverte" — l'écran affiche alors son bandeau d'ouverture, qui EST la
                // bonne conduite à tenir. Un message d'erreur n'ajouterait rien d'actionnable.
                this.currentSession = null;
            } finally {
                this.isLoadingSession = false;
            }
        },

        async submitOpenSession() {
            this.isOpeningSession = true;
            this.openSessionError = null;
            try {
                await window.api.post('/finance/sessions/open', {
                    openingBalance: Number(this.openSessionForm.openingBalance) || 0
                });
                this.openSessionForm = { openingBalance: 0 };
                await this.loadCurrentSession();
            } catch (err) {
                this.openSessionError = window.api.toMessage(err, "Erreur lors de l'ouverture de la session de caisse.");
            } finally {
                this.isOpeningSession = false;
            }
        },

        openCloseSessionModal() {
            this.closeSessionError = null;
            this.closeSessionResult = null;
            this.closeSessionNeedsReason = false;
            this.closeSessionForm = { actualCashAmount: '', discrepancyReason: '' };
            this.isCloseSessionModalOpen = true;
        },

        closeCloseSessionModal() {
            this.isCloseSessionModalOpen = false;
        },

        /**
         * Comptage physique obligatoire (ticket JGK-F09) : Volume 1 §15.2 fige le solde théorique,
         * jamais re-clôturable ensuite. Un premier essai sans motif suffit tant que la caisse tombe
         * juste ; un écart renvoie 422 (DiscrepancyReason) et révèle le champ motif — jamais de
         * soumission automatique avec un motif deviné à la place du caissier.
         */
        async confirmCloseSession() {
            if (!this.currentSession) return;
            if (this.closeSessionForm.actualCashAmount === '' || this.closeSessionForm.actualCashAmount === null) {
                this.closeSessionError = 'Indiquez le montant réellement compté en caisse.';
                return;
            }
            this.isClosingSession = true;
            this.closeSessionError = null;
            try {
                this.closeSessionResult = await window.api.post(
                    `/finance/sessions/${this.currentSession.sessionId}/close`, {
                        actualCashAmount: Number(this.closeSessionForm.actualCashAmount) || 0,
                        discrepancyReason: this.closeSessionForm.discrepancyReason || null
                    });
                await this.loadCurrentSession(); // redevient null : la journée suivante en ouvrira une autre.
            } catch (err) {
                if (err.status === 422 && err.details && err.details.DiscrepancyReason) {
                    this.closeSessionNeedsReason = true;
                }
                this.closeSessionError = window.api.toMessage(err, 'Erreur lors de la clôture de la session.');
            } finally {
                this.isClosingSession = false;
            }
        },

        async downloadClosingReport() {
            if (!this.closeSessionResult) return;
            await this.openPdfPreview(
                `/api/v1/finance/sessions/${this.closeSessionResult.sessionId}/closing-report`,
                'Rapport de clôture de caisse',
                `Rapport-Cloture-${this.closeSessionResult.sessionId}.pdf`);
        },

        formatTime(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        },

        init() {
            this.idempotencyKey = window.networkGuard.newIdempotencyKey();
            this.initPaymentCancel();
            if (this.canRecordPayment) this.loadCurrentSession();
            if (window.formDraft && window.formDraft.has('caisse_form')) {
                this.hasDraft = true;
            }
            this.$watch('form', (val) => {
                if (window.formDraft && (val.amount || this.selectedStudent)) {
                    window.formDraft.save('caisse_form', {
                        form: val,
                        student: this.selectedStudent
                    });
                }
            });
        },

        restoreDraft() {
            if (!window.formDraft) return;
            const draft = window.formDraft.load('caisse_form');
            if (!draft) return;
            if (draft.student) {
                this.selectStudent(draft.student, { autoQuickPay: false }).then(() => {
                    if (draft.form) Object.assign(this.form, draft.form);
                });
            } else if (draft.form) {
                Object.assign(this.form, draft.form);
            }
            this.hasDraft = false;
        },

        clearDraft() {
            if (window.formDraft) window.formDraft.clear('caisse_form');
            this.hasDraft = false;
        },

        // ---------------------------------------------------------------- Recherche élève

        /**
         * Recherche CÔTÉ SERVEUR (GET /students?search=…), et non un chargement d'une page fixe filtrée
         * en local : une école secondaire sénégalaise compte couramment plus de mille élèves (même
         * remarque que GetStudentsQuery), un plafond de page les rendrait invisibles à la recherche.
         * Déclenchée avec un anti-rebond (x-on:input.debounce.300ms dans la vue).
         */
        async searchStudents() {
            this.clearSelection();
            const q = this.studentSearch.trim();

            if (q.length === 0) {
                this.students = [];
                this.studentsLoaded = false;
                this.studentSearchError = null;
                return;
            }

            this.isSearchingStudents = true;
            this.studentSearchError = null;
            try {
                // Route dédiée (finance/students/search), pas la recherche générique /students : chaque
                // ligne porte déjà le solde de l'inscription active, pour signaler dans LA LISTE — avant
                // toute sélection — qu'un élève vient d'être inscrit par le secrétariat mais n'a encore
                // rien versé (voir badgeFor ci-dessous).
                this.students = await window.api.get(`/finance/students/search?q=${encodeURIComponent(q)}`);
                this.studentsLoaded = true;
            } catch (err) {
                this.studentSearchError = window.api.toMessage(err, 'Erreur lors de la recherche.');
            } finally {
                this.isSearchingStudents = false;
            }
        },

        clearSelection() {
            this.selectedStudent = null;
            this.balance = null;
            this.balanceError = null;
            this.quickPay.open = false;
        },

        async selectStudent(student, { autoQuickPay = true } = {}) {
            this.selectedStudent = student;
            this.studentSearch = `${student.matricule} — ${student.fullName}`;
            await this.loadBalance();
            // Guichet rapide : dès qu'un élève est choisi et qu'il a un montant échu, on ouvre la
            // pop-up d'encaissement (frais dus pré-cochés). Les cas particuliers — montant libre,
            // solde intégral, consultation de l'historique — restent accessibles derrière [Annuler],
            // sur le panneau détaillé qui reste affiché. Jamais à la restauration d'un brouillon
            // (autoQuickPay=false) : l'utilisateur y reprend une saisie manuelle en cours.
            if (autoQuickPay && this.balance && this.dueNowTotal > 0 && !this.showReceipt) {
                this.openQuickPay();
            }
        },

        // ---------------------------------------------------------------- Guichet rapide (pop-up)

        /** Échéances À RÉGLER MAINTENANT (engagement initial + mensualités échues) encore dues — les
         *  lignes proposées dans la pop-up. `isDueNow` est servi par le serveur ; le repli sur
         *  `isInitialScope` ne sert qu'à un payload antérieur. */
        quickPayRows() {
            return (this.balance?.installments ?? []).filter((i) => (i.isDueNow ?? i.isInitialScope) && i.remainingDue > 0);
        },

        /** Total des lignes actuellement COCHÉES — pilote le « Total à encaisser » et le bouton. */
        quickPayTotal() {
            return this.quickPayRows().reduce(
                (sum, row, idx) => sum + (this.quickPay.checked[idx] ? row.remainingDue : 0), 0);
        },

        openQuickPay() {
            const rows = this.quickPayRows();
            if (rows.length === 0) return;
            this.quickPay.checked = rows.map(() => true); // tout coché par défaut
            this.quickPay.method = 'Cash';
            this.quickPay.amount = this.quickPayTotal();
            this.formErrors = {};
            this.conflictError = false;
            this.quickPay.open = true;
        },

        /** [Annuler] : referme la pop-up et laisse le panneau détaillé accessible (cas particuliers). */
        closeQuickPay() {
            this.quickPay.open = false;
        },

        /**
         * Sélection CONTIGUË : cocher la ligne i coche aussi 0..i-1 ; décocher la ligne i décoche
         * aussi i+1..fin. On ne peut donc régler qu'un PRÉFIXE des frais dus — c'est exactement ce
         * que l'imputation oldest-first de InstallmentScheduleCalculator sait refléter en « Réglé ».
         */
        onQuickRowToggle(i) {
            if (this.quickPay.checked[i]) {
                for (let j = 0; j < i; j++) this.quickPay.checked[j] = true;
            } else {
                for (let j = i + 1; j < this.quickPay.checked.length; j++) this.quickPay.checked[j] = false;
            }
            this.quickPay.amount = this.quickPayTotal();
        },

        /** « Mensualité (Mois 1) » → « Mois 1 » pour la colonne « Période / Note » du reçu ventilé. */
        quickPayNote(designation) {
            const match = /\(([^)]+)\)\s*$/.exec(designation || '');
            return match ? match[1] : null;
        },

        async submitQuickPay() {
            if (!this.balance) return;
            const rows = this.quickPayRows();
            const chosen = rows.filter((_, idx) => this.quickPay.checked[idx]);
            if (chosen.length === 0) return;

            const checkedTotal = chosen.reduce((sum, r) => sum + r.remainingDue, 0);
            const amount = Number(this.quickPay.amount) || checkedTotal;

            // Ventilation (une ligne de reçu par poste) seulement si le montant perçu correspond
            // EXACTEMENT au total coché ET que chaque poste porte sa catégorie de frais (échéancier
            // dérivé, pas un plan personnalisé). Sinon le reçu retombe sur sa ligne unique — garde-fou
            // comptable, cf. PaymentReceiptDto.HasBalancedLines.
            let breakdowns = null;
            if (amount === checkedTotal && chosen.every((r) => r.feeCategoryId)) {
                breakdowns = chosen.map((r) => ({
                    feeCategoryId: r.feeCategoryId,
                    amountAllocated: r.remainingDue,
                    label: this.quickPayNote(r.designation)
                }));
            }

            const ok = await this.recordPayment({ amount, method: this.quickPay.method, breakdowns });
            if (ok) this.quickPay.open = false;
        },

        // ---------------------------------------------------------------- Solde

        async loadBalance() {
            if (!this.selectedStudent) return;

            this.isLoadingBalance = true;
            this.balance = null;
            this.balanceError = null;
            this.conflictError = false;
            this.formErrors = {};

            try {
                this.balance = await window.api.get(`/finance/students/${this.selectedStudent.id}/balance`);
                this.form.amount = '';
            } catch (err) {
                // 404 : aucune inscription active pour cet élève — état géré par balanceError, pas une
                // erreur globale (le panneau dédié explique la marche à suivre).
                this.balanceError = err.status === 404
                    ? 'Aucune inscription active.'
                    : (window.api.toMessage(err, 'Erreur lors du chargement du solde.'));
            } finally {
                this.isLoadingBalance = false;
            }
        },

        /** Après un 409 : on ne réessaie jamais avec les anciennes données, on relit le solde à jour. */
        async reloadBalance() {
            this.conflictError = false;
            await this.loadBalance();
        },

        fillFullBalance() {
            if (this.balance) this.form.amount = this.balance.remainingBalance;
        },

        /**
         * MONTANT ÉCHU à ce jour : l'engagement initial (frais d'inscription, uniforme… + le PREMIER
         * mois de scolarité) PLUS les mensualités dont la date est atteinte, non encore réglés —
         * jamais les mensualités FUTURES, qui n'ont pas lieu d'être encaissées d'avance. Un élève
         * qui a réglé son inscription mais pas ses mois échus doit encore apparaître à la caisse.
         *
         * Le serveur le calcule (StudentBalanceDto.dueNowTotal) à partir des échéances marquées
         * `isDueNow` — source de vérité unique. Le repli ne sert qu'à un payload servi par un
         * cache antérieur : il recompose la même somme depuis les mêmes échéances.
         *
         * PLUS de filtre « date d'échéance <= aujourd'hui » : une année qui démarre le mois prochain
         * n'a AUCUNE échéance passée, et ce montant valait alors 0 à tort — le parent doit pourtant
         * déjà régler l'inscription et le 1er mois.
         */
        get dueNowTotal() {
            if (!this.balance) return 0;
            if (typeof this.balance.dueNowTotal === 'number') return this.balance.dueNowTotal;
            return (this.balance.installments || [])
                .filter((inst) => (inst.isDueNow ?? inst.isInitialScope) && inst.remainingDue > 0)
                .reduce((sum, inst) => sum + inst.remainingDue, 0);
        },

        fillDueNow() {
            const total = this.dueNowTotal;
            if (total > 0) this.form.amount = total;
        },

        // ---------------------------------------------------------------- Encaissement

        canSubmit() {
            if (!this.balance || this.balance.remainingBalance <= 0 || this.isSubmitting) return false;
            const amount = Number(this.form.amount);
            return amount > 0 && amount <= this.balance.remainingBalance;
        },

        async submit() {
            if (!this.balance) return;
            await this.recordPayment({
                amount: Number(this.form.amount),
                method: this.form.method
            });
        },

        /**
         * Cœur de l'encaissement, partagé par le formulaire détaillé (submit) et le guichet rapide
         * (submitQuickPay). Renvoie true si le versement est passé (fenêtre de confirmation ouverte),
         * false si l'erreur a été traitée à l'écran (409 concurrentiel, ou 400 de validation).
         *
         * @param {{amount:number, method:string, breakdowns?:Array}} p — `breakdowns` : ventilation
         *   facultative (une entrée {feeCategoryId, amountAllocated, label} par poste imputé). Omise,
         *   le reçu porte sa ligne unique « Versement reçu ».
         */
        async recordPayment({ amount, method, breakdowns }) {
            this.formErrors = {};
            this.conflictError = false;
            this.isSubmitting = true;
            this.sendState = null;

            try {
                const payload = {
                    enrollmentId: this.balance.enrollmentId,
                    amount,
                    method,
                    idempotencyKey: this.idempotencyKey
                };
                if (breakdowns && breakdowns.length) payload.breakdowns = breakdowns;

                // submitWithRetry (JGK-L02) rejoue UNIQUEMENT sur coupure réseau, jamais sur une
                // réponse HTTP — la même idempotencyKey à chaque tentative fait qu'un retry rejoue le
                // paiement déjà encaissé (JGK-L01) au lieu d'en créer un second.
                this.paymentResult = await window.api.postWithRetry('/finance/payments', payload,
                    { onStateChange: (state) => { this.sendState = state; } });

                // Le résultat du POST est volontairement minimal (règle CQRS) : on relit le reçu complet
                // pour l'affichage/l'impression, comme /inscriptions le fait pour son propre reçu.
                this.receipt = await window.api.get(`/finance/payments/${this.paymentResult.paymentId}/receipt`);

                // Étape 1 : on confirme l'encaissement dans une fenêtre dédiée ; le reçu n'apparaît
                // qu'ensuite, si l'utilisateur clique « Imprimer le reçu ».
                this.showReceipt = false;
                this.showConfirmDialog = true;
                if (window.formDraft) window.formDraft.clear('caisse_form');
                this.hasDraft = false;
                return true;
            } catch (err) {
                if (err.status === 409) {
                    // Solde modifié entre-temps par un autre caissier : jamais un écrasement silencieux
                    // (règle #5). On force explicitement un rechargement avant toute nouvelle tentative.
                    this.conflictError = true;
                } else {
                    this.formErrors = window.api.toFieldErrors(err, "Erreur lors de l'encaissement.");
                }
                return false;
            } finally {
                this.isSubmitting = false;
            }
        },

        /** Libellé de l'état d'envoi affiché près du bouton, piloté par submitWithRetry (JGK-L03). */
        sendStateLabel() {
            switch (this.sendState) {
                case 'retrying': return 'Connexion instable — nouvelle tentative en cours, en attente d\'envoi…';
                case 'failed': return 'Échec après plusieurs tentatives — la saisie est conservée, réessayez.';
                default: return '';
            }
        },

        /** Fenêtre de confirmation → « Imprimer le reçu » : on ferme le dialogue et on révèle le reçu. */
        showReceiptFromDialog() {
            this.showConfirmDialog = false;
            this.showReceipt = true;
            // $nextTick : la feuille n'existe dans le DOM qu'après le rendu du x-show.
            this.$nextTick(() => this.checkReceiptFit());
        },

        /** Fenêtre de confirmation → « Terminer » (ou fermeture) : repart sur une recherche vierge. */
        finishFromDialog() {
            this.showConfirmDialog = false;
            this.startNewPayment();
        },

        /** Écran du reçu → « Retour (Fermer) » : masque le reçu et revient à la recherche d'élève. */
        closeReceipt() {
            this.showReceipt = false;
            this.startNewPayment();
        },

        startNewPayment() {
            this.selectedStudent = null;
            this.balance = null;
            this.balanceError = null;
            this.conflictError = false;
            this.studentSearch = '';
            this.form = { amount: '', method: 'Cash' };
            this.quickPay = { open: false, method: 'Cash', amount: '', checked: [] };
            this.formErrors = {};
            this.paymentResult = null;
            this.receipt = null;
            this.showReceipt = false;
            this.showConfirmDialog = false;
            // La feuille A5 est démontée avec `receipt` (x-if) : sa mesure de tenue en page ne
            // vaut plus rien. La garder afficherait le verdict du reçu PRÉCÉDENT sur le suivant.
            this.receiptFitMm = null;
            // Nouvel encaissement = nouvelle clé (JGK-L03) : réutiliser l'ancienne rejouerait le
            // paiement précédent au lieu d'en enregistrer un nouveau.
            this.idempotencyKey = window.networkGuard.newIdempotencyKey();
            this.sendState = null;
        },

        formatDateOnly(dateStr) {
            if (!dateStr) return '—';
            try {
                const d = new Date(dateStr);
                if (isNaN(d.getTime())) return dateStr;
                return d.toLocaleDateString('fr-FR');
            } catch {
                return dateStr;
            }
        },

        /**
         * Classe de pastille d'une échéance.
         *
         * Rendait auparavant le NOM d'une variante ('success', 'danger'…) destiné à l'attribut
         * `variant` du Tag Helper &lt;badge&gt;, via `:variant="statusBadgeVariant(...)"`. Cette
         * liaison ne pouvait pas fonctionner : `variant` est une propriété C# lue au rendu du
         * serveur, et `:variant` n'est qu'un attribut HTML de plus posé sur le &lt;span&gt; émis.
         * Résultat, toute la colonne « Statut » du tableau des échéances s'affichait en gris neutre
         * — soldé, en retard et partiel avaient exactement la même apparence.
         *
         * On rend donc directement les classes du design system, que `:class` sait appliquer.
         */
        statusBadgeClass(status) {
            switch (status) {
                case 'Paid': return 'status-badge-success';
                case 'Overdue': return 'status-badge-danger';
                case 'Partial': return 'status-badge-warning';
                default: return 'status-badge-neutral';
            }
        },

        statusLabel(status) {
            switch (status) {
                case 'Paid': return 'Soldé';
                case 'Overdue': return 'En retard';
                case 'Partial': return 'Partiel';
                case 'Pending': return 'En attente';
                default: return status || '—';
            }
        },

        payInstallment(inst) {
            if (!inst || inst.remainingDue <= 0) return;
            this.form.amount = inst.remainingDue;
            const input = document.getElementById('caisse-amount');
            if (input) {
                input.focus();
                input.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        },

        /**
         * « Imprimer » ouvre le reçu de caisse PDF dans la modale d'aperçu partagée (visionneuse PDF
         * native du navigateur, avec ses propres boutons Imprimer / Télécharger). Plus de
         * window.print() sur le rendu HTML de la page.
         */
        async printReceipt() {
            await this.downloadPdf();
        },

        // Aperçu PDF (reçu de caisse) — état + méthodes étalés depuis le moteur partagé
        // (wwwroot/js/pdf-preview.js) ; downloadPdf/previewReceipt ci-dessous appellent openPdfPreview.
        ...window.pdfPreview.state(),

        /** Ouvre la modale de prévisualisation PDF pour le reçu de caisse (ticket JGK-F02 / Axe 4). */
        async downloadPdf() {
            if (!this.paymentResult) return;
            await this.openPdfPreview(
                `/api/v1/finance/payments/${this.paymentResult.paymentId}/receipt/pdf`,
                `Reçu de paiement n° ${this.paymentResult.receiptNumber}`,
                `Recu-${this.paymentResult.receiptNumber}.pdf`
            );
        },

        /** Ouvre l'aperçu et l'impression d'un reçu historique depuis le tableau des versements. */
        async previewReceipt(paymentId, receiptNumber) {
            await this.openPdfPreview(
                `/api/v1/finance/payments/${paymentId}/receipt/pdf`,
                `Reçu de paiement n° ${receiptNumber}`,
                `Recu-${receiptNumber}.pdf`
            );
        },

        // ------------------------------------------------- Reçu de caisse A5 (refonte 25/08/2026)
        //
        // Le reçu porte désormais la VENTILATION du versement — une ligne par poste imputé — au lieu
        // de l'unique « Versement reçu ». Le dû annuel, le déjà-réglé et le solde n'y figurent plus :
        // un reçu n'atteste que de la somme entrée en caisse ce jour-là (AGENTS.md règle #12).
        // Ils restent affichés dans le cadre de gauche, à l'usage du caissier.

        /**
         * Vrai quand la ventilation est imprimable. La décision vient du SERVEUR
         * (PaymentReceiptDto.HasBalancedLines) : si le détail saisi ne totalise pas exactement le
         * montant encaissé, le document se replie sur sa ligne unique — un tableau dont le détail
         * contredit le total est un faux. Le repli local reproduit la même règle, jamais une variante.
         */
        hasVentilation() {
            if (!this.receipt) return false;
            if (typeof this.receipt.hasBalancedLines === 'boolean') {
                return this.receipt.hasBalancedLines;
            }
            const lines = this.receipt.lines || [];
            if (lines.length === 0) return false;
            return lines.reduce((sum, l) => sum + l.amount, 0) === this.receipt.amount;
        },

        /**
         * Colonne « Période / Note » : le libellé propre à la ligne, à défaut la période du versement
         * entier, à défaut CHAÎNE VIDE. Jamais un tiret ni une période devinée — un reçu n'invente pas
         * la période qu'il atteste. Même cascade que PaymentReceiptDto.ResolveLineLabel.
         */
        lineLabel(line) {
            if (line && line.label && line.label.trim()) return line.label;
            const period = this.receipt ? this.receipt.referencePeriod : null;
            return period && period.trim() ? period : '';
        },

        /** Précision du bandeau de section : la période couverte par le versement, si renseignée. */
        periodHint() {
            const period = this.receipt ? this.receipt.referencePeriod : null;
            return period && period.trim() ? `Période de référence : ${period}` : '';
        },

        /** Ligne de coordonnées de l'en-tête : adresse · téléphone · e-mail, sans les trous. */
        contactLine() {
            if (!this.receipt) return '';
            return [this.receipt.schoolAddress, this.receipt.schoolPhone, this.receipt.schoolEmail]
                .filter(Boolean).join('  ·  ');
        },

        /** Ligne « NINEA … · RCCM … » : n'imprime que les mentions réellement saisies. */
        legalMentions() {
            if (!this.receipt) return '';
            return [
                this.receipt.schoolNinea ? `NINEA : ${this.receipt.schoolNinea}` : null,
                this.receipt.schoolRegistreCommerce ? `RCCM : ${this.receipt.schoolRegistreCommerce}` : null
            ].filter(Boolean).join('  ·  ');
        },

        /**
         * Écart entre la hauteur du contenu et la zone utile de la feuille (128 mm), en millimètres.
         * Positif = il reste de la place, négatif = ça déborde. Mesuré sur le rendu RÉEL : c'est la
         * seule garantie de page unique quel que soit le nombre de postes ventilés. L'impression
         * rogne le débordement — cet indicateur existe pour qu'il soit vu avant, et non subi.
         */
        checkReceiptFit() {
            const sheet = document.getElementById('receipt-printable');
            if (!sheet) { this.receiptFitMm = null; return; }

            const style = window.getComputedStyle(sheet);
            const inner = sheet.clientHeight
                - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);

            let used = 0;
            for (const child of sheet.children) used += child.offsetHeight;

            // Le filet bas ne s'applique qu'À L'IMPRESSION : la feuille mesurée ici est celle de
            // L'ÉCRAN, qui ne le porte pas encore. Sans cette soustraction, l'indicateur annonce
            // 4 mm de place qui n'existent pas au moment d'imprimer — mesuré le 03/09/2026 :
            // 127,4 mm annoncés contre 124,1 mm réellement disponibles. Un reçu affiché comme
            // « tient sur une page » avec 2 mm libres serait alors rogné en silence, ce que cet
            // indicateur existe précisément pour empêcher. Valeur lue sur l'élément : le CSS
            // (--receipt-print-bottom-inset) reste la seule source de vérité.
            const printInsetMm = parseFloat(style.getPropertyValue('--receipt-print-bottom-inset')) || 0;
            this.receiptFitMm = (inner - used) / (96 / 25.4) - printInsetMm;
        },

        // ---------------------------------------------------------------- Affichage

        methodLabel(method) {
            switch (method) {
                case 'Cash': return 'Espèces';
                case 'Cheque': return 'Chèque';
                case 'Transfer': return 'Virement';
                case 'MobileMoney': return 'Mobile Money (Wave / Orange Money)';
                default: return method;
            }
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return window.formatFCFA(amount);
        },

        /** Bas de reçu « Fait à [ville], le [date] » (référence de design §1.6) ; sans ville, on abrège. */
        faitMention() {
            if (!this.receipt) return '';
            const date = this.formatDate(this.receipt.paidAt);
            return this.receipt.schoolCity ? `Fait à ${this.receipt.schoolCity}, le ${date}` : `Fait le ${date}`;
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/payment-cancel.js`

````javascript
/**
 * Annulation d'un paiement saisi par erreur (POST /finance/payments/{id}/cancel) — logique PARTAGÉE par
 * la fiche élève (onglet Paiements) et l'écran Caisse, pour que le motif se choisisse de la même façon
 * partout. Le modal correspondant est Views/Shared/_CancelPaymentModal.cshtml.
 *
 * Usage dans un composant Alpine :
 *     Alpine.data('xxx', () => ({ ...window.paymentCancel.mixin(), async afterPaymentCancelled() { … }, … }))
 * `afterPaymentCancelled()` (à fournir) recharge ce que l'écran affiche : solde, historique…
 *
 * Le motif est OBLIGATOIRE côté serveur (CancelPaymentCommandValidator, 3 à 500 caractères) : ce fichier
 * ne fait qu'aider à le composer. Le Directeur et la Finance annulent dans le délai de grâce de 48 h ; passé ce délai,
 * seul le Directeur peut passer une CONTRE-PASSATION (même route, `reversal: true`) : écriture inverse datée du jour,
 * les périodes déjà arrêtées ne sont pas réécrites. `canCancelPayment` / `canReversePayment` ne sont qu'un confort
 * d'affichage : le serveur revérifie.
 */
(function () {
    const OTHER = 'Autre motif';

    const REASONS = [
        'Montant saisi incorrect',
        'Paiement enregistré sur le mauvais élève',
        'Paiement enregistré en double',
        'Mauvais mode de paiement',
        'Chèque ou virement non abouti',
        'Remboursement au parent',
        OTHER
    ];

    // Destination de l'argent (PaymentCancellationOutcome). Les écoles remboursent très rarement : la valeur par
    // défaut est donc « l'argent reste à l'école » ; l'annulation se comptabilise pareil dans les trois cas.
    const OUTCOMES = [
        { value: 'KeptBySchool', label: "Simple correction : l'argent reste à l'école" },
        { value: 'ReusedForEnrollment', label: "Argent réutilisé pour l'inscription de l'enfant" },
        { value: 'RefundedToParent', label: 'Argent remboursé au parent' }
    ];

    window.paymentCancel = {
        OTHER,
        REASONS,
        OUTCOMES,

        mixin() {
            return {
                // Directeur : toujours. Finance : tant que le Directeur ne lui a pas retiré ce droit
                // (SchoolSettings.allowFinanceToCancelPayments, vrai par défaut) — affiné par initPaymentCancel().
                // Propriété simple, PAS un getter : le mixin est copié par décomposition (...mixin()).
                canCancelPayment: ['Directeur', 'Finance'].includes(window.auth.role),
                canReversePayment: window.auth.role === 'Directeur',
                cancelPaymentReasons: REASONS,
                cancelingPayment: null, // { paymentId, receiptNumber, amount, reversal }
                cancelPaymentOutcomes: OUTCOMES,
                cancelPaymentOutcome: 'KeptBySchool',
                cancelPaymentChoice: '',
                cancelPaymentDetail: '',
                cancelPaymentError: null,
                isCancelingPayment: false,

                /**
                 * À appeler depuis l'init() de la page hôte : lit le réglage du Directeur. Seule la Finance en dépend ;
                 * en cas d'échec de lecture on garde le défaut (ouvert) — le serveur reste l'arbitre (403 sinon).
                 */
                async initPaymentCancel() {
                    if (window.auth.role !== 'Finance') return;
                    try {
                        const settings = await window.api.get('/schools/current/settings');
                        this.canCancelPayment = settings.allowFinanceToCancelPayments !== false;
                    } catch {
                        // silence-volontaire : confort d'affichage, le défaut ouvert est celui du serveur.
                    }
                },

                /** `options.reversal` : contre-passation d'un versement hors délai (bouton réservé au Directeur). */
                openCancelPayment(payment, options) {
                    this.cancelingPayment = {
                        paymentId: payment.paymentId,
                        receiptNumber: payment.receiptNumber,
                        amount: payment.amount,
                        reversal: !!(options && options.reversal)
                    };
                    this.cancelPaymentChoice = '';
                    this.cancelPaymentOutcome = 'KeptBySchool';
                    this.cancelPaymentDetail = '';
                    this.cancelPaymentError = null;
                },

                closeCancelPayment() {
                    this.cancelingPayment = null;
                    this.cancelPaymentChoice = '';
                    this.cancelPaymentOutcome = 'KeptBySchool';
                    this.cancelPaymentDetail = '';
                    this.cancelPaymentError = null;
                },

                /**
                 * Le délai de grâce court à partir de l'encaissement : `cancellableUntil` est calculé par le
                 * serveur (PaymentCancellationPolicy), jamais recopié ici. Confort d'affichage — le serveur refuse
                 * de toute façon (409) un paiement hors délai.
                 */
                isPaymentCancellable(payment) {
                    if (!payment || payment.status === 'Cancelled') return false;
                    if (!payment.cancellableUntil) return true;
                    return Date.now() <= new Date(payment.cancellableUntil).getTime();
                },

                /**
                 * Hors délai de grâce, seul le Directeur peut encore corriger : par contre-passation. Le même calcul
                 * que isPaymentCancellable, inversé — `cancellableUntil` vient du serveur.
                 */
                isPaymentReversible(payment) {
                    if (!this.canReversePayment || !payment || payment.status === 'Cancelled') return false;
                    if (!payment.cancellableUntil) return false;
                    return Date.now() > new Date(payment.cancellableUntil).getTime();
                },

                /** Le champ de saisie libre est obligatoire pour « Autre motif », facultatif sinon. */
                cancelPaymentDetailRequired() {
                    return this.cancelPaymentChoice === OTHER;
                },

                /** Motif final envoyé au serveur : « Libellé — précision » ou « Autre : précision ». */
                composeCancelPaymentReason() {
                    const detail = (this.cancelPaymentDetail || '').trim();
                    if (this.cancelPaymentChoice === OTHER) {
                        return detail ? `Autre : ${detail}` : '';
                    }
                    if (!this.cancelPaymentChoice) return '';
                    return detail ? `${this.cancelPaymentChoice} — ${detail}` : this.cancelPaymentChoice;
                },

                async confirmCancelPayment() {
                    if (!this.cancelingPayment) return;

                    if (!this.cancelPaymentChoice) {
                        this.cancelPaymentError = "Choisissez un motif dans la liste.";
                        return;
                    }
                    const reason = this.composeCancelPaymentReason();
                    if (this.cancelPaymentDetailRequired() && (this.cancelPaymentDetail || '').trim().length < 3) {
                        this.cancelPaymentError = "Précisez le motif (3 caractères minimum).";
                        return;
                    }

                    this.isCancelingPayment = true;
                    this.cancelPaymentError = null;
                    try {
                        const body = { reason, outcome: this.cancelPaymentOutcome };
                        if (this.cancelingPayment.reversal) body.reversal = true;
                        await window.api.post(`/finance/payments/${this.cancelingPayment.paymentId}/cancel`, body);
                        this.closeCancelPayment();
                        // Le solde, le total versé et l'historique changent : l'écran se relit.
                        await this.afterPaymentCancelled();
                    } catch (err) {
                        // Déjà annulé / caisse clôturée : le message serveur est déjà explicite, affiché tel quel.
                        this.cancelPaymentError = (err && err.message) || "Erreur lors de l'annulation du paiement.";
                        if (err && err.code === 'CONCURRENCY_CONFLICT') {
                            await this.afterPaymentCancelled();
                        }
                    } finally {
                        this.isCancelingPayment = false;
                    }
                }
            };
        }
    };
})();
````


#### `src/SamaEcole.Web/wwwroot/js/fees.js`

````javascript
/**
 * Écran Paramétrage des frais — ticket JGK-F01.
 *
 * Trois ressources composent la grille : les catégories, les classes et le barème (une ligne par
 * catégorie × classe). L'interface les croise pour afficher, catégorie par catégorie, le montant de
 * chaque classe — ou « non défini » là où aucune ligne n'existe encore.
 *
 * Le serveur reste seul juge : le rôle est relu du JWT pour masquer les actions d'écriture, mais
 * l'API répond 403 à qui les appellerait quand même. Le jeton de concurrence (rowVersion/xmin) est
 * renvoyé tel quel à chaque modification — deux éditions concurrentes ne peuvent pas s'écraser
 * (AGENTS.md règle #5).
 *
 * Matrice d'autorisation "Photoshop" : le Directeur peut TOUJOURS modifier/supprimer ; la Finance ne
 * le peut QUE si SchoolSettings.allowFinanceToModifyFees / allowFinanceToDeleteFees est activé pour
 * l'école (canModifyFees / canDeleteFees, deux getters — jamais une valeur figée au chargement, même
 * principe que canManageGradingConfig dans settings.js). Ces deux booléens sont lus une seule fois à
 * l'ouverture de l'écran (comme gradingScale dans grades.js) : un changement de délégation fait par
 * le Directeur pendant que cet écran est déjà ouvert n'est visible qu'au rechargement.
 *
 * Feature D — canModifyFees couvre aussi la CRÉATION d'une catégorie et l'application d'un montant
 * standard (Option 1), pas seulement l'ajustement d'une ligne déjà existante (Option 2) : les trois
 * façonnent le même barème, donc la même délégation (voir FinanceController).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('feesView', () => ({
        ...window.guidedDelete.mixin({ resource: 'finance/fee-categories' }),
        categories: [],
        classrooms: [],
        fees: [],
        selectedCategoryId: null,

        activeTab: 'fees', // 'fees' ou 'disbursements'
        disbursements: [],
        isLoadingDisbursements: false,
        
        disbursementForm: { date: '', beneficiary: '', reason: '', category: 'Salaires', amount: null, paymentMethod: 'Cash' },
        creatingDisbursement: false,
        isSubmittingDisbursement: false,
        createDisbursementError: null,

        deletingDisbursement: null,
        isDeletingDisbursement: false,
        deleteDisbursementError: null,

        isLoading: false,
        error: null,

        // Recherche + filtre niveau + tri sur la grille de la catégorie sélectionnée (Volume 5 §6)
        search: '',
        levelFilter: '',
        sortKey: 'name',
        sortDir: 'asc',

        isDirecteur: window.auth.role === 'Directeur',
        allowFinanceToModifyFees: false,
        allowFinanceToDeleteFees: false,

        get canModifyFees() {
            return this.isDirecteur || (window.auth.role === 'Finance' && this.allowFinanceToModifyFees);
        },
        get canDeleteFees() {
            return this.isDirecteur || (window.auth.role === 'Finance' && this.allowFinanceToDeleteFees);
        },

        // Suppression d'une catégorie : modale partagée (guided-delete.js)
        showCategoryDeletedDialog: false,
        deletedCategoryName: '',

        // Suppression d'une ligne de barème / exception (modale de confirmation)
        deletingFee: null, // { classFeeId, classroomName, rowVersion }
        isDeletingFee: false,
        deleteFeeError: null,
        showFeeDeletedDialog: false,
        deletedFeeClassroomName: '',

        // Création de catégorie (panneau latéral)
        isCategoryOpen: false,
        isSavingCategory: false,
        newCategory: { name: '', isRecurring: true, isBoardingFee: false, isOptional: false },
        categoryErrors: {},

        // Confirmation « Catégorie ajoutée » affichée après un enregistrement réussi.
        showCategoryAddedDialog: false,
        addedCategoryName: '',

        // Application d'un montant standard (modale). `level` = périmètre : chaîne vide = toutes les
        // classes, sinon le seul niveau ciblé (le serveur ne touche alors pas aux autres cycles).
        isApplyOpen: false,
        isApplying: false,
        applyForm: { amount: null, level: '', overwriteExisting: false },
        applyErrors: {},

        // Édition d'une exception (modale)
        editing: null, // { classFeeId, classroomName, amount, rowVersion }
        isSavingEdit: false,
        editErrors: {},
        showFeeEditedDialog: false,
        editedFeeClassroomName: '',
        editedFeeAmount: null,

        // Historique (modale)
        historyFor: null, // { classroomName }
        historyEntries: [],
        isLoadingHistory: false,

        async init() {
            // Restauration depuis la corbeille (trash.js) : catégories et barème se rechargent.
            window.addEventListener('trash:restored', (event) => {
                if (event.detail && event.detail.kind === 'fee-categories') this.loadAll();
            });
            await this.loadAll();
        },

        async loadAll() {
            this.isLoading = true;
            this.error = null;
            try {
                const [categories, classrooms, fees, settings] = await Promise.all([
                    window.api.get('/finance/fee-categories'),
                    window.api.get('/classrooms'),
                    window.api.get('/finance/fees'),
                    window.api.get('/schools/current/settings')
                ]);
                this.categories = categories;
                this.classrooms = classrooms;
                this.fees = fees;
                this.allowFinanceToModifyFees = settings.allowFinanceToModifyFees;
                this.allowFinanceToDeleteFees = settings.allowFinanceToDeleteFees;

                // Garde la catégorie sélectionnée si elle existe encore, sinon prend la première.
                if (!this.categories.some((c) => c.id === this.selectedCategoryId)) {
                    this.selectedCategoryId = this.categories.length ? this.categories[0].id : null;
                }

                await this.loadDisbursements();
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des frais.');
            } finally {
                this.isLoading = false;
            }
        },

        /** Recharge le seul barème (après une écriture) sans re-solliciter catégories et classes. */
        async reloadFees() {
            this.fees = await window.api.get('/finance/fees');
        },

        get selectedCategory() {
            return this.categories.find((c) => c.id === this.selectedCategoryId) || null;
        },

        /**
         * Segmented control des catégories (Views\Fees\Index.cshtml) : barre d'onglets partagée
         * (.tab-nav-scroll / .tab-btn / .tab-btn-active, Styles/input.css) — pastille active bleu
         * Unikol plein, comme /parametres et tous les autres modules. Ne renvoie que le
         * modificateur actif ; la base `.tab-btn` est posée dans la vue.
         */
        tabClass(categoryId) {
            return this.selectedCategoryId === categoryId ? 'tab-btn-active' : '';
        },

        /**
         * TOUTES les classes croisées avec leur montant pour la catégorie sélectionnée (montant null
         * si aucune ligne n'existe encore). Base non filtrée : sert de dénominateur à definedCount, la
         * recherche/filtre/tri ne s'appliquent qu'à l'affichage (voir rows()).
         */
        get allRows() {
            if (!this.selectedCategoryId) return [];

            return this.classrooms.map((classroom) => {
                const fee = this.fees.find(
                    (f) => f.feeCategoryId === this.selectedCategoryId && f.classroomId === classroom.id);
                return { classroom, fee: fee || null };
            });
        },

        get definedCount() {
            return this.allRows.filter((r) => r.fee).length;
        },

        get knownLevels() {
            return [...new Set(this.classrooms.map((c) => c.level))].sort();
        },

        toggleSort(key) {
            if (this.sortKey === key) {
                this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
            } else {
                this.sortKey = key;
                this.sortDir = 'asc';
            }
        },

        /** Recherche (nom/niveau) + filtre niveau + tri, appliqués à l'affichage sans toucher allRows. */
        get rows() {
            const q = this.search.trim().toLowerCase();
            let rows = this.allRows.filter((r) =>
                (!q || r.classroom.name.toLowerCase().includes(q) || r.classroom.level.toLowerCase().includes(q)) &&
                (!this.levelFilter || r.classroom.level === this.levelFilter));

            const dir = this.sortDir === 'asc' ? 1 : -1;
            return [...rows].sort((a, b) => {
                if (this.sortKey === 'amount') {
                    const va = a.fee ? a.fee.amount : -1;
                    const vb = b.fee ? b.fee.amount : -1;
                    return (va - vb) * dir;
                }
                return String(a.classroom[this.sortKey]).localeCompare(String(b.classroom[this.sortKey])) * dir;
            });
        },

        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return Number(amount).toLocaleString('fr-FR') + ' F';
        },

        // ------------------------------------------------------------ Catégories

        openCreateCategory() {
            this.newCategory = { name: '', isRecurring: true, isBoardingFee: false, isOptional: false };
            this.categoryErrors = {};
            this.isCategoryOpen = true;
        },

        async submitCreateCategory() {
            this.isSavingCategory = true;
            this.categoryErrors = {};
            try {
                const created = await window.api.post('/finance/fee-categories', this.newCategory);
                this.isCategoryOpen = false;
                this.addedCategoryName = this.newCategory.name;
                await this.loadAll();
                this.selectedCategoryId = created.id; // bascule sur la catégorie qu'on vient de créer
                this.showCategoryAddedDialog = true; // confirmation « Catégorie ajoutée »
            } catch (err) {
                this.categoryErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la catégorie.');
            } finally {
                this.isSavingCategory = false;
            }
        },

        openDeleteCategory(category) {
            return this.openGuidedDelete({
                id: category.id, name: category.name, resource: 'finance/fee-categories',
                financialHint: "Des inscriptions ou des paiements enregistrés utilisent cette catégorie : elle ne peut pas être supprimée. Un paiement annulé ne bloque plus.",
                readyText: "Cette catégorie sera supprimée et n'apparaîtra plus pour les nouvelles inscriptions. L'historique comptable des paiements reste intact."
            });
        },

        async afterGuidedDelete(target) {
            this.deletedCategoryName = target.name;
            window.softDeleteTrash.notifyChanged('fee-categories');
            await this.loadAll();
            this.showCategoryDeletedDialog = true;
        },

        // ------------------------------------------------- Montant standard (Option 1)

        openApplyStandard() {
            // Le périmètre reprend le filtre de niveau actif sur la grille : quand on vient de
            // consulter le Collège, c'est presque toujours le Collège qu'on veut tarifer.
            this.applyForm = { amount: null, level: this.levelFilter, overwriteExisting: false };
            this.applyErrors = {};
            this.isApplyOpen = true;
        },

        /** Classes réellement visées par le formulaire — sert au décompte affiché dans la modale. */
        get applyTargetClassrooms() {
            return this.applyForm.level
                ? this.classrooms.filter((c) => c.level === this.applyForm.level)
                : this.classrooms;
        },

        async submitApplyStandard() {
            this.isApplying = true;
            this.applyErrors = {};
            try {
                const report = await window.api.post('/finance/fees/apply-standard', {
                    feeCategoryId: this.selectedCategoryId,
                    amount: this.applyForm.amount,
                    // Chaîne vide = « toutes les classes » : on envoie null, le contrat d'API ne
                    // connaît que null ou un niveau réel (openapi.yaml, ApplyStandardFeeRequest).
                    level: this.applyForm.level || null,
                    overwriteExisting: this.applyForm.overwriteExisting
                });
                // Succès : on FERME la modale tout de suite et on résume l'action dans un Toast,
                // plutôt qu'un encadré vert qui obligeait l'utilisateur à fermer lui-même.
                this.isApplyOpen = false;
                await this.reloadFees();
                toast.success(this.applyReportMessage(report));
            } catch (err) {
                this.applyErrors = window.api.toFieldErrors(err, "Erreur lors de l'application du montant.");
            } finally {
                this.isApplying = false;
            }
        },

        /** Résumé lisible du compte-rendu d'application ({created, updated, skipped}) pour le Toast. */
        applyReportMessage(report) {
            const applied = (report.created || 0) + (report.updated || 0);
            const parts = [`${applied} montant${applied > 1 ? 's' : ''} appliqué${applied > 1 ? 's' : ''}`];
            if (report.skipped > 0) {
                parts.push(`${report.skipped} inchangé${report.skipped > 1 ? 's' : ''}`);
            }
            return `Frais mis à jour : ${parts.join(', ')}.`;
        },

        // ----------------------------------------------- Exception par classe (Option 2)

        openEdit(row) {
            // Une ligne « non définie » n'a pas d'identifiant : on ne peut pas la modifier seule. Le
            // bouton n'est proposé que sur les lignes existantes (voir la vue) ; garde-fou ici aussi.
            if (!row.fee) return;

            this.editing = {
                classFeeId: row.fee.id,
                classroomName: row.classroom.name,
                amount: row.fee.amount,
                rowVersion: row.fee.rowVersion
            };
            this.editErrors = {};
        },

        closeEdit() {
            this.editing = null;
            this.editErrors = {};
        },

        async submitEdit() {
            if (!this.editing) return;

            this.isSavingEdit = true;
            this.editErrors = {};
            try {
                await window.api.put(`/finance/fees/${this.editing.classFeeId}`, {
                    amount: this.editing.amount,
                    rowVersion: this.editing.rowVersion
                });
                this.editedFeeClassroomName = this.editing.classroomName;
                this.editedFeeAmount = this.editing.amount;
                this.closeEdit();
                await this.reloadFees();
                this.showFeeEditedDialog = true;
            } catch (err) {
                // Conflit optimiste : quelqu'un a modifié cette ligne entre-temps. On recharge pour
                // montrer la valeur réelle et on invite à recommencer, plutôt que d'écraser (règle #5).
                if (err.status === 409) {
                    this.editErrors = { global: 'Ce montant vient d\'être modifié par un autre utilisateur. La grille a été rafraîchie — vérifiez la valeur puis réessayez.' };
                    await this.reloadFees();
                } else {
                    this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEdit = false;
            }
        },

        openDeleteFee(row) {
            // Miroir du garde-fou d'openEdit : une ligne « non définie » n'a pas d'identifiant.
            if (!row.fee) return;

            this.deletingFee = {
                classFeeId: row.fee.id,
                classroomName: row.classroom.name,
                rowVersion: row.fee.rowVersion
            };
            this.deleteFeeError = null;
        },

        closeDeleteFee() {
            this.deletingFee = null;
            this.deleteFeeError = null;
        },

        async confirmDeleteFee() {
            if (!this.deletingFee) return;

            this.isDeletingFee = true;
            this.deleteFeeError = null;
            try {
                await window.api.delete(`/finance/fees/${this.deletingFee.classFeeId}?rowVersion=${this.deletingFee.rowVersion}`);
                this.deletedFeeClassroomName = this.deletingFee.classroomName;
                this.deletingFee = null;
                await this.reloadFees();
                this.showFeeDeletedDialog = true;
            } catch (err) {
                if (err.status === 409) {
                    // Conflit optimiste : quelqu'un a modifié cette ligne entre-temps (règle #5). On
                    // recharge pour montrer la valeur réelle plutôt que de forcer une suppression aveugle.
                    this.deleteFeeError = 'Ce montant vient d\'être modifié par un autre utilisateur. La grille a été rafraîchie — vérifiez la valeur puis réessayez.';
                    await this.reloadFees();
                } else {
                    this.deleteFeeError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingFee = false;
            }
        },

        // ------------------------------------------------------------ Historique

        async openHistory(row) {
            if (!row.fee) return;

            this.historyFor = { classroomName: row.classroom.name };
            this.historyEntries = [];
            this.isLoadingHistory = true;
            try {
                this.historyEntries = await window.api.get(`/finance/fees/${row.fee.id}/history`);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'historique.");
                this.historyFor = null;
            } finally {
                this.isLoadingHistory = false;
            }
        },

        closeHistory() {
            this.historyFor = null;
            this.historyEntries = [];
        },

        // ------------------------------------------------------------ Décaissements

        async loadDisbursements() {
            this.isLoadingDisbursements = true;
            try {
                this.disbursements = await window.api.get('/finance/disbursements');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des décaissements.');
            } finally {
                this.isLoadingDisbursements = false;
            }
        },

        openCreateDisbursement() {
            this.createDisbursementError = null;
            this.disbursementForm = {
                date: new Date().toISOString().split('T')[0],
                beneficiary: '',
                reason: '',
                category: 'Salaires',
                amount: null,
                paymentMethod: 'Cash'
            };
            this.creatingDisbursement = true;
        },

        closeCreateDisbursement() {
            this.creatingDisbursement = false;
        },

        async submitCreateDisbursement() {
            this.createDisbursementError = null;
            this.isSubmittingDisbursement = true;
            try {
                await window.api.post('/finance/disbursements', this.disbursementForm);
                this.closeCreateDisbursement();
                await this.loadDisbursements();
            } catch (err) {
                this.createDisbursementError = window.api.toMessage(err, 'Erreur lors de l\'enregistrement.');
            } finally {
                this.isSubmittingDisbursement = false;
            }
        },

        // ------------------------------------------------------------ Décaissements : contre-écritures
        //
        // Un décaissement est une écriture comptable (solde réel, trésorerie, TVA déductible) : il n'est jamais
        // supprimé. « Annuler » crée une NOUVELLE ligne aux montants opposés qui référence l'original (API :
        // reversalOfId) ; l'original passe à isReversed. Les totaux s'annulent d'eux-mêmes, l'historique reste complet.

        /** 'reversal' : contre-écriture · 'reversed' : original déjà annulé · 'active' : écriture normale. */
        disbursementState(d) {
            if (d.reversalOfId) return 'reversal';
            return d.isReversed ? 'reversed' : 'active';
        },

        /** Seule une écriture normale s'annule : ni une contre-écriture, ni un original déjà annulé (409 côté API). */
        canReverseDisbursement(d) {
            return this.disbursementState(d) === 'active';
        },

        disbursementRowClass(d) {
            switch (this.disbursementState(d)) {
                case 'reversal': return 'bg-rose-50/60 hover:bg-rose-50';
                case 'reversed': return 'bg-slate-50/60 hover:bg-slate-50';
                default: return 'hover:bg-slate-50';
            }
        },

        /** Phrase qui relie la contre-écriture à l'écriture d'origine (si elle est dans la liste affichée). */
        reversalNote(d) {
            const original = this.disbursements.find((x) => x.id === d.reversalOfId);
            if (!original) return "Annule un décaissement antérieur.";
            return `Annule le décaissement du ${this.formatDateOnly(original.date)} (${original.beneficiary}).`;
        },

        openDeleteDisbursement(disbursement) {
            this.deleteDisbursementError = null;
            this.deletingDisbursement = disbursement;
        },

        closeDeleteDisbursement() {
            this.deletingDisbursement = null;
        },

        async confirmDeleteDisbursement() {
            this.isDeletingDisbursement = true;
            this.deleteDisbursementError = null;
            try {
                await window.api.delete(`/finance/disbursements/${this.deletingDisbursement.id}`);
                this.closeDeleteDisbursement();
                await this.loadDisbursements();
            } catch (err) {
                this.deleteDisbursementError = window.api.toMessage(err, 'Erreur lors de l\'annulation.');
            } finally {
                this.isDeletingDisbursement = false;
            }
        },

        formatDateOnly(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR');
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/payroll.js`

````javascript
/**
 * Module Comptabilité & Fiscalité — Paie (/paie). Trois onglets : Contrats (prérequis à toute fiche
 * de paie), Fiches de paie (génération + bulletin PDF), Déclarations fiscales (récapitulatif mensuel
 * des charges sociales). Réservé au Directeur et à la Finance (garde réelle : FinanceController).
 *
 * window.api.get/post renvoient déjà le JSON désérialisé ou lèvent une erreur normalisée — jamais de
 * response.ok/response.json() ici (voir le correctif appliqué à discipline.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('payrollView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : bulletin de paie, attestation de travail et
        // fiche d'heures s'ouvrent dans la modale _PdfPreviewModal (impression / téléchargement au
        // choix), jamais un download forcé.
        ...window.pdfPreview.state(),

        tab: 'contrats',
        isLoading: false,
        error: null,

        // ---------------------------------------------------------------- Contrats
        contracts: [],
        teachers: [],
        users: [],
        isContractModalOpen: false,
        isSavingContract: false,
        contractForm: {
            employeeKind: 'teacher',
            teacherId: '',
            userId: '',
            externalFullName: '',
            externalJobTitle: '',
            externalPhone: '',
            type: 'Permanent',
            baseSalary: '',
            hourlyRate: '',
            transportAllowance: '',
            applyStatutoryDeductions: true
        },

        // Modification (Volume 1 §14.1) : augmentation de salaire ou révision du taux horaire sur un contrat ACTIF.
        isEditContractModalOpen: false,
        isSavingEditContract: false,
        editContractForm: { id: '', type: '', baseSalary: '', hourlyRate: '', transportAllowance: '', applyStatutoryDeductions: true, reason: '', rowVersion: 0 },

        // Clôture (Volume 1 §14.1) : jamais une suppression — le contrat reste consultable, mais ne génère plus de fiche de paie.
        isCloseContractModalOpen: false,
        isClosingContract: false,
        closeContractForm: { id: '', employeeFullName: '', endDate: new Date().toISOString().split('T')[0], reason: '', rowVersion: 0 },

        // ---------------------------------------------------------------- Fiches de paie
        payslips: [],
        filterMonth: '',
        filterYear: new Date().getFullYear(),
        isPayslipModalOpen: false,
        isGeneratingPayslip: false,
        payslipForm: {
            employeeContractId: '',
            month: new Date().getMonth() + 1,
            year: new Date().getFullYear(),
            hoursWorked: 0,
            transportAllowance: 0
        },
        printingPayslipId: null,

        // Payer / corriger / annuler une fiche (Volume 1 §14.5 et §14.6).
        // Directeur : toujours. Finance : tant que le Directeur ne lui a pas retiré ce droit
        // (SchoolSettings.allowFinanceToCancelPayslips, vrai par défaut). Confort d'affichage : le serveur reste
        // l'arbitre (403 si le droit est retiré).
        canCancelPayslip: ['Directeur', 'Finance'].includes(window.auth.role),
        isPayslipActionOpen: false,
        isSubmittingPayslipAction: false,
        payslipAction: { kind: 'pay', id: '', status: '', employeeFullName: '', periodLabel: '', netSalary: 0, paymentMethod: 'Cash', date: '', reason: '', rowVersion: 0 },
        payslipPaymentMethodOptions: [
            { value: 'Cash', label: 'Espèces' },
            { value: 'Transfer', label: 'Virement bancaire' },
            { value: 'MobileMoney', label: 'Mobile money (Wave, Orange Money)' },
            { value: 'Cheque', label: 'Chèque' }
        ],
        downloadingCertificateId: null,

        // Suggestion d'heures (ticket JGK-K01) — voir loadSuggestedHours ci-dessous.
        suggestedHours: null,
        isLoadingSuggestion: false,
        suggestionError: null,

        // ---------------------------------------------------------------- Fiche heures Vacataire
        isHourRecordsModalOpen: false,
        hourRecordsContract: null,
        hourRecordsFilter: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },
        hourRecords: [],
        hourRecordForm: { date: new Date().toISOString().split('T')[0], hours: '', note: '' },
        isSavingHourRecord: false,
        downloadingHourRecordSheet: false,

        // ---------------------------------------------------------------- Déclarations fiscales
        taxDeclarations: [],
        filterTaxYear: new Date().getFullYear(),
        isTaxModalOpen: false,
        isGeneratingTax: false,
        taxForm: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },

        init() {
            this.loadCancelPayslipRight();
            this.loadContracts();
            this.loadTeachers();
            this.loadUsers();
            this.loadPayslips();
            this.loadTaxDeclarations();
        },

        // Barre d'onglets partagée (.tab-nav-scroll / .tab-btn / .tab-btn-active, Styles/input.css) :
        // ne renvoie que le modificateur actif, la base `.tab-btn` est posée dans la vue.
        tabClass(name) {
            return this.tab === name ? 'tab-btn-active' : '';
        },

        // ---------------------------------------------------------------- Contrats

        async loadContracts() {
            this.isLoading = true;
            this.error = null;
            try {
                this.contracts = await api.get('/finance/employee-contracts');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des contrats.');
            } finally {
                this.isLoading = false;
            }
        },

        async loadTeachers() {
            try {
                // pageSize plafonné à 100 côté serveur (GetTeachersQueryValidator.MaxPageSize) : au-delà,
                // la requête est rejetée en 422 et le sélecteur reste vide sans qu'aucune erreur ne
                // s'affiche à l'écran (bug réel constaté le 27/08/2026 — pageSize=1000 échouait toujours).
                // 100 reste une limite pour un établissement à très gros effectif enseignant ; passer par
                // une recherche serveur comme students.js le fait serait la vraie solution si ça arrive.
                const data = await api.get('/teachers?page=1&pageSize=100');
                this.teachers = data.items || [];
            } catch (err) {
                console.error('Erreur chargement enseignants:', err);
                this.teachers = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des enseignants.'));
            }
        },

        /** GET /users est réservé au Directeur (UsersController) : la Finance ne pourra créer que des contrats Enseignant. */
        async loadUsers() {
            try {
                this.users = await api.get('/users');
            } catch (err) {
                // silence-volontaire: GET /users est réservé au Directeur (UsersController). Un 403 est
                // le cas NORMAL pour la Finance, qui ne crée alors que des contrats Enseignant — c'est
                // exactement ce que dit le commentaire de la méthode. Un toast transformerait ce refus
                // attendu en incident à chaque chargement de l'écran Paie.
                this.users = [];
            }
        },

        // Moyens de paiement RH (ticket JGK-K02) — distincts du PaymentMethod du module Finance élèves.
        payoutMethodOptions: [
            { value: 'Cash', label: 'Espèces' },
            { value: 'BankTransfer', label: 'Virement bancaire' },
            { value: 'Wave', label: 'Wave' },
            { value: 'OrangeMoney', label: 'Orange Money' }
        ],

        payoutMethodLabel(method) {
            const found = this.payoutMethodOptions.find((o) => o.value === method);
            return found ? found.label : (method || 'Espèces');
        },

        openContractModal() {
            this.contractForm = {
                employeeKind: 'teacher', teacherId: '', userId: '',
                externalFullName: '', externalJobTitle: '', externalPhone: '',
                type: 'Permanent', baseSalary: '', hourlyRate: '', transportAllowance: '',
                // Avec retenues par défaut : le calcul historique. Le Directeur d'un Daara décoche pour payer le brut sans rien déduire.
                applyStatutoryDeductions: true,
                payoutMethod: 'Cash', payoutAccountReference: ''
            };
            this.isContractModalOpen = true;
        },

        async submitContract() {
            const kind = this.contractForm.employeeKind;
            const employeeId = kind === 'teacher' ? this.contractForm.teacherId
                : kind === 'user' ? this.contractForm.userId
                : (this.contractForm.externalFullName || '').trim();
            if (!employeeId) {
                toast.error(kind === 'external' ? 'Veuillez saisir le nom de la personne.' : 'Veuillez sélectionner un employé.');
                return;
            }

            this.isSavingContract = true;
            try {
                await api.post('/finance/employee-contracts', {
                    teacherId: this.contractForm.employeeKind === 'teacher' ? this.contractForm.teacherId : null,
                    userId: this.contractForm.employeeKind === 'user' ? this.contractForm.userId : null,
                    externalFullName: kind === 'external' ? this.contractForm.externalFullName.trim() : null,
                    externalJobTitle: kind === 'external' ? ((this.contractForm.externalJobTitle || '').trim() || null) : null,
                    externalPhone: kind === 'external' ? ((this.contractForm.externalPhone || '').trim() || null) : null,
                    type: this.contractForm.type,
                    baseSalary: Number(this.contractForm.baseSalary) || 0,
                    hourlyRate: Number(this.contractForm.hourlyRate) || 0,
                    transportAllowance: Number(this.contractForm.transportAllowance) || 0,
                    applyStatutoryDeductions: this.contractForm.applyStatutoryDeductions !== false,
                    payoutMethod: this.contractForm.payoutMethod,
                    payoutAccountReference: this.contractForm.payoutAccountReference || null
                });
                toast.success('Contrat enregistré.');
                this.isContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de l'enregistrement du contrat."));
            } finally {
                this.isSavingContract = false;
            }
        },

        // Modification d'un contrat actif (Volume 1 §14.1).
        openEditContractModal(contract) {
            this.editContractForm = {
                id: contract.id,
                type: contract.type,
                baseSalary: contract.baseSalary,
                hourlyRate: contract.hourlyRate,
                transportAllowance: contract.transportAllowance,
                // Un contrat renvoyé sans le champ (ancienne réponse) reste « avec retenues », jamais l'inverse.
                applyStatutoryDeductions: contract.applyStatutoryDeductions !== false,
                payoutMethod: contract.payoutMethod || 'Cash',
                payoutAccountReference: contract.payoutAccountReference || '',
                reason: '',
                rowVersion: contract.rowVersion
            };
            this.isEditContractModalOpen = true;
        },

        async submitEditContract() {
            if (!this.editContractForm.reason || this.editContractForm.reason.trim().length < 5) {
                toast.error('Le motif est obligatoire (5 caractères minimum).');
                return;
            }

            this.isSavingEditContract = true;
            try {
                await api.patch(`/finance/employee-contracts/${this.editContractForm.id}`, {
                    baseSalary: Number(this.editContractForm.baseSalary) || 0,
                    hourlyRate: Number(this.editContractForm.hourlyRate) || 0,
                    transportAllowance: Number(this.editContractForm.transportAllowance) || 0,
                    applyStatutoryDeductions: this.editContractForm.applyStatutoryDeductions !== false,
                    payoutMethod: this.editContractForm.payoutMethod,
                    payoutAccountReference: this.editContractForm.payoutAccountReference || null,
                    reason: this.editContractForm.reason.trim(),
                    rowVersion: this.editContractForm.rowVersion
                });
                toast.success('Contrat modifié.');
                this.isEditContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la modification du contrat.'));
            } finally {
                this.isSavingEditContract = false;
            }
        },

        // Clôture définitive d'un contrat (Volume 1 §14.1) — jamais une suppression.
        openCloseContractModal(contract) {
            this.closeContractForm = {
                id: contract.id,
                employeeFullName: contract.employeeFullName,
                endDate: new Date().toISOString().split('T')[0],
                reason: '',
                rowVersion: contract.rowVersion
            };
            this.isCloseContractModalOpen = true;
        },

        async submitCloseContract() {
            if (!this.closeContractForm.reason || this.closeContractForm.reason.trim().length < 5) {
                toast.error('Le motif est obligatoire (5 caractères minimum).');
                return;
            }

            this.isClosingContract = true;
            try {
                await api.post(`/finance/employee-contracts/${this.closeContractForm.id}/close`, {
                    endDate: this.closeContractForm.endDate,
                    reason: this.closeContractForm.reason.trim(),
                    rowVersion: this.closeContractForm.rowVersion
                });
                toast.success('Contrat clôturé.');
                this.isCloseContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la clôture du contrat.'));
            } finally {
                this.isClosingContract = false;
            }
        },

        // ---------------------------------------------------------------- Fiches de paie

        async loadPayslips() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.filterMonth) params.set('month', this.filterMonth);
                if (this.filterYear) params.set('year', this.filterYear);
                this.payslips = await api.get(`/finance/payroll?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des fiches de paie.');
            } finally {
                this.isLoading = false;
            }
        },

        /** Seule la Finance dépend du réglage du Directeur ; en cas d'échec de lecture on garde le défaut (ouvert). */
        async loadCancelPayslipRight() {
            if (window.auth.role !== 'Finance') return;
            try {
                const settings = await window.api.get('/schools/current/settings');
                this.canCancelPayslip = settings.allowFinanceToCancelPayslips !== false;
            } catch {
                // silence-volontaire : confort d'affichage, le défaut ouvert est celui du serveur.
            }
        },

        // Moyen de règlement proposé par défaut : celui du contrat (PayoutMethod RH -> PaymentMethod des décaissements).
        defaultPaymentMethodFor(payslip) {
            const contract = this.contracts.find((c) => c.id === payslip.employeeContractId);
            switch (contract && contract.payoutMethod) {
                case 'BankTransfer': return 'Transfer';
                case 'Wave':
                case 'OrangeMoney': return 'MobileMoney';
                default: return 'Cash';
            }
        },

        openPayslipAction(payslip, kind) {
            this.payslipAction = {
                kind,
                id: payslip.id,
                status: payslip.status,
                employeeFullName: payslip.employeeFullName,
                periodLabel: this.monthLabel(payslip.month) + ' ' + payslip.year,
                netSalary: payslip.netSalary,
                paymentMethod: this.defaultPaymentMethodFor(payslip),
                date: new Date().toISOString().split('T')[0],
                reason: '',
                rowVersion: payslip.rowVersion
            };
            this.isPayslipActionOpen = true;
        },

        async submitPayslipAction() {
            const a = this.payslipAction;
            if (a.kind !== 'pay' && (!a.reason || a.reason.trim().length < 5)) {
                toast.error('Le motif est obligatoire (5 caractères minimum).');
                return;
            }

            this.isSubmittingPayslipAction = true;
            try {
                if (a.kind === 'pay') {
                    await api.post(`/finance/payroll/${a.id}/pay`, { paymentMethod: a.paymentMethod, date: a.date, rowVersion: a.rowVersion });
                    toast.success('Paiement enregistré : la dépense « Salaires » est en trésorerie. Imprimez le reçu pour signature.');
                } else if (a.kind === 'regenerate') {
                    await api.post(`/finance/payroll/${a.id}/regenerate`, { reason: a.reason.trim(), rowVersion: a.rowVersion });
                    toast.success('Fiche annulée et régénérée sur le contrat actuel.');
                } else {
                    await api.post(`/finance/payroll/${a.id}/cancel`, { reason: a.reason.trim(), rowVersion: a.rowVersion });
                    toast.success('Fiche annulée.');
                }
                this.isPayslipActionOpen = false;
                await this.loadPayslips();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de l'opération sur la fiche de paie."));
            } finally {
                this.isSubmittingPayslipAction = false;
            }
        },

        applyPayslipFilters() {
            this.loadPayslips();
        },

        openPayslipModal() {
            this.payslipForm = {
                employeeContractId: '', month: new Date().getMonth() + 1, year: new Date().getFullYear(),
                hoursWorked: 0, transportAllowance: 0
            };
            this.suggestedHours = null;
            this.suggestionError = null;
            this.isPayslipModalOpen = true;
        },

        /** Contrat sélectionné dans le formulaire de génération, pour connaître son type (Vacataire ou non). */
        get payslipSelectedContract() {
            return this.contracts.find((c) => c.id === this.payslipForm.employeeContractId) || null;
        },

        get payslipContractIsVacataire() {
            return this.payslipSelectedContract && this.payslipSelectedContract.type === 'Vacataire';
        },

        /**
         * Suggestion d'heures pour la paie du vacataire (ticket JGK-K01) — PUREMENT CONSULTATIVE :
         * agrège les heures pointées (TeacherHourRecord) du mois choisi et les rapproche de l'emploi
         * du temps planifié. Ne pré-remplit rien tant que la Direction n'a pas explicitement cliqué
         * « Utiliser cette suggestion » — un écart signalé ici n'empêche jamais la génération de la fiche.
         */
        async loadSuggestedHours() {
            if (!this.payslipForm.employeeContractId || !this.payslipContractIsVacataire) return;

            this.isLoadingSuggestion = true;
            this.suggestionError = null;
            this.suggestedHours = null;
            try {
                const params = new URLSearchParams({ month: this.payslipForm.month, year: this.payslipForm.year });
                this.suggestedHours = await api.get(
                    `/finance/employee-contracts/${this.payslipForm.employeeContractId}/suggested-hours?${params.toString()}`);
            } catch (err) {
                this.suggestionError = window.api.toMessage(err, 'Erreur lors du calcul de la suggestion.');
            } finally {
                this.isLoadingSuggestion = false;
            }
        },

        /** Reprend la suggestion dans le champ « Heures travaillées » — la Direction reste seule à valider. */
        applySuggestedHours() {
            if (!this.suggestedHours) return;
            this.payslipForm.hoursWorked = this.suggestedHours.suggestedHours;
        },

        /** Une suggestion affichée pour un autre contrat n'a plus de sens dès que la sélection change. */
        onPayslipContractChanged() {
            this.suggestedHours = null;
            this.suggestionError = null;
        },

        async submitPayslip() {
            if (!this.payslipForm.employeeContractId) {
                toast.error('Veuillez sélectionner un contrat.');
                return;
            }

            this.isGeneratingPayslip = true;
            try {
                await api.post('/finance/payroll', {
                    employeeContractId: this.payslipForm.employeeContractId,
                    month: Number(this.payslipForm.month),
                    year: Number(this.payslipForm.year),
                    hoursWorked: Number(this.payslipForm.hoursWorked) || 0,
                    transportAllowance: Number(this.payslipForm.transportAllowance) || 0
                });
                toast.success('Fiche de paie générée.');
                this.isPayslipModalOpen = false;
                await this.loadPayslips();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de la génération de la fiche de paie."));
            } finally {
                this.isGeneratingPayslip = false;
            }
        },

        /** Bulletin PDF ouvert dans la modale d'aperçu partagée (pdf-preview.js) : impression / téléchargement au choix. */
        async printPayslip(fichePaieId) {
            if (!fichePaieId || fichePaieId === 'undefined') {
                console.error('Identifiant de fiche de paie invalide ou indéfini', fichePaieId);
                return;
            }
            this.printingPayslipId = fichePaieId;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/payroll/${fichePaieId}/pdf`,
                    'Bulletin de paie',
                    `Bulletin-${fichePaieId}.pdf`
                );
            } finally {
                this.printingPayslipId = null;
            }
        },

        /** Attestation de travail PDF — ouverte dans la modale d'aperçu partagée (même mécanique que printPayslip). */
        async downloadWorkCertificate(contract) {
            if (!contract || !contract.id || contract.id === 'undefined') {
                console.error('Identifiant de contrat invalide ou indéfini', contract && contract.id);
                return;
            }
            this.downloadingCertificateId = contract.id;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/employee-contracts/${contract.id}/work-certificate/pdf`,
                    'Attestation de travail',
                    `Attestation-Travail-${contract.employeeFullName}.pdf`
                );
            } finally {
                this.downloadingCertificateId = null;
            }
        },

        // ---------------------------------------------------------------- Fiche heures Vacataire

        openHourRecordsModal(contract) {
            this.hourRecordsContract = contract;
            this.hourRecordsFilter = { month: new Date().getMonth() + 1, year: new Date().getFullYear() };
            this.hourRecordForm = { date: new Date().toISOString().split('T')[0], hours: '', note: '' };
            this.isHourRecordsModalOpen = true;
            this.loadHourRecords();
        },

        async loadHourRecords() {
            if (!this.hourRecordsContract) return;
            try {
                const params = new URLSearchParams({ month: this.hourRecordsFilter.month, year: this.hourRecordsFilter.year });
                this.hourRecords = await api.get(`/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records?${params.toString()}`);
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des heures.'));
            }
        },

        async submitHourRecord() {
            if (!this.hourRecordsContract || !this.hourRecordForm.date || !this.hourRecordForm.hours) {
                toast.error('Veuillez renseigner la date et le nombre d\'heures.');
                return;
            }

            this.isSavingHourRecord = true;
            try {
                await api.post(`/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records`, {
                    date: this.hourRecordForm.date,
                    hours: Number(this.hourRecordForm.hours),
                    note: this.hourRecordForm.note || null
                });
                toast.success('Heures ajoutées.');
                this.hourRecordForm = { date: new Date().toISOString().split('T')[0], hours: '', note: '' };
                await this.loadHourRecords();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de l'enregistrement des heures."));
            } finally {
                this.isSavingHourRecord = false;
            }
        },

        /** Fiche heures PDF du mois/année sélectionné — ouverte dans la modale d'aperçu partagée. */
        async previewHourRecordSheet() {
            if (!this.hourRecordsContract) return;
            this.downloadingHourRecordSheet = true;
            try {
                const params = new URLSearchParams({ month: this.hourRecordsFilter.month, year: this.hourRecordsFilter.year });
                await this.openPdfPreview(
                    `/api/v1/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records/sheet/pdf?${params.toString()}`,
                    "Fiche d'heures — Vacataire",
                    `Fiche-Heures-${this.hourRecordsContract.employeeFullName}.pdf`
                );
            } finally {
                this.downloadingHourRecordSheet = false;
            }
        },

        // ---------------------------------------------------------------- Déclarations fiscales

        async loadTaxDeclarations() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.filterTaxYear) params.set('year', this.filterTaxYear);
                this.taxDeclarations = await api.get(`/finance/tax-declarations?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des déclarations fiscales.');
            } finally {
                this.isLoading = false;
            }
        },

        applyTaxFilters() {
            this.loadTaxDeclarations();
        },

        openTaxModal() {
            this.taxForm = { month: new Date().getMonth() + 1, year: new Date().getFullYear() };
            this.isTaxModalOpen = true;
        },

        async submitTaxDeclaration() {
            this.isGeneratingTax = true;
            try {
                await api.post('/finance/tax-declaration', {
                    month: Number(this.taxForm.month),
                    year: Number(this.taxForm.year)
                });
                toast.success('Déclaration fiscale générée.');
                this.isTaxModalOpen = false;
                await this.loadTaxDeclarations();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération de la déclaration.'));
            } finally {
                this.isGeneratingTax = false;
            }
        },

        // ---------------------------------------------------------------- Affichage

        employeeLabel(kind) {
            return kind === 'teacher' ? 'Enseignant' : 'Utilisateur (non-enseignant)';
        },

        contractTypeLabel(type) {
            return type === 'Vacataire' ? 'Vacataire (horaire)' : 'Permanent';
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/taxes.js`

````javascript
/**
 * Module Comptabilité & Fiscalité — Fiscalité (/fiscalite). Cartes KPI de la période sélectionnée
 * (mois + année fiscale), historique de l'année, génération d'une nouvelle déclaration et
 * téléchargement de l'état synthétique en PDF. Réservé au Directeur et à la Finance (garde réelle :
 * FinanceController).
 *
 * La période du sélecteur d'en-tête est la SEULE source de vérité : l'année pilote l'appel API
 * (/finance/tax-declarations?year=…) et le mois sélectionne, côté client, la déclaration affichée
 * dans les cartes — pas de second filtre « Année » au-dessus du tableau, qui pouvait diverger.
 *
 * window.api.get/post renvoient déjà le JSON désérialisé ou lèvent une erreur normalisée — jamais de
 * response.ok/response.json() ici (même convention que payroll.js/discipline.js).
 */
document.addEventListener('alpine:init', () => {
    const MONTH_NAMES = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin',
                         'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];

    Alpine.data('taxesView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : l'état synthétique s'ouvre dans la modale
        // _PdfPreviewModal (impression / téléchargement au choix), jamais un download forcé.
        ...window.pdfPreview.state(),

        isLoading: false,
        error: null,

        taxDeclarations: [],

        // Période affichée par les cartes KPI (mois en cours par défaut).
        periodMonth: new Date().getMonth() + 1,
        periodYear: new Date().getFullYear(),

        isTaxModalOpen: false,
        isGeneratingTax: false,
        taxForm: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },

        downloadingPdfId: null,

        init() {
            this.loadTaxDeclarations();
        },

        /** Déclaration de la période choisie, ou null si elle n'a pas encore été générée. */
        get selectedDeclaration() {
            const month = Number(this.periodMonth);
            const year = Number(this.periodYear);
            return this.taxDeclarations.find(d => d.month === month && d.year === year) || null;
        },

        monthOptions() {
            return MONTH_NAMES.map((label, index) => ({ value: index + 1, label }));
        },

        /** Année en cours et les 4 précédentes — au-delà, aucune fiche de paie n'est saisie dans l'app. */
        yearOptions() {
            const current = new Date().getFullYear();
            return Array.from({ length: 5 }, (_, i) => current - i).map(y => ({ value: y, label: String(y) }));
        },

        periodLabel() {
            return `${this.monthLabel(Number(this.periodMonth))} ${this.periodYear}`;
        },

        /** Montant d'un champ de la déclaration courante — 0 FCFA tant qu'aucune n'existe pour la période. */
        amountOf(field) {
            return this.formatAmount(this.selectedDeclaration ? this.selectedDeclaration[field] : 0);
        },

        onPeriodChange() {
            // Le mois ne change que la sélection côté client : la liste de l'année est déjà chargée.
        },

        async onYearChange() {
            await this.loadTaxDeclarations();
        },

        async loadTaxDeclarations() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.periodYear) params.set('year', this.periodYear);
                this.taxDeclarations = await api.get(`/finance/tax-declarations?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des déclarations fiscales.');
            } finally {
                this.isLoading = false;
            }
        },

        openTaxModal() {
            this.taxForm = { month: new Date().getMonth() + 1, year: new Date().getFullYear() };
            this.isTaxModalOpen = true;
        },

        async submitTaxDeclaration() {
            this.isGeneratingTax = true;
            try {
                await api.post('/finance/tax-declaration', {
                    month: Number(this.taxForm.month),
                    year: Number(this.taxForm.year)
                });
                toast.success('Déclaration fiscale générée.');
                this.isTaxModalOpen = false;
                // On bascule les cartes KPI sur la période qui vient d'être générée, sinon l'écran
                // resterait sur un mois vide alors que l'utilisateur vient de produire la déclaration.
                this.periodMonth = Number(this.taxForm.month);
                this.periodYear = Number(this.taxForm.year);
                await this.loadTaxDeclarations();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération de la déclaration.'));
            } finally {
                this.isGeneratingTax = false;
            }
        },

        /** État synthétique PDF — ouvert dans la modale d'aperçu partagée (pdf-preview.js). */
        async downloadDeclarationPdf(declarationId) {
            if (!declarationId || declarationId === 'undefined') {
                console.error('Identifiant de déclaration invalide ou indéfini', declarationId);
                return;
            }
            this.downloadingPdfId = declarationId;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/tax-declarations/${declarationId}/pdf`,
                    'État synthétique — déclaration fiscale',
                    `Declaration-Fiscale-${declarationId}.pdf`
                );
            } finally {
                this.downloadingPdfId = null;
            }
        },

        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/treasury.js`

````javascript
/**
 * Module Comptabilité & Fiscalité — Tableau de bord Trésorerie (/tresorerie). Agrège les encaissements
 * (module Caisse) et les décaissements déjà existants sur une période ; n'introduit aucun nouveau
 * registre de mouvements. Réservé au Directeur et à la Finance (garde réelle : FinanceController).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('treasuryView', () => ({
        data: null,
        isLoading: false,
        error: null,

        // Période par défaut : du 1er du mois courant à aujourd'hui (même convention que le rapport d'assiduité).
        startDate: '',
        endDate: '',

        init() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);

            this.load();
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        async load() {
            if (!this.startDate || !this.endDate) return;

            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ startDate: this.startDate, endDate: this.endDate });
                this.data = await window.api.get(`/finance/treasury?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement de la trésorerie.');
                this.data = null;
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.load();
        },

        resetFilters() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);
            this.applyFilters();
        },

        periodLabel() {
            if (!this.data) return '';
            return `${this.formatDate(this.data.startDate)} → ${this.formatDate(this.data.endDate)}`;
        },

        kindBadgeClass(kind) {
            // Classes du design system (.status-badge-*) plutôt qu'une paire bg/text écrite ici : la
            // pastille de la Trésorerie doit être la même que celle de la Caisse ou des Frais, sans
            // qu'un écran ait à connaître les codes couleur.
            return kind === 'Encaissement' ? 'status-badge-success' : 'status-badge-danger';
        },

        /**
         * Part d'un poste dans le total de SA colonne (encaissé ou décaissé), en pourcentage entier
         * 0-100 — la largeur de la jauge, pas un libellé.
         *
         * Le total est le dénominateur, jamais le plus gros poste : « 60 % des encaissements en
         * espèces » est ce qui intéresse le directeur ; « deux fois plus que le mobile money » ne se
         * lit pas sur une barre. Un total nul rend 0 plutôt que NaN, qui produirait `width: NaN%` —
         * une barre pleine dans certains navigateurs.
         */
        sharePct(amount, total) {
            const t = Number(total) || 0;
            if (t <= 0) return 0;
            return Math.min(100, Math.max(0, Math.round((Number(amount) || 0) / t * 100)));
        },

        /** Même part, en texte, pour l'accompagnement du libellé (la couleur seule ne suffit pas). */
        shareLabel(amount, total) {
            return `${this.sharePct(amount, total)} %`;
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/financial-report.js`

````javascript
/**
 * JGK-F05 — Rapports financiers (/rapports/financiers). Consolidation des encaissements sur une
 * période, ventilée par cycle, par classe et par mode de paiement, doublée de l'export comptable
 * .xlsx.
 *
 * Les deux boutons interrogent des routes JUMELLES du même contrôleur (GET /finance/reports/revenue
 * et .../revenue/excel) : l'écran et le fichier téléchargé portent donc exactement les mêmes chiffres,
 * sur exactement les mêmes bornes de période (voir GetRevenueConsolidationExcelQueryHandler, qui
 * réutilise la requête d'affichage au lieu de recalculer).
 *
 * Réservé au Directeur et à la Finance, ET aux formules Standard/Premium : la garde réelle est
 * FinancialReportsController ([Authorize] + [RequireFeature(AdvancedFinancialReports)]) — ce que fait
 * `featureGate` en façade n'est qu'un confort d'affichage (voir features.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('financialReportView', () => ({
        data: null,
        isLoading: false,
        error: null,
        exporting: false,

        // Verrou de formule porté par CE composant plutôt que par le composant partagé `featureGate` :
        // le bouton d'export appelle exportExcel(), qui écrit `exporting` — il doit vivre dans la même
        // portée Alpine, sans quoi une portée imbriquée masquerait la propriété du parent.
        featureAllowed: false,
        featureLabel: window.features.upgradeLabel('AdvancedFinancialReports'),

        // Période par défaut : du 1er janvier à aujourd'hui — l'API retient l'année civile en cours
        // quand aucune borne n'est fournie, l'écran affiche donc d'emblée la même chose qu'elle.
        from: '',
        to: '',

        init() {
            const today = new Date();
            this.from = this.toIsoDate(new Date(today.getFullYear(), 0, 1));
            this.to = this.toIsoDate(today);

            this.loadFeatures();
            this.load();
        },

        async loadFeatures() {
            await window.features.load();
            this.featureAllowed = window.features.has('AdvancedFinancialReports');
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        async load() {
            if (!this.from || !this.to) return;

            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ from: this.from, to: this.to });
                this.data = await window.api.get(`/finance/reports/revenue?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du rapport financier.');
                this.data = null;
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.load();
        },

        resetFilters() {
            const today = new Date();
            this.from = this.toIsoDate(new Date(today.getFullYear(), 0, 1));
            this.to = this.toIsoDate(today);
            this.applyFilters();
        },

        /**
         * Téléchargement .xlsx — fetch bas niveau plutôt que window.api : la réponse est un binaire,
         * pas du JSON, et le jeton doit voyager en en-tête (il vit dans localStorage, jamais dans un
         * cookie). Même mécanique que l'export du rapport d'assiduité (attendance-report.js).
         */
        async exportExcel() {
            if (!this.from || !this.to || this.exporting) return;

            this.exporting = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const params = new URLSearchParams({ from: this.from, to: this.to });
                const response = await fetch(`/api/v1/finance/reports/revenue/excel?${params.toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });

                if (!response.ok) {
                    this.error = response.status === 403
                        ? "L'export comptable est réservé aux formules Standard et Premium."
                        : "Erreur lors de l'export du rapport financier.";
                    return;
                }

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = this.exportFileName(response);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'export du rapport financier.");
            } finally {
                this.exporting = false;
            }
        },

        /** Nom du fichier : Content-Disposition renvoyé par l'API si présent, sinon reconstruit des bornes. */
        exportFileName(response) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);

            return `revenus_${this.from.replace(/-/g, '')}_${this.to.replace(/-/g, '')}.xlsx`;
        },

        periodLabel() {
            if (!this.data) return '';
            return `${this.formatDate(this.data.from)} → ${this.formatDate(this.data.to)}`;
        },

        /** Part d'une ligne dans le total encaissé — 0 quand le total l'est aussi (pas de division par zéro). */
        share(amount) {
            const total = this.data ? this.data.totalCollected : 0;
            if (!total) return 0;
            return amount / total;
        },

        // --- GRAPHIQUES — même technique que le tableau de bord financier (dashboard.js) : donut en
        // SVG inline (stroke-dasharray) et barres CSS en pourcentage, pas de librairie de graphes.

        /** Hauteur de barre (%) relative au mois le plus fort de la période — pas au total, pour que
         * la série d'évolution reste lisible même sur une période longue (barWidth() de dashboard.js
         * fait de même pour comparer jour/mois/année). */
        monthBarHeight(amount) {
            if (!this.data || this.data.byMonth.length === 0) return 2;
            const max = Math.max(...this.data.byMonth.map((m) => m.amount), 1);
            return Math.max(2, Math.round((amount / max) * 100));
        },

        /** "2026-01-01" → "Janv. 26", même paire mois-court/année-courte que le badge d'année scolaire. */
        formatMonth(periodStr) {
            const d = new Date(periodStr);
            const label = new Intl.DateTimeFormat('fr-FR', { month: 'short', year: '2-digit' }).format(d);
            return label.charAt(0).toUpperCase() + label.slice(1);
        },

        /** Palette et construction de segments IDENTIQUES à disbursementSegments()/getCategoryColor()
         * de dashboard.js — même astuce de cercle SVG (circonférence 100, stroke-dashoffset cumulé). */
        getCategoryColor(index) {
            const colors = ['#F59E0B', '#EF4444', '#8B5CF6', '#10B981', '#3B82F6', '#EC4899', '#6366F1'];
            return colors[index % colors.length];
        },

        /**
         * Tableau de longueur FIXE (MAX_PAYMENT_SEGMENTS), jamais parcouru par `<template x-for>` dans
         * le SVG appelant : le contenu d'un <template> est TOUJOURS analysé en namespace HTML, y compris
         * quand le <template> est lui-même un enfant de <svg> — un <circle> qui en sort n'est donc pas un
         * SVGCircleElement mais un élément inconnu, invisible sans la moindre erreur visible à l'écran
         * (seule la console révèle "segment is not defined" / "reading 'children'"). D'où un nombre FIXE
         * de <circle> écrits en dur dans la vue plutôt qu'une boucle — même piège que celui déjà présent
         * dans disbursementSegments()/Dashboard/Index.cshtml (onglet Décaissements), non corrigé ici :
         * hors périmètre de ce ticket, signalé séparément.
         */
        paymentMethodSegments() {
            const MAX_PAYMENT_SEGMENTS = 7; // longueur de la palette getCategoryColor()
            const empty = { fraction: 0, offset: 0, color: '#E5E7EB', name: '', amount: 0 };
            if (!this.data || this.data.byPaymentMethod.length === 0 || !this.data.totalCollected) {
                return Array(MAX_PAYMENT_SEGMENTS).fill(empty);
            }
            let currentOffset = 0;
            const segments = this.data.byPaymentMethod.map((item, index) => {
                const fraction = (item.amount / this.data.totalCollected) * 100;
                const segment = { fraction, offset: currentOffset, color: this.getCategoryColor(index), name: item.label, amount: item.amount };
                currentOffset -= fraction; // inversé : le sens de l'arc SVG est opposé à celui de la liste
                return segment;
            });
            while (segments.length < MAX_PAYMENT_SEGMENTS) segments.push(empty);
            return segments;
        },

        /** Même pctVal(part, total) que dashboard.js : pourcentage borné 0-100, jamais de division par zéro. */
        pctVal(part, total) {
            if (!total) return 0;
            return Math.min(100, Math.round((part / total) * 100));
        },

        /** Les 6 classes les plus fortes : la barre est un résumé visuel, le tableau plus bas garde le détail complet. */
        topClassrooms() {
            if (!this.data) return [];
            return this.data.byClassroom.slice(0, 6);
        },

        formatPercent(ratio) {
            return `${((ratio || 0) * 100).toFixed(1)} %`;
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/network-guard.js`

````javascript
/**
 * Guard Réseau, sonde de joignabilité et enregistrement du Service Worker (PWA & Résilience).
 * Conforme au Volume 0 §0.8, à la décision D-01 (« plus d'Offline-First ») et aux règles AGENTS.md :
 * aucune base locale, aucune file de mutations, aucune écriture métier hors du serveur.
 *
 * Ce module est la SOURCE UNIQUE de l'état de connectivité côté navigateur. Il expose trois états —
 * et non un simple booléen — parce que `navigator.onLine` ne répond pas à la question qui intéresse
 * un utilisateur en train de saisir un paiement :
 *
 *   'online'   : le serveur a répondu (ou le navigateur n'a jamais signalé de coupure).
 *   'offline'  : le navigateur a signalé une coupure, ou la sonde n'a pas obtenu de réponse.
 *   'checking' : une sonde est en cours — état transitoire, affiché pour ne pas laisser croire à
 *                une coupure pendant les quelques centaines de millisecondes de la vérification.
 *
 * `navigator.onLine` ne prouve QUE l'activité de l'interface réseau : il reste `true` derrière un
 * portail captif d'hôtel ou une 4G qui ne route plus rien. D'où la sonde HEAD sur un asset statique
 * — pas d'authentification, pas de JWT, pas de RLS en jeu, donc utilisable même session expirée.
 */
const NETWORK_PROBE_URL = '/manifest.json';
const NETWORK_PROBE_TIMEOUT_MS = 5000;

window.networkGuard = {
    /** 'online' | 'offline' | 'checking' */
    state: navigator.onLine ? 'online' : 'offline',
    lastProbeAt: null,
    listeners: [],
    probeInFlight: null,

    /** Conservé pour les appelants historiques qui testent un booléen. */
    get isOnline() {
        return this.state === 'online';
    },

    init() {
        // Le navigateur annonce le retour du réseau : on ne le croit pas sur parole, on vérifie que
        // le serveur répond vraiment avant de repasser au vert.
        window.addEventListener('online', () => { this.check(); });

        // Une coupure annoncée, en revanche, est fiable : inutile de sonder pour la confirmer.
        window.addEventListener('offline', () => { this.setState('offline'); });

        this.registerServiceWorker();
    },

    onChange(cb) {
        if (typeof cb === 'function') {
            this.listeners.push(cb);
        }
    },

    setState(next) {
        if (this.state === next) return;
        this.state = next;
        this.notifyListeners();
    },

    notifyListeners() {
        const online = this.isOnline;
        this.listeners.forEach((cb) => {
            try { cb(online, this.state); } catch (e) { console.error(e); }
        });
    },

    /**
     * Vérifie que le serveur répond réellement. Utilisé par le bouton « Vérifier la connexion » et
     * au retour d'un événement `online`. Les appels concurrents partagent la même sonde : marteler
     * le bouton ne déclenche pas dix requêtes.
     */
    async check() {
        if (this.probeInFlight) return this.probeInFlight;

        if (!navigator.onLine) {
            this.setState('offline');
            return false;
        }

        this.setState('checking');

        this.probeInFlight = this.probe()
            .then((reachable) => {
                this.lastProbeAt = new Date();
                this.setState(reachable ? 'online' : 'offline');
                return reachable;
            })
            .finally(() => { this.probeInFlight = null; });

        return this.probeInFlight;
    },

    /**
     * HEAD sur un asset statique, hors cache et hors Service Worker (sw.js n'intercepte ni les
     * requêtes non-GET ni ce chemin). Le paramètre `_probe` déjoue les caches intermédiaires des
     * opérateurs, fréquents sur les réseaux mobiles sénégalais et capables de renvoyer un 200 alors
     * que la liaison est morte.
     */
    async probe() {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), NETWORK_PROBE_TIMEOUT_MS);

        try {
            const response = await fetch(`${NETWORK_PROBE_URL}?_probe=${Date.now()}`, {
                method: 'HEAD',
                cache: 'no-store',
                credentials: 'same-origin',
                signal: controller.signal
            });
            return response.ok;
        } catch (e) {
            return false;
        } finally {
            clearTimeout(timer);
        }
    },

    /**
     * Soumission résiliente d'une écriture métier (ticket JGK-L02) : retry avec backoff exponentiel
     * tant que l'onglet reste ouvert, PENDANT une coupure réseau courte. Reste strictement dans le
     * cadre D-01/D-09 (voir l'en-tête de ce fichier) : aucune persistance de la tentative — si l'onglet
     * se ferme avant confirmation, la saisie est perdue par conception. C'est cette frontière, pas une
     * limite technique, qui garde ce mécanisme dans « résilience réseau » et hors d'« offline-first ».
     *
     * Ne retente JAMAIS une erreur MÉTIER (4xx/5xx) : dès que le serveur répond, quelle que soit la
     * réponse, l'appelant la reçoit immédiatement. Seule une erreur RÉSEAU (fetch qui ne reçoit aucune
     * réponse — coupure, DNS, timeout) déclenche une nouvelle tentative.
     *
     * @param {string} url
     * @param {RequestInit} [options] - options fetch (method, headers, body...), rejouées À
     *   L'IDENTIQUE à chaque tentative. Si l'endpoint appelé attend une clé d'idempotence (ex.
     *   POST /finance/payments, ticket JGK-L01), c'est à l'APPELANT de l'inclure dans `options.body`
     *   AVANT le premier essai (voir `newIdempotencyKey()`) — cette fonction ne connaît pas la forme
     *   du corps de chaque endpoint, elle se contente de renvoyer le même corps à chaque tentative.
     * @param {object} [config]
     * @param {number} [config.maxAttempts=5]
     * @param {(state: 'sending'|'retrying'|'done'|'failed', attempt: number) => void} [config.onStateChange]
     *   'sending' : première tentative. 'retrying' : nouvelle tentative après une coupure — l'appelant
     *   y affiche l'état « en attente d'envoi ». 'done' : une réponse HTTP est arrivée (succès ou
     *   erreur métier). 'failed' : maxAttempts atteint sans qu'aucune tentative n'ait joint le serveur.
     * @returns {Promise<Response>}
     */
    async submitWithRetry(url, options = {}, config = {}) {
        const maxAttempts = config.maxAttempts ?? 5;

        for (let attempt = 1; ; attempt++) {
            config.onStateChange?.(attempt === 1 ? 'sending' : 'retrying', attempt);

            try {
                const response = await fetch(url, options);
                config.onStateChange?.('done', attempt);
                return response;
            } catch (networkError) {
                if (attempt >= maxAttempts) {
                    config.onStateChange?.('failed', attempt);
                    throw networkError;
                }

                // Backoff exponentiel plafonné : 1s, 2s, 4s, 8s, puis 15s — assez patient pour une
                // micro-coupure mobile sénégalaise typique, sans faire attendre l'utilisateur des minutes.
                const delayMs = Math.min(1000 * 2 ** (attempt - 1), 15000);
                await new Promise((resolve) => setTimeout(resolve, delayMs));
            }
        }
    },

    /** UUID v4 côté client pour `submitWithRetry` — à générer UNE FOIS par formulaire, pas par tentative. */
    newIdempotencyKey() {
        return crypto.randomUUID();
    },

    registerServiceWorker() {
        if ('serviceWorker' in navigator && (window.location.protocol === 'https:' || window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')) {
            window.addEventListener('load', () => {
                navigator.serviceWorker.register('/sw.js', { scope: '/' })
                    .then((reg) => {
                        console.debug('SamaEcole ServiceWorker enregistré avec succès. Scope :', reg.scope);
                    })
                    .catch((err) => {
                        console.warn('Échec de l\'enregistrement du ServiceWorker :', err);
                    });
            });
        }
    }
};

// Initialisation immédiate du Guard Réseau
window.networkGuard.init();

document.addEventListener('alpine:init', () => {
    /**
     * Bandeau d'alerte plein cadre, affiché uniquement sur une coupure CONFIRMÉE ('offline').
     * Volontairement muet pendant 'checking' : un bandeau rouge qui clignote à chaque vérification
     * de routine finit par être ignoré.
     */
    Alpine.data('networkGuardBanner', () => ({
        state: window.networkGuard.state,
        showReconnectedToast: false,

        init() {
            // La reprise se lit sur le dernier état CONFIRMÉ, pas sur l'état précédent brut : le
            // retour du réseau passe par offline → checking → online, et comparer à 'checking'
            // ferait manquer la transition à chaque fois, donc le toast ne s'afficherait jamais.
            let lastConfirmed = this.state === 'checking' ? 'online' : this.state;

            window.networkGuard.onChange((online, state) => {
                this.state = state;
                if (state === 'checking') return;

                if (online && lastConfirmed === 'offline') {
                    this.showReconnectedToast = true;
                    setTimeout(() => { this.showReconnectedToast = false; }, 5000);
                }

                lastConfirmed = state;
            });
        },

        get offline() { return this.state === 'offline'; }
    }));

    /**
     * Badge de connectivité de la barre supérieure.
     *
     * Le compteur affiché est un nombre de BROUILLONS DE FORMULAIRE conservés sur ce poste
     * (localStorage, voir form-draft.js) — pas une file de mutations en attente d'envoi : il n'en
     * existe pas, et il n'en existera pas (D-01/D-09). La nuance est tout l'intérêt du badge : elle
     * dit à la secrétaire « ta saisie n'est pas perdue » sans lui laisser croire « c'est enregistré ».
     */
    Alpine.data('connectivityBadge', () => ({
        state: window.networkGuard.state,
        draftCount: 0,

        init() {
            window.networkGuard.onChange((online, state) => {
                this.state = state;
                this.refreshDrafts();
            });

            this.refreshDrafts();
            window.addEventListener('samaecole:drafts-changed', () => this.refreshDrafts());
            window.addEventListener('focus', () => this.refreshDrafts());
        },

        refreshDrafts() {
            this.draftCount = window.formDraft ? window.formDraft.count() : 0;
        },

        label() {
            if (this.state === 'checking') return 'Vérification…';
            if (this.state === 'offline') {
                return this.draftCount > 0
                    ? `Hors ligne — ${this.draftCount} saisie${this.draftCount > 1 ? 's' : ''} conservée${this.draftCount > 1 ? 's' : ''}`
                    : 'Hors ligne';
            }
            return this.draftCount > 0 ? `Connecté — ${this.draftCount} brouillon${this.draftCount > 1 ? 's' : ''}` : 'Connecté';
        },

        title() {
            if (this.state === 'checking') return 'Vérification de la liaison avec le serveur…';
            if (this.state === 'offline') {
                return this.draftCount > 0
                    ? `Aucune liaison avec le serveur. ${this.draftCount} saisie(s) en cours conservée(s) sur ce poste : rien n'est enregistré tant que la connexion n'est pas rétablie.`
                    : "Aucune liaison avec le serveur. Les enregistrements sont impossibles tant que la connexion n'est pas rétablie.";
            }
            return this.draftCount > 0
                ? `Connecté au serveur. ${this.draftCount} brouillon(s) de formulaire non soumis sur ce poste.`
                : 'Connecté au serveur.';
        },

        badgeClass() {
            if (this.state === 'checking') return 'bg-primary-50 text-primary-700';
            if (this.state === 'offline') return 'bg-warning-bg text-warning';
            return 'bg-success-bg text-success';
        },

        dotClass() {
            if (this.state === 'checking') return 'bg-primary animate-pulse';
            if (this.state === 'offline') return 'bg-warning';
            return 'bg-success';
        },

        checking() { return this.state === 'checking'; },

        recheck() { window.networkGuard.check(); }
    }));
});
````


#### `src/SamaEcole.Web/wwwroot/js/api.js`

````javascript
/**
 * Client HTTP de l'API — dépend de auth.js, à charger avant lui.
 *
 * Sa raison d'être : une session ne doit jamais se terminer brutalement pendant une saisie. L'access
 * token ne vit que 15 minutes ; ce client le renouvelle de façon transparente (ticket JGK-A04), et
 * l'appelant n'a rien à savoir de tout cela — il fait api.get(...) et reçoit ses données.
 */

// Ticket JGK-I04 : point d'atterrissage unique quand SubscriptionAwaitingPaymentMiddleware bloque un
// appel. Le JWT ne voyage jamais sur une navigation classique (localStorage, pas de cookie) — c'est
// donc ICI, au premier appel d'API d'une page bloquée, que la redirection peut réellement se décider,
// jamais côté serveur au moment du rendu de la page (voir le commentaire de classe du middleware).
const SUBSCRIPTION_RESTRICTED_PATH = '/abonnement/paiement';

// Onboarding & Pricing SaaS : repli si la réponse ONBOARDING_REQUIRED ne porte pas sa destination.
const ONBOARDING_PATH = '/onboarding/select-profile';

/**
 * Codes HTTP qui décrivent une indisponibilité PASSAGÈRE de l'infrastructure, pas un refus : un
 * reverse-proxy qui n'a pas encore de backend prêt (502), un redémarrage applicatif (503), une
 * requête coupée en amont (504). Un 4xx n'est jamais rejoué — le serveur a compris et a dit non.
 */
const TRANSIENT_HTTP_STATUSES = [502, 503, 504];

window.api = {
    baseUrl: '/api/v1',

    // 3 tentatives = 2 reprises, soit ~1,6 s de patience au pire avant de rendre la main. Au-delà,
    // l'utilisateur croit l'écran figé et recharge la page — ce qui annule le bénéfice.
    retryMaxAttempts: 3,
    retryBaseDelayMs: 400,

    async request(endpoint, method = 'GET', body = null) {
        // Renouvellement PRÉVENTIF : si l'on sait déjà que le jeton est périmé, inutile de dépenser
        // un aller-retour pour se faire répondre 401.
        if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await this.refreshOrRedirect();
        }

        let response = await this.sendWithRetry(endpoint, method, body);

        // Filet de sécurité : le jeton a pu être révoqué côté serveur, ou l'horloge du poste être
        // décalée au point que le renouvellement préventif n'ait pas vu venir l'expiration. Une seule
        // reprise — si le second appel échoue encore, insister ne ferait que boucler.
        if (response.status === 401 && window.auth.isAuthenticated()) {
            await this.refreshOrRedirect();
            response = await this.sendWithRetry(endpoint, method, body);
        }

        if (response.status === 401) {
            window.auth.redirectToLogin();
            throw await this.toError(response);
        }

        if (!response.ok) {
            const error = await this.toError(response);

            // Ticket JGK-I04 : un 403 "ordinaire" (rôle insuffisant) ne doit PAS rediriger — seul ce
            // code précis, posé par SubscriptionAwaitingPaymentMiddleware, déclenche la redirection.
            if (error.code === 'SUBSCRIPTION_AWAITING_PAYMENT'
                && window.location.pathname !== SUBSCRIPTION_RESTRICTED_PATH) {
                window.location.assign(SUBSCRIPTION_RESTRICTED_PATH);
            }

            // Onboarding & Pricing SaaS : OnboardingRoutingMiddleware refuse toute l'API tant que le
            // Directeur n'a pas choisi profil et tranche. Même principe — seul ce code précis redirige,
            // jamais un 403 ordinaire. La destination vient de la réponse (details.redirectTo), avec le
            // chemin connu en repli ; aucune redirection si l'on y est déjà (pas de boucle).
            if (error.code === 'ONBOARDING_REQUIRED') {
                const target = (error.details && error.details.redirectTo) || ONBOARDING_PATH;
                if (window.location.pathname !== target) {
                    window.location.assign(target);
                }
            }

            throw error;
        }

        if (response.status === 204) return null;
        return await response.json();
    },

    /**
     * Reprise automatique des défaillances RÉSEAU passagères — le vrai quotidien d'une connexion
     * mobile sénégalaise : une requête part pendant un micro-basculement d'antenne et meurt seule,
     * alors que la suivante, 400 ms plus tard, passe sans problème.
     *
     * ┌─ RÈGLE ABSOLUE ────────────────────────────────────────────────────────────────────────┐
     * │ Seules les LECTURES (GET) sont rejouées. Jamais un POST/PUT/PATCH/DELETE.               │
     * └────────────────────────────────────────────────────────────────────────────────────────┘
     *
     * Un `fetch` qui échoue ne dit PAS si le serveur a traité la requête : la coupure peut être
     * survenue sur la réponse, la transaction étant déjà validée. Rejouer un POST /payments, c'est
     * donc encaisser deux fois le même versement ; rejouer une inscription, c'est consommer deux
     * matricules (AGENTS.md règle 3) pour un seul élève. Aucune reprise automatique ne peut
     * distinguer ces cas — c'est précisément ce qu'un identifiant d'idempotence serveur résoudrait,
     * et il n'en existe pas. L'écriture échouée remonte donc telle quelle à l'écran, qui conserve la
     * saisie (form-draft.js) et laisse l'utilisateur décider de renvoyer.
     */
    isRetryable(method) {
        return String(method).toUpperCase() === 'GET';
    },

    /** Palier exponentiel + gigue : deux onglets coupés ensemble ne repartent pas à la même seconde. */
    backoffDelayMs(attempt) {
        return (this.retryBaseDelayMs * Math.pow(2, attempt - 1)) + Math.floor(Math.random() * 100);
    },

    delay(ms) {
        return new Promise((resolve) => setTimeout(resolve, ms));
    },

    async sendWithRetry(endpoint, method, body) {
        const maxAttempts = this.isRetryable(method) ? this.retryMaxAttempts : 1;

        for (let attempt = 1; ; attempt++) {
            const isLastAttempt = attempt >= maxAttempts;

            try {
                const response = await this.send(endpoint, method, body);

                if (!isLastAttempt && TRANSIENT_HTTP_STATUSES.includes(response.status)) {
                    await this.delay(this.backoffDelayMs(attempt));
                    continue;
                }

                return response;
            } catch (error) {
                // `navigator.onLine` à false : le poste est franchement déconnecté, insister ne fait
                // que retarder le message d'erreur que l'utilisateur doit voir tout de suite.
                if (isLastAttempt || error.code !== 'NETWORK_OFFLINE' || !navigator.onLine) {
                    throw error;
                }

                await this.delay(this.backoffDelayMs(attempt));
            }
        }
    },

    async send(endpoint, method, body) {
        if (!navigator.onLine) {
            const error = new Error("📡 Connexion réseau indisponible. Votre saisie a été sauvegardée en mémoire sur votre navigateur. Veuillez retenter l'envoi dès le rétablissement de la connexion.");
            error.code = 'NETWORK_OFFLINE';
            error.status = 0;
            throw error;
        }

        const headers = {};
        const token = window.auth.accessToken;

        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        // FormData (upload de fichier) : le navigateur doit fixer lui-même le Content-Type avec sa
        // frontière multipart — l'imposer ici casserait le décodage côté serveur.
        const isFormData = body instanceof FormData;
        if (!isFormData) {
            headers['Content-Type'] = 'application/json';
        }

        try {
            return await fetch(`${this.baseUrl}${endpoint}`, {
                method,
                headers,
                credentials: 'same-origin',
                body: isFormData ? body : (body ? JSON.stringify(body) : undefined)
            });
        } catch (fetchErr) {
            if (!navigator.onLine || fetchErr.name === 'TypeError' || (fetchErr.message && fetchErr.message.includes('Failed to fetch'))) {
                const error = new Error("📡 Connexion au serveur interrompue pendant l'envoi. Votre saisie est conservée en mémoire dans le navigateur. Veuillez retenter dès que le réseau est rétabli.");
                error.code = 'NETWORK_OFFLINE';
                error.status = 0;
                throw error;
            }
            throw fetchErr;
        }
    },

    /**
     * Le renouvellement a échoué : le refresh token est expiré, révoqué, ou a été rejoué et le
     * serveur a coupé toute la famille. Il n'y a plus de session à sauver — on renvoie l'utilisateur
     * vers la connexion, en gardant la page courante pour l'y ramener ensuite.
     */
    async refreshOrRedirect() {
        try {
            await window.auth.refresh();
        } catch (error) {
            window.auth.redirectToLogin();
            throw error;
        }
    },

    /** Vrai pour le 409 « cet élément existe dans les éléments supprimés » : à restaurer, pas à recréer. */
    isArchivedConflict(error) {
        return !!error && error.status === 409 && error.code === 'ARCHIVED_ENTITY_EXISTS';
    },

    /**
     * Traduit une erreur d'API en messages affichables SOUS LES CHAMPS du formulaire.
     *
     * L'API renvoie `details` sous la forme d'un DICTIONNAIRE { "FullName": ["Le nom est
     * obligatoire."] } — ExceptionHandlingMiddleware y place le dictionnaire Errors de
     * FluentValidation. Les formulaires attendaient jusqu'ici un TABLEAU de { field, reason } : la
     * condition Array.isArray() échouait donc toujours, et chaque erreur de saisie tombait dans le
     * message générique « global ». Aucune erreur ne s'est jamais affichée sous son champ.
     *
     * Les clés sont mises en minuscules pour correspondre aux noms attendus par les vues
     * (createErrors.fullname, createErrors.classroomid…).
     */
    toFieldErrors(error, fallbackMessage, aliases) {
        // Déjà porté par la modale universelle « Accès refusé » (access-denied.js) : aucun message
        // local, sinon il s'affiche en double derrière la modale.
        if (error && error.handledGlobally) return {};

        // Création refusée parce que l'identité existe parmi les éléments SUPPRIMÉS (409 ARCHIVED_ENTITY_EXISTS,
        // conception soft delete §3.2) : le message du serveur explique, et le drapeau `archivedConflict` permet au
        // formulaire d'afficher un bouton « Voir les éléments supprimés » (trash.js) plutôt qu'un simple bandeau rouge.
        if (this.isArchivedConflict(error)) {
            return { global: error.message || fallbackMessage, archivedConflict: true };
        }

        const details = error && error.details;

        if (details && typeof details === 'object' && !Array.isArray(details)) {
            const errors = {};

            Object.entries(details).forEach(([field, messages]) => {
                const text = Array.isArray(messages) ? messages[0] : String(messages);
                const key = field.toLowerCase();
                const target = (aliases && aliases[key]) || key;
                errors[target] = errors[target] ? `${errors[target]} ${text}` : text;
            });

            if (Object.keys(errors).length > 0) return this.surfaceUnmappedErrors(errors);
        }

        // Pas de détail par champ : conflit (409), règle métier, panne réseau… → message global.
        return { global: (error && error.message) || fallbackMessage };
    },

    /**
     * Clés d'erreur qu'une page SAIT afficher : toutes celles que ses éléments `x-text` lisent
     * (`createErrors.fullname`, `errors['email']`…). Convention commune à tous les écrans : chaque champ a son
     * `<p x-text="xxxErrors.champ">`, et le bandeau du haut lit `.global`. `null` quand la page n'affiche aucune
     * erreur par champ (pas de DOM, ou écran à bandeau unique) — il n'y a alors rien à comparer.
     */
    displayedErrorKeys() {
        if (typeof document === 'undefined' || !document.querySelectorAll) return null;

        const keys = new Set();
        const pattern = /\b\w*[Ee]rrors?\s*(?:\.\s*(\w+)|\[\s*['"]([^'"]+)['"]\s*\])/g;
        document.querySelectorAll('[x-text]').forEach((el) => {
            for (const m of (el.getAttribute('x-text') || '').matchAll(pattern)) {
                keys.add((m[1] || m[2]).toLowerCase());
            }
        });
        return keys.size > 0 ? keys : null;
    },

    /**
     * Aucune erreur serveur ne doit rester muette. Un 422 peut porter une clé (`GuardianPhone`, `BedId`…) que le
     * formulaire n'affiche nulle part : l'erreur était reçue puis jetée, et l'utilisateur voyait « Enregistrer »
     * sans aucun effet. Le message d'une telle clé est donc AJOUTÉ au bandeau `global` (la clé d'origine est
     * conservée : le code d'un écran peut la lire). Sans effet quand tout est affichable. Un `console.warn`
     * nomme le champ manquant pour que le développeur ajoute son emplacement à la vue.
     */
    surfaceUnmappedErrors(errors) {
        const shown = this.displayedErrorKeys();
        if (!shown) return errors;

        const orphans = Object.entries(errors)
            .filter(([key, value]) => key !== 'global' && typeof value === 'string' && !shown.has(key));
        if (orphans.length === 0) return errors;

        console.warn('[api] Erreur serveur sans emplacement d\'affichage dans cette page :',
            orphans.map(([key]) => key).join(', '));
        errors.global = [errors.global, ...orphans.map(([, value]) => value)].filter(Boolean).join(' ');
        return errors;
    },

    /**
     * Traduit une erreur d'API en UNE phrase à afficher dans un bandeau.
     *
     * À utiliser partout où un écran montre un bandeau unique plutôt que des erreurs sous les champs.
     * `error.message` seul ne suffit pas : sur un 422, le format normalisé
     * (docs/Volume_4_API_Design.md §0.4) place dans `message` la phrase de service « Une ou plusieurs
     * erreurs de validation se sont produites. » et garde l'explication RÉELLE dans `details`. Les
     * écrans qui affichaient `err.message` montraient donc la phrase de service et jetaient
     * l'explication — l'utilisateur apprenait qu'il y avait une erreur, jamais laquelle.
     *
     * Les détails sont joints par un espace : un même champ peut porter plusieurs reproches, et les
     * cacher tous sauf un obligerait l'utilisateur à corriger en plusieurs allers-retours.
     */
    toMessage(error, fallbackMessage) {
        // Déjà affiché par la modale universelle « Accès refusé » (access-denied.js) : on ne renvoie
        // rien, pour que le bandeau/toast de l'écran ne double pas la modale (« '' » est falsy, donc
        // x-show="error" et toast.error('') restent silencieux).
        if (error && error.handledGlobally) return '';

        const details = error && error.details;

        if (details && typeof details === 'object' && !Array.isArray(details)) {
            const phrases = Object.values(details)
                .flatMap(v => (Array.isArray(v) ? v : [v]))
                .map(v => String(v).trim())
                .filter(Boolean);

            if (phrases.length > 0) return phrases.join(' ');
        }

        return (error && error.message) || fallbackMessage;
    },

    /**
     * Message affichable quand l'API répond en dehors du format normalisé (docs/Volume_4_API_Design.md
     * §0.4) — typiquement une réponse 400 automatique d'ASP.NET Core pour un échec de model-binding
     * JSON (ex. une chaîne vide envoyée pour un champ Guid/décimal), qui survient AVANT que le
     * validateur applicatif n'ait la main et ne renvoie donc jamais de `message` exploitable. Sans
     * cette traduction, l'utilisateur voit littéralement « Erreur HTTP 400 » — vrai pour un
     * développeur, incompréhensible pour un(e) Secrétariat ou Directeur d'école.
     */
    httpFallbackMessage(status) {
        switch (status) {
            case 400: return "La demande n'a pas pu être traitée : un champ du formulaire contient une valeur inattendue. Vérifiez votre saisie puis réessayez.";
            case 403: return "Vous n'avez pas les droits nécessaires pour effectuer cette action.";
            case 404: return "Cet élément est introuvable — il a peut-être été supprimé ou déplacé entretemps.";
            case 409: return 'Cette donnée vient d\'être modifiée par quelqu\'un d\'autre. Rafraîchissez la page puis réessayez.';
            case 422: return 'Certaines informations saisies ne sont pas valides. Vérifiez le formulaire puis réessayez.';
            default: return status >= 500
                ? 'Le service rencontre une difficulté technique. Réessayez dans quelques instants.'
                : 'Une erreur inattendue est survenue. Réessayez, et contactez le support si le problème persiste.';
        }
    },

    /** Format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4), ou corps vide pour un 401 du middleware JWT. */
    async toError(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.details = payload.details;
            error.status = response.status;

            // Refus de PORTÉE de rôle (ForbiddenException serveur → code FORBIDDEN) : présenté par la
            // modale universelle « Accès refusé » (access-denied.js), identique sur tous les écrans,
            // plutôt que par le bandeau rouge de chacun. `handledGlobally` dit à toMessage()/
            // toFieldErrors() de ne rien renvoyer, pour ne pas doubler la modale. Le 403 d'abonnement
            // impayé (SUBSCRIPTION_AWAITING_PAYMENT) a son propre code et n'est pas concerné.
            if (response.status === 403 && payload.code === 'FORBIDDEN') {
                error.handledGlobally = true;
                window.dispatchEvent(new CustomEvent('sama:access-denied', {
                    detail: { message: payload.message }
                }));
            }

            // Modification directe INTERCEPTÉE (profil, cycles, module non souscrit) : elle change l'abonnement, donc elle passe
            // par une demande validée par le Super Admin. Le corps porte l'aperçu (changements, tarif ajusté, demande à envoyer) :
            // la modale universelle « Mise à jour de votre abonnement » (subscription-change.js) l'affiche, au lieu d'un bandeau
            // rouge par écran. Même mécanique `handledGlobally` que ci-dessus pour ne pas la redire.
            if (response.status === 409 && payload.code === 'SUBSCRIPTION_CHANGE_REQUIRED') {
                error.handledGlobally = true;
                window.dispatchEvent(new CustomEvent('sama:subscription-change', {
                    detail: { message: payload.message, preview: payload.details }
                }));
            }

            return error;
        }

        const fallback = new Error(this.httpFallbackMessage(response.status));
        fallback.status = response.status;
        return fallback;
    },

    get(endpoint) { return this.request(endpoint, 'GET'); },
    post(endpoint, body) { return this.request(endpoint, 'POST', body); },
    put(endpoint, body) { return this.request(endpoint, 'PUT', body); },
    patch(endpoint, body) { return this.request(endpoint, 'PATCH', body); },
    // `body` OPTIONNEL : la plupart des suppressions n'en ont pas, mais celles qui se confirment par
    // recopie d'un libellé en transportent un (DELETE /school-years/{id}). Sans argument, la requête
    // part exactement comme avant — `request` n'ajoute de corps que s'il y en a un.
    delete(endpoint, body) { return this.request(endpoint, 'DELETE', body); },

    /**
     * Récupère la TOTALITÉ d'une collection paginée (`{ items, totalCount, page, pageSize }`), en
     * enchaînant les pages jusqu'à `totalCount`.
     *
     * Pourquoi ce helper existe : les validateurs serveur plafonnent `pageSize` à 100
     * (GetStudentsQueryValidator.MaxPageSize & consorts) — ce plafond est la seule chose qui empêche
     * un client de dicter la taille de la réponse, il ne doit PAS être relevé. Or plusieurs écrans
     * demandaient `pageSize=1000` pour remplir un menu déroulant : la requête partait en 422
     * VALIDATION_ERROR et le sélecteur restait vide, SANS message (bug constaté sur /teachers le
     * 27/08/2026, puis sur les trois écrans Surveillant — billets, discipline, convocations).
     *
     * Le repli « demander seulement 100 » n'est pas acceptable pour les élèves : un établissement
     * sénégalais dépasse couramment le millier (cf. GetStudentsQuery), et l'écran aurait alors
     * silencieusement masqué les élèves au-delà du centième — un bug invisible, pire que le bug
     * visible qu'il remplace.
     *
     * `maxPages` est un garde-fou anti-boucle (60 × 100 = 6 000 lignes) : au-delà, un menu déroulant
     * n'est plus le bon composant, il faut une recherche serveur (cf. le widget de /caisse).
     */
    async getAllPages(endpoint, options) {
        const pageSize = (options && options.pageSize) || 100;
        const maxPages = (options && options.maxPages) || 60;
        const separator = endpoint.includes('?') ? '&' : '?';
        const all = [];

        for (let page = 1; page <= maxPages; page++) {
            const data = await this.get(`${endpoint}${separator}page=${page}&pageSize=${pageSize}`);
            const items = (data && data.items) || [];
            all.push(...items);

            // Page vide => plus rien à lire (borne sûre même si totalCount venait à manquer).
            if (items.length === 0) break;

            const total = data && typeof data.totalCount === 'number' ? data.totalCount : all.length;
            if (all.length >= total) break;
        }

        return all;
    },
    /** Upload multipart (FormData) — même robustesse (renouvellement de jeton, 401, erreurs) que post(). */
    upload(endpoint, formData) { return this.request(endpoint, 'POST', formData); },

    /**
     * Écriture résiliente (ticket JGK-L03) : câble `window.networkGuard.submitWithRetry` (JGK-L02)
     * sur une méthode d'écriture, au lieu du simple `send()` non rejoué de `request()`. Réservé aux
     * écrans qui en ont explicitement besoin (Caisse, Pointage) — les autres écritures du produit
     * gardent volontairement le comportement « échec immédiat + brouillon local » de `post()`
     * (voir le commentaire de `isRetryable` ci-dessus).
     *
     * `config.onStateChange` est transmis tel quel à `submitWithRetry` : 'sending' | 'retrying' |
     * 'done' | 'failed'. Contrairement à `request()`, AUCUNE reprise sur 401 n'est tentée ICI — un
     * jeton expiré pendant une coupure réseau est un cas assez rare pour ne pas complexifier la seule
     * fonction dont la propriété recherchée est justement la simplicité de son chemin d'erreur.
     */
    async requestWithRetry(endpoint, method, body, config = {}) {
        if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await this.refreshOrRedirect();
        }

        const headers = { 'Content-Type': 'application/json' };
        const token = window.auth.accessToken;
        if (token) headers['Authorization'] = `Bearer ${token}`;

        const options = {
            method,
            headers,
            credentials: 'same-origin',
            body: body ? JSON.stringify(body) : undefined
        };

        let response;
        try {
            response = await window.networkGuard.submitWithRetry(`${this.baseUrl}${endpoint}`, options, config);
        } catch {
            // maxAttempts atteint sans qu'aucune tentative n'ait joint le serveur : même message et
            // même code que send() pour rester interprétable par toMessage()/toFieldErrors().
            const error = new Error("📡 Connexion au serveur interrompue après plusieurs tentatives. Rien n'a été enregistré tant que la connexion n'est pas rétablie.");
            error.code = 'NETWORK_OFFLINE';
            error.status = 0;
            throw error;
        }

        if (response.status === 401) {
            window.auth.redirectToLogin();
            throw await this.toError(response);
        }

        if (!response.ok) throw await this.toError(response);
        if (response.status === 204) return null;
        return await response.json();
    },

    postWithRetry(endpoint, body, config) { return this.requestWithRetry(endpoint, 'POST', body, config); }
};
````


## A.3 Composants Razor transverses


#### `src/SamaEcole.Web/TagHelpers/ReceiptA5TagHelper.cs`

````csharp
using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>
/// Châssis commun aux deux documents imprimables au format A5 paysage : le reçu de paiement
/// (/caisse) et le reçu/attestation d'inscription (/inscriptions). Les deux partageaient, recopié à
/// l'identique, le conteneur (<c>id="receipt-printable"</c>, mêmes classes Tailwind), l'en-tête
/// (logo + nom d'établissement + coordonnées + mentions légales) et le pied de page (case « Fait à… »,
/// cachet officiel, deux blocs de signature). Seuls varient : les pastilles en haut à droite, la
/// pastille centrée optionnelle (N° d'inscription), le corps du document (tableau de règlement côté
/// caisse ; déclaration + échéancier côté inscription), le libellé du second signataire, et la mention
/// en bas de page.
///
/// IMPORTANT — deux choix de conception, tous deux dictés par des vérifications FAITES, pas supposées :
///
/// 1. Ce composant NE PORTE PAS la garde <c>&lt;template x-if="receipt"&gt;</c> : elle reste écrite en
///    clair dans chaque vue appelante, autour de <c>&lt;receipt-a5&gt;</c>. `x-show` n'est pas une
///    protection — Alpine évalue quand même les liaisons du sous-arbre dès le montage — et
///    `regression-guards.test.mjs` (garde-fou NULL_DEREF_DEBT) lit le TEXTE SOURCE des vues pour
///    vérifier cette garde : un Tag Helper qui l'émettrait lui-même la rendrait invisible à cette
///    analyse statique.
///
/// 2. Les sections variables (pastilles, mention de pied) sont découpées par MARQUEURS HTML
///    (<c>&lt;!--badges--&gt;…&lt;!--/badges--&gt;</c>), jamais par des Tag Helpers enfants séparés
///    communiquant via <c>TagHelperContext.Items</c> — le patron que suit pourtant
///    <see cref="ModalShellTagHelper"/> (<c>&lt;modal-subtitle&gt;</c> etc.) partout ailleurs dans ce
///    projet. Vérifié PAR INSTRUMENTATION DIRECTE (02-03/09/2026) sur le SDK de cette machine : un Tag
///    Helper enfant imbriqué reçoit un <c>TagHelperContext.Items</c> qui n'est PAS le même objet que
///    celui de son parent (deux hash codes différents, confirmés en traçant les deux) — le contenu écrit
///    par l'enfant n'atteint donc jamais le parent. C'est un défaut de la plateforme, PRÉEXISTANT et
///    INDÉPENDANT de ce composant : il rend d'ores et déjà muets tous les <c>&lt;modal-subtitle&gt;</c>,
///    <c>&lt;modal-title&gt;</c> et <c>&lt;modal-footer&gt;</c> du site (vérifié sur /convocations ET
///    sur une sonde isolée, sans aucun rapport avec /caisse ou /inscriptions). Le corriger touche un
///    mécanisme partagé par une dizaine d'écrans et dépasse le périmètre de cette factorisation — signalé
///    séparément. En attendant, un SEUL appel à <c>GetChildContentAsync()</c> — celui de ce Tag Helper
///    sur SES PROPRES enfants directs — est fiable (c'est ce que prouve déjà le corps du document, qui
///    s'affiche correctement) ; le découpage par marqueurs se fait ensuite en mémoire, sur cette unique
///    chaîne de confiance.
///
/// Usage (voir Views/Caisse/Index.cshtml et Views/Enrollments/Index.cshtml) :
/// <code>
/// &lt;template x-if="receipt"&gt;
///     &lt;receipt-a5 signatory="Le Caissier"&gt;
///         &lt;!--badges--&gt;
///             ... pastilles Alpine libres, en haut à droite ...
///         &lt;!--/badges--&gt;
///
///         ... corps du document : cartouche, tableau ou déclaration+échéancier ...
///
///         &lt;!--footer--&gt;
///             ... mention de bas de page, texte ou liaison Alpine (x-text sur un &lt;span&gt; interne) ...
///         &lt;!--/footer--&gt;
///     &lt;/receipt-a5&gt;
/// &lt;/template&gt;
/// </code>
/// </summary>
[HtmlTargetElement("receipt-a5")]
public class ReceiptA5TagHelper : TagHelper
{
    /// <summary>
    /// Racine Alpine du document (propriété portant schoolLogoUrl/schoolName…). Toujours « receipt »
    /// à ce jour sur les deux écrans ; paramétrable si un troisième document A5 apparaît un jour avec
    /// un autre nom d'état.
    /// </summary>
    public string Receipt { get; set; } = "receipt";

    /// <summary>
    /// Active la pastille « N° … » centrée dans l'en-tête (marqueur <c>center-badge</c>) et
    /// l'espacement associé — seul le reçu d'inscription l'utilise. Cette pastille et le padding qui
    /// l'accompagne (aération de l'en-tête, cf. Enrollments) sont un choix visuel PROPRE à ce
    /// document ; ne pas l'activer sur /caisse changerait un rendu déjà validé sans que rien ne le
    /// demande.
    /// </summary>
    public bool CenterBadge { get; set; }

    /// <summary>Libellé du second signataire du pied de page (« Le Caissier », « Le Secrétariat »…).</summary>
    public string Signatory { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var raw = (await output.GetChildContentAsync()).GetContent();

        var (badges, afterBadges) = ExtractMarked(raw, "badges");
        var (centerBadge, afterCenterBadge) = ExtractMarked(afterBadges, "center-badge");
        var (footerMention, body) = ExtractMarked(afterCenterBadge, "footer");

        // Aération de l'en-tête réservée au reçu d'inscription (cf. XML doc de CenterBadge) : reproduite
        // ici à l'identique de ce qu'était Views/Enrollments/Index.cshtml AVANT cette extraction, pour
        // que son rendu ne bouge pas d'un pixel (règle #12 — reproduction fidèle du design-reference).
        var headerPadding = CenterBadge ? "pb-2.5 pt-1.5" : "pb-3";
        var headerRelative = CenterBadge ? " relative" : "";
        var namePadding = CenterBadge ? " pr-24" : "";
        var signatory = WebUtility.HtmlEncode(Signatory);

        output.TagName = null;
        output.Content.SetHtmlContent($"""
            <div id="receipt-printable" class="receipt-a5 receipt-container bg-white border border-slate-200/80 mx-auto text-slate-800 shadow-sm rounded-xl flex flex-col justify-between min-h-[148mm]">

                <div class="receipt-header{headerRelative} flex items-start gap-4 border-b border-slate-200 {headerPadding}">
                    <img x-show="{Receipt}.schoolLogoUrl" x-cloak :src="{Receipt}.schoolLogoUrl" class="h-10 w-10 shrink-0 object-contain" alt="" />
                    <div class="min-w-0 flex-1{namePadding}">
                        <h2 class="text-lg font-bold text-slate-900 tracking-tight" x-text="{Receipt}.schoolName"></h2>
                        <p class="text-[10px] text-slate-500 mt-0.5" x-text="contactLine()"></p>
                        <p x-show="legalMentions()" x-cloak class="text-[10px] text-slate-500" x-text="legalMentions()"></p>
                    </div>
                    {centerBadge}
                    <div class="shrink-0 flex flex-col items-end gap-1">
                        {badges}
                    </div>
                </div>

                {body}

                <!-- Pied de page, ancré en bas (flex-col + justify-between du conteneur) -->
                <div class="receipt-footer mt-auto pt-2 border-t border-slate-200">
                    <div class="flex items-end gap-6 text-[10px]">
                        <div class="flex-1">
                            <p class="italic text-slate-500" x-text="faitMention()"></p>
                            <p class="mt-2.5 text-[9px] font-semibold uppercase tracking-wider text-slate-400">Cachet officiel</p>
                        </div>
                        <div class="flex-1 text-right">
                            <div class="h-6"></div>
                            <div class="border-t border-dashed border-slate-400 pt-1 text-[9px] font-semibold uppercase tracking-wider text-slate-500">{signatory}</div>
                        </div>
                        <div class="flex-1 text-right">
                            <div class="h-6"></div>
                            <div class="border-t border-dashed border-slate-400 pt-1 text-[9px] font-semibold uppercase tracking-wider text-slate-500">Le Directeur</div>
                        </div>
                    </div>
                    <p class="mt-2 text-center text-[8px] leading-tight text-slate-400">{footerMention}</p>
                </div>

            </div>
            """);
    }

    /// <summary>
    /// Retire et renvoie le contenu compris entre <c>&lt;!--{marker}--&gt;</c> et
    /// <c>&lt;!--/{marker}--&gt;</c>, ainsi que le reste de <paramref name="html"/> une fois ce
    /// segment (marqueurs compris) ôté. Absent → contenu vide, reste inchangé : chaque section est
    /// facultative (seul <c>center-badge</c> l'est vraiment à ce jour, mais aucune des trois ne doit
    /// être required pour rester robuste à un futur troisième document A5 qui n'en aurait pas besoin).
    /// </summary>
    private static (string Content, string Remainder) ExtractMarked(string html, string marker)
    {
        var open = $"<!--{marker}-->";
        var close = $"<!--/{marker}-->";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        if (start < 0) return ("", html);

        var contentStart = start + open.Length;
        var end = html.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (end < 0) return ("", html);

        var content = html[contentStart..end];
        var remainder = string.Concat(html.AsSpan(0, start), html.AsSpan(end + close.Length));
        return (content, remainder);
    }
}
````


## A.4 Documents PDF (QuestPDF)

### A.4.1 Utilitaires PDF partagés (thème des reçus, largeurs de colonnes, anti-coupure, garde de rendu, en-tête officiel)

#### `src/SamaEcole.Infrastructure/Documents/ReceiptTheme.cs`

````csharp
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Palette et blocs communs des pièces A5 remises en main propre — le reçu de caisse
/// (<see cref="PaymentReceiptDocument"/>), l'attestation d'inscription
/// (<see cref="EnrollmentReceiptDocument"/>) et la décharge de matériel
/// (<see cref="DischargeNoteDocument"/>, module Inventaire). Refonte validée le 25/08/2026 : les
/// documents ne sont plus en noir et blanc strict, ils portent une couleur SÉMANTIQUE et rien d'autre
/// (docs/design-references/README.md §1 et §1bis, mis à jour en conséquence).
///
/// Trois familles, jamais mélangées :
///   * neutres — encre, filets, fonds de tableau ;
///   * <see cref="Paid"/> (vert) — EXCLUSIVEMENT ce qui est acquitté. Un montant dû ne porte
///     jamais cette couleur : sur l'attestation, le vert dirait « payé » alors que le secrétariat
///     n'encaisse rien ;
///   * <see cref="Plan"/> (indigo) — le prospectif : tarifs mensuels à venir, état administratif.
///
/// Source de vérité unique des valeurs : régler une couleur « à l'œil » dans un document isolé
/// réintroduit la dérive que ce fichier supprime (même principe que <see cref="PdfColumnWidths"/>).
///
/// POLICE : la maquette HTML est composée en Inter, absente du pipeline PDF — aucune police n'est
/// enregistrée auprès de QuestPDF. Les documents utilisent donc sa police par défaut (Lato), une
/// grotesque humaniste de proportions voisines. Passer réellement à Inter suppose d'embarquer le
/// fichier de police et de l'enregistrer au démarrage ; ce n'est pas fait ici.
/// </summary>
internal static class ReceiptTheme
{
    // ----------------------------------------------------------------- neutres --
    public static readonly Color Ink        = Color.FromHex("#111827");
    public static readonly Color InkSoft    = Color.FromHex("#374151");
    public static readonly Color Muted      = Color.FromHex("#6B7280");
    public static readonly Color Faint      = Color.FromHex("#9CA3AF");
    public static readonly Color Rule       = Color.FromHex("#E5E7EB");
    public static readonly Color RuleStrong = Color.FromHex("#D1D5DB");
    public static readonly Color HeadFill   = Color.FromHex("#F3F4F6");
    public static readonly Color PanelFill  = Color.FromHex("#F9FAFB");

    // ------------------------------------------------- acquitté (vert) — encaissé --
    public static readonly Color Paid     = Color.FromHex("#1E8E3E");
    public static readonly Color PaidFill = Color.FromHex("#EAF7EE");
    public static readonly Color PaidRule = Color.FromHex("#BFE3CB");

    // ------------------------------------------ prospectif (indigo) — à venir / état --
    public static readonly Color Plan       = Color.FromHex("#4338CA");
    public static readonly Color PlanAccent = Color.FromHex("#6366F1");
    public static readonly Color PlanFill   = Color.FromHex("#EEF2FF");
    public static readonly Color PlanRule   = Color.FromHex("#C7D2FE");

    /// <summary>Style d'un badge d'en-tête. <see cref="Paid"/> est réservé à un encaissement effectif.</summary>
    public enum BadgeStyle { Neutral, Paid, Info }

    /// <summary>
    /// En-tête commun : logo optionnel, identité de l'établissement à gauche, badges empilés à droite,
    /// filet de séparation. Chaque mention absente est simplement OMISE — jamais de séparateur
    /// orphelin ni de valeur inventée (convention posée par la référence de design §1.1).
    /// </summary>
    public static void ComposeHeader(
        ColumnDescriptor column,
        string schoolName,
        string? address,
        string? phone,
        string? email,
        string? ninea,
        string? registreCommerce,
        byte[]? logo,
        params (string Text, BadgeStyle Style)[] badges)
    {
        column.Item().BorderBottom(0.75f).BorderColor(Rule).PaddingBottom(6).Row(row =>
        {
            // Logo à GAUCHE du nom, discret : l'angle droit est occupé par les badges. Absent, on
            // n'imprime aucun cadre témoin — un en-tête propre vaut mieux qu'un trou légendé.
            if (logo is not null)
            {
                row.ConstantItem(30).MaxHeight(30).AlignMiddle().Image(logo).FitArea();
                row.ConstantItem(10);
            }

            row.RelativeItem().Column(identity =>
            {
                identity.Item().Text(schoolName).Bold().FontSize(12.5f).FontColor(Ink);

                var contact = JoinPresent(address, phone, email);
                if (contact.Length > 0)
                {
                    identity.Item().PaddingTop(2).Text(contact).FontSize(6.5f).FontColor(Muted);
                }

                var legal = JoinPresent(
                    ninea is null ? null : $"NINEA {ninea}",
                    registreCommerce is null ? null : $"RCCM {registreCommerce}");
                if (legal.Length > 0)
                {
                    identity.Item().Text(legal).FontSize(6.5f).FontColor(Muted);
                }
            });

            row.ConstantItem(12);
            row.ConstantItem(120).Column(stack =>
            {
                foreach (var (text, style) in badges)
                {
                    stack.Item().PaddingBottom(2.5f).AlignRight().Element(c => Badge(c, text, style));
                }
            });
        });
    }

    /// <summary>
    /// Pastille d'en-tête. Coins droits : <c>CornerRadius</c> n'existe pas dans QuestPDF 2024.10.3,
    /// contrairement à la maquette HTML — seul écart assumé vis-à-vis d'elle.
    /// </summary>
    private static void Badge(IContainer container, string text, BadgeStyle style)
    {
        var (fill, border, ink) = style switch
        {
            BadgeStyle.Paid => (PaidFill, PaidRule, Paid),
            BadgeStyle.Info => (PlanFill, PlanRule, Plan),
            _ => (HeadFill, Rule, InkSoft)
        };

        container
            .Background(fill).Border(0.5f).BorderColor(border)
            .PaddingVertical(2).PaddingHorizontal(5)
            .Text(text.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(ink).LetterSpacing(0.06f);
    }

    /// <summary>
    /// Cartouche d'identité : panneau gris clair, grille de 2 colonnes, étiquette capitale au-dessus
    /// de sa valeur. Les cellules sont fournies dans l'ordre de lecture (gauche, droite, gauche…).
    /// </summary>
    public static void ComposeCartouche(IContainer container, params (string Label, string Value)[] cells)
    {
        container.Background(PanelFill).Border(0.5f).BorderColor(Rule).Padding(5).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            foreach (var (label, value) in cells)
            {
                table.Cell().PaddingVertical(1.5f).PaddingRight(10).Column(cell =>
                {
                    cell.Item().Text(label.ToUpperInvariant())
                        .Bold().FontSize(6).FontColor(Muted).LetterSpacing(0.08f);
                    cell.Item().Text(value).SemiBold().FontSize(8).FontColor(Ink);
                });
            }
        });
    }

    /// <summary>Intitulé de section : libellé capitale, précision facultative en gris clair.</summary>
    public static void SectionLabel(IContainer container, string label, string? hint = null)
    {
        container.Text(text =>
        {
            text.Span(label.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(Muted).LetterSpacing(0.1f);

            if (!string.IsNullOrWhiteSpace(hint))
            {
                text.Span($"   {hint}").FontSize(6.5f).FontColor(Faint);
            }
        });
    }

    /// <summary>
    /// Pied de page : « Fait à … » et cachet à gauche, puis deux traits de signature. Les rôles
    /// diffèrent d'une pièce à l'autre — le secrétariat délivre l'attestation, la caisse encaisse.
    /// </summary>
    public static void ComposeSignatures(IContainer container, string faitA, string leftRole, string rightRole)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(faitA).Italic().FontSize(7).FontColor(Muted);
                left.Item().PaddingTop(10).Text("Cachet officiel")
                    .Bold().FontSize(6.5f).FontColor(Faint).LetterSpacing(0.06f);
            });

            row.ConstantItem(14);
            row.RelativeItem().Element(c => SignatureLine(c, leftRole));
            row.ConstantItem(14);
            row.RelativeItem().Element(c => SignatureLine(c, rightRole));
        });
    }

    /// <summary>
    /// Trait de signature. Trait CONTINU : QuestPDF ne sait pas tracer un filet pointillé, là où la
    /// maquette HTML en pose un. Sans incidence à l'usage — la signature se pose par-dessus.
    /// </summary>
    private static void SignatureLine(IContainer container, string role)
    {
        container.Column(column =>
        {
            column.Item().Height(12);
            column.Item().BorderTop(0.5f).BorderColor(Faint).PaddingTop(2)
                .Text(role.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(Muted).LetterSpacing(0.06f);
        });
    }

    /// <summary>Note de bas de page : la plus petite ligne du document, jamais une mention obligatoire.</summary>
    public static void Footnote(IContainer container, string text) =>
        container.AlignCenter().Text(text).FontSize(5.8f).FontColor(Faint);

    /// <summary>Concatène les mentions RENSEIGNÉES, séparées par un point médian. Vide si tout manque.</summary>
    public static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
````


#### `src/SamaEcole.Infrastructure/Documents/PdfColumnWidths.cs`

````csharp
namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Largeurs (en points PDF) des colonnes de tableau dont le CONTENU a un gabarit fixe : un matricule,
/// une date, un numéro de téléphone ont toujours la même longueur, d'un élève à l'autre et d'un
/// document à l'autre. Une <c>RelativeColumn</c> les fait déborder dès que le tableau se resserre
/// (colonne voisine plus large, page portrait au lieu de paysage) : le dernier caractère repasse à la
/// ligne et la ligne du tableau double de hauteur. <c>NoBreakText</c> ne corrige pas ce cas — il
/// neutralise la coupure sur le tiret, pas le repli d'un texte plus large que sa colonne.
///
/// Ces constantes sont la SEULE source de vérité : un même matricule doit occuper la même largeur sur
/// la liste des élèves, la liste des enseignants, le PV de délibération et le journal de caisse.
/// Régler une largeur « à l'œil » dans un document isolé, c'est réintroduire la dérive que ce fichier
/// supprime. Valeurs calibrées au rendu (2026-08-04) sur la police par défaut de QuestPDF.
/// </summary>
internal static class PdfColumnWidths
{
    /// <summary>
    /// Identifiant métier : matricule (« ELEV‑2025‑0001 », « ENS‑2025‑0007 »), numéro de reçu
    /// (« REC‑2025‑0002 »), référence de PV. Calibrée sur 14 caractères à 8 pt.
    /// </summary>
    public const float Identifier = 78f;

    /// <summary>
    /// Date au format « jj/mm/aaaa » (naissance, embauche, paiement, échéance). Sans largeur fixe,
    /// c'est l'ANNÉE qui saute à la ligne — le défaut le plus visible sur une liste imprimée.
    /// </summary>
    public const float Date = 62f;

    /// <summary>
    /// Date suivie d'une heure (« 04/08/2026 14:30 ») — journaux, historiques, pointage.
    /// </summary>
    public const float DateTime = 88f;

    /// <summary>Heure seule (« 14:30 »).</summary>
    public const float Time = 38f;

    /// <summary>
    /// Téléphone mis en forme par <c>PhoneFormatter.FormatSenegal</c>. Dimensionnée sur la forme
    /// LONGUE avec indicatif (« +221 77 000 00 00 »), pas sur « 77 000 00 00 » : une école qui saisit
    /// l'indicatif verrait sinon la fin du numéro passer à la ligne. Minimum absolu — ne pas réduire.
    /// </summary>
    public const float Phone = 88f;

    /// <summary>Colonne à un seul caractère ou à sigle court (« M »/« F ») : l'en-tête fait la largeur.</summary>
    public const float Initial = 36f;

    /// <summary>Montant en francs CFA aligné à droite (« 1 250 000 »).</summary>
    public const float Amount = 72f;
}
````


#### `src/SamaEcole.Infrastructure/Documents/NoBreakText.cs`

````csharp
namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Le tiret d'un identifiant métier (matricule "ELEV-2025-0005", reçu "REC-2025-0002", PV "PV-3F2A9C1D"…)
/// est un point de coupure de ligne légitime pour le moteur de rendu de texte de QuestPDF : sans
/// intervention, l'identifiant qui tombe en fin de ligne (surtout dans une colonne de tableau étroite)
/// peut se scinder sur deux lignes. Remplacer le tiret standard par un tiret insécable (U+2011) neutralise
/// cette coupure — l'identifiant reste toujours sur une seule ligne, quelle que soit la largeur disponible.
/// Utilisé partout où un tel identifiant s'imprime, pas seulement pour le matricule élève d'origine.
/// </summary>
internal static class NoBreakText
{
    /// <summary>
    /// <paramref name="value"/> null ou vide ressort en chaîne vide : un identifiant manquant ne doit
    /// jamais faire échouer QuestPDF (NRE dans <c>Compose</c>) et donc l'émission de tout le document.
    /// </summary>
    public static string NoBreak(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace('-', '‑');
}
````


#### `src/SamaEcole.Infrastructure/Documents/PdfRenderGuard.cs`

````csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Filet UNIQUE de tous les générateurs PDF QuestPDF. Garantit qu'un générateur ne renvoie JAMAIS
/// <c>null</c> ni un tableau de 0 octet : soit un PDF valide, soit une exception explicite et
/// journalisée. Le middleware d'exception en fait alors un 500 normalisé — jamais un 200 à corps vide
/// qui bloquerait l'aperçu client sur « Le document généré par le serveur est vide (0 octet) » sans
/// laisser la moindre trace serveur.
///
/// POURQUOI CE HELPER
/// ------------------
/// Trois générateurs (reçu d'inscription, reçu de paiement, export élèves) codaient déjà cette garde,
/// chacun à sa façon. Une quinzaine d'autres se contentaient d'un <c>catch (Exception) when (logo is
/// not null)</c> qui retente sans logo — mais la relance n'était pas gardée et rien ne vérifiait
/// <c>Length == 0</c>. Une dizaine ne gardaient rien du tout. Résultat : selon le document cliqué, un
/// échec de rendu pouvait produire un 500 propre, un 500 opaque, ou (via un chemin QuestPDF) un
/// tableau vide silencieux. Ce helper impose le MÊME comportement partout.
///
/// DEUX PASSES
/// ----------
///  1. <paramref name="render"/> — rendu complet (logo d'établissement, cachet, signature…).
///  2. <paramref name="renderPlain"/> (optionnel) — même document dépouillé des ornements chargés
///     dynamiquement : un logo illisible ne doit JAMAIS empêcher l'émission d'une pièce officielle.
///     Les documents sans ornement passent <c>null</c>.
///
/// Un rendu qui LÈVE **ou** qui renvoie 0 octet est traité de façon identique : on tente la passe
/// dépouillée si elle existe, sinon on lève <see cref="InvalidOperationException"/> en citant le
/// document et ses identifiants (log <c>Error</c>, exploitable en production).
/// </summary>
public static class PdfRenderGuard
{
    /// <param name="logger">
    /// Journal du générateur appelant (contexte : nom de la classe génératrice). <c>null</c> accepté
    /// (tests unitaires qui instancient le générateur sans conteneur) : le helper retombe alors sur
    /// <see cref="NullLogger"/>.
    /// </param>
    /// <param name="documentLabel">
    /// Libellé humain du document AVEC ses identifiants, p. ex.
    /// <c>$"reçu de paiement {receipt.ReceiptNumber} (matricule {receipt.Matricule})"</c>.
    /// Il apparaît tel quel dans les logs et dans le message d'erreur : le mettre parlant.
    /// </param>
    /// <param name="render">Rendu complet. Peut lever ou (pathologiquement) renvoyer un tableau vide.</param>
    /// <param name="renderPlain">Rendu dépouillé du logo/cachet/signature, ou <c>null</c> si sans objet.</param>
    public static byte[] Render(
        ILogger? logger,
        string documentLabel,
        Func<byte[]?> render,
        Func<byte[]?>? renderPlain = null)
    {
        logger ??= NullLogger.Instance;

        byte[]? pdf = null;
        Exception? failure = null;

        try
        {
            pdf = render();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if ((pdf is null || pdf.Length == 0) && renderPlain is not null)
        {
            if (failure is not null)
            {
                logger.LogWarning(
                    failure,
                    "PDF « {DocumentLabel} » : échec du rendu complet. Nouvelle tentative sans logo/cachet/signature.",
                    documentLabel);
            }
            else
            {
                logger.LogWarning(
                    "PDF « {DocumentLabel} » : le rendu complet a produit 0 octet. Nouvelle tentative sans logo/cachet/signature.",
                    documentLabel);
            }

            failure = null;
            try
            {
                pdf = renderPlain();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        if (pdf is { Length: > 0 })
        {
            return pdf;
        }

        var message = $"La génération du PDF « {documentLabel} » a produit 0 octet — aucun document ne sera renvoyé.";
        logger.LogError(failure, "{Message}", message);

        throw failure is not null
            ? new InvalidOperationException(message, failure)
            : new InvalidOperationException(message);
    }
}
````


#### `src/SamaEcole.Infrastructure/Documents/Components/OfficialHeaderComponent.cs`

````csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Infrastructure.Documents;

namespace SamaEcole.Infrastructure.Documents.Components;

/// <summary>
/// Bandeau République/devise/Ministère + ligne IA/IEF/établissement, partagés par tout nouveau
/// document officiel destiné à l'élève ou à sa famille (Exéat, PV de discipline, Sommation…), sur le
/// même modèle que <see cref="EnrollmentCertificateDocument"/>. Les 12 documents antérieurs à ce
/// composant gardent leur propre méthode privée (pattern historique, non touché ici) : ce composant
/// n'est utilisé que par les documents introduits après lui, pour ne pas produire de diff hors
/// périmètre sur des fichiers déjà validés.
/// </summary>
public static class OfficialHeaderComponent
{
    /// <summary>République du Sénégal / devise / Ministère — bandeau centré, identique sur tout document officiel.</summary>
    public static void ComposeMinistryBanner(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(12);
            column.Item().AlignCenter().Text("Un Peuple - Un But - Une Foi").Italic().FontSize(9);
            column.Item().PaddingTop(6).AlignCenter().Text("MINISTÈRE DE L'ÉDUCATION NATIONALE").Bold().FontSize(11);
        });
    }

    /// <summary>
    /// IA / IEF / établissement — libellé en gras, valeur en normal, ligne vide si non renseignée
    /// plutôt qu'une valeur inventée (AGENTS.md : ne jamais fabriquer une donnée absente).
    /// </summary>
    public static void ComposeEstablishmentBlock(
        IContainer container,
        string? inspectionAcademie,
        string? inspectionEducationFormation,
        string headingPrefix,
        string? headingName,
        byte[]? logo)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem(3).Element(left =>
                ComposeEstablishmentLines(left, inspectionAcademie, inspectionEducationFormation, headingPrefix, headingName));

            if (logo is not null)
            {
                row.ConstantItem(55).MaxHeight(40).AlignRight().Image(logo).FitArea();
            }
        });

    }

    /// <summary>
    /// Les trois lignes IA / IEF / établissement seules (sans filet ni logo), pour les documents qui composent leur
    /// propre bandeau — le cahier de roulement y ajoute les lignes arabes (Franco-Arabe, Bloc 5c).
    /// </summary>
    public static void ComposeEstablishmentLines(
        IContainer container,
        string? inspectionAcademie,
        string? inspectionEducationFormation,
        string headingPrefix,
        string? headingName)
    {
        container.Column(column =>
        {
            column.Item().Element(c => HeaderLine(c, "Inspection d'Académie de", inspectionAcademie));
            column.Item().Element(c => HeaderLine(c, "Inspection de l'Éducation et de la Formation de", inspectionEducationFormation));
            column.Item().Element(c => HeaderLine(c, headingPrefix, headingName));
        });
    }

    private static void HeaderLine(IContainer container, string label, string? value) =>
        container.Text(text =>
        {
            text.Span($"{label} : ").Bold().FontSize(9.5f);
            text.Span(value ?? "").FontSize(9.5f);
        });

    /// <summary>
    /// Bloc de pied de document anti-fraude commun (AGENTS.md §2.5 du cahier des charges Documents) :
    /// QR code de vérification à gauche, numéro de référence et mention de provenance à droite.
    /// </summary>
    public static void ComposeAuthenticityFooter(IContainer container, byte[] qrCodeImage, string referenceNumber)
    {
        container.Row(row =>
        {
            row.ConstantItem(48).Height(48).Image(qrCodeImage).FitArea();
            row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(column =>
            {
                column.Item().Text($"Référence : {NoBreakText.NoBreak(referenceNumber)}").FontSize(7).FontColor(Colors.Grey.Darken2);
                column.Item().Text("Document généré par Unikol — vérifiable par le QR code ci-contre")
                    .FontSize(7).FontColor(Colors.Grey.Darken2);
            });
        });
    }
}
````


### A.4.2 Reçu de paiement et reçu d'inscription

#### `src/SamaEcole.Infrastructure/Documents/PaymentReceiptDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu de CAISSE officiel en PDF (ticket JGK-F02) — justificatif comptable immédiat. C'est la SEULE
/// pièce qui atteste d'un encaissement : l'attestation d'inscription
/// (<see cref="EnrollmentReceiptDocument"/>) annonce ce qu'il y a à régler, ce document constate ce qui
/// est entré en caisse. Aucun recouvrement entre les deux.
///
/// Refonte validée le 25/08/2026 (maquette « Pièces de caisse repensées », docs/design-references/
/// README.md §1bis) : A5 paysage à marge de 10 mm, en-tête à badges, cartouche d'identité, et surtout
/// une VENTILATION du versement — une ligne par poste réglé, au lieu de l'unique « Versement reçu »
/// d'avant. Palette et blocs communs dans <see cref="ReceiptTheme"/>.
///
/// Ce que le document n'imprime toujours PAS, et ne doit jamais imprimer : le dû annuel, le déjà-réglé
/// et le solde (<see cref="PaymentReceiptDto.TotalDue"/>, <see cref="PaymentReceiptDto.AlreadyPaid"/>,
/// <see cref="PaymentReceiptDto.RemainingBalance"/>). Ces champs restent au DTO pour la Finance ; un
/// reçu atteste d'un encaissement, pas d'une dette (règle comptable non négociable, AGENTS.md règle #12).
///
/// La mention obligatoire est une CONSTANTE : aucun appelant ne peut l'altérer ni l'omettre.
/// </summary>
public class PaymentReceiptDocument(PaymentReceiptDto receipt, byte[]? logo) : IDocument
{
    private const string MandatoryMention =
        "Il est demandé aux parents de garder minutieusement leur reçu après le paiement.";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Reçu de caisse {receipt.ReceiptNumber}",
        Author = receipt.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            // A5 paysage, marge 10 mm : zone utile de 190 × 128 mm, identique à la maquette validée.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(ReceiptTheme.Ink).DisableFontFeature(FontFeatures.StandardLigatures));

            // Reçu annulé : filigrane en travers de la page, en plus du bandeau ci-dessous — jamais pris pour un reçu valable.
            if (!string.IsNullOrWhiteSpace(receipt.CancellationNotice))
            {
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-25)
                    .Text("ANNULÉ").FontSize(90).Bold().FontColor(Colors.Red.Lighten3);
            }

            page.Content().Column(column =>
            {
                if (!string.IsNullOrWhiteSpace(receipt.CancellationNotice))
                {
                    column.Item().PaddingBottom(4).Border(1).BorderColor(Colors.Red.Darken2).Padding(3)
                        .Text(receipt.CancellationNotice).Bold().FontSize(8).FontColor(Colors.Red.Darken2);
                }

                ReceiptTheme.ComposeHeader(
                    column,
                    receipt.SchoolName,
                    receipt.SchoolAddress,
                    PhoneFormatter.FormatSenegal(receipt.SchoolPhone),
                    receipt.SchoolEmail,
                    receipt.SchoolNinea,
                    receipt.SchoolRegistreCommerce,
                    logo,
                    ("Payé", ReceiptTheme.BadgeStyle.Paid),
                    ($"Reçu n° {receipt.ReceiptNumber}", ReceiptTheme.BadgeStyle.Neutral),
                    (FormatDate(receipt.PaidAt), ReceiptTheme.BadgeStyle.Neutral));

                column.Item().PaddingTop(6).Element(ComposeCartouche);

                column.Item().PaddingTop(8).Element(c => ReceiptTheme.SectionLabel(
                    c, "Détail du règlement", PeriodHint()));

                column.Item().PaddingTop(4).Element(ComposeAmountsTable);

                column.Item().PaddingTop(8).AlignCenter()
                    .Text(MandatoryMention).Italic().FontSize(7).FontColor(ReceiptTheme.InkSoft);

                // Pied ancré en bas de la feuille, quel que soit le nombre de lignes ventilées.
                column.Item().PaddingTop(8).BorderTop(0.75f).BorderColor(ReceiptTheme.Rule).PaddingTop(7)
                    .Element(c => ReceiptTheme.ComposeSignatures(c, FaitA(), "Le Caissier", "Le Directeur"));

                column.Item().PaddingTop(6).Element(c => ReceiptTheme.Footnote(c,
                    $"Ce reçu atteste uniquement des sommes encaissées le {FormatDate(receipt.PaidAt)}. Il ne constitue pas un relevé de compte."));
            });
        });
    }

    private void ComposeCartouche(IContainer container) =>
        ReceiptTheme.ComposeCartouche(container,
            ("Élève", receipt.StudentFullName),
            ("Matricule", NoBreakText.NoBreak(receipt.Matricule)),
            ("Classe & année",
                $"{ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)} — {receipt.SchoolYearLabel}"),
            ("Mode de règlement", MethodLabel(receipt.Method)));

    /// <summary>
    /// Précision du bandeau de section : la période couverte par le versement entier, quand elle est
    /// renseignée. Rappelée ici même si chaque ligne porte son propre libellé — le tuteur doit pouvoir
    /// rattacher le versement à un mois sans lire le tableau ligne à ligne.
    /// </summary>
    private string? PeriodHint() =>
        string.IsNullOrWhiteSpace(receipt.ReferencePeriod)
            ? null
            : $"Période de référence : {receipt.ReferencePeriod}";

    /// <summary>
    /// Tableau du règlement. Deux formes, choisies par <see cref="PaymentReceiptDto.HasBalancedLines"/> :
    ///
    ///   * VENTILÉE — une ligne par poste imputé, avec sa période ou sa note. C'est la forme normale
    ///     depuis la refonte : le tuteur voit à quoi servent les francs qu'il vient de remettre.
    ///   * UNIQUE — la ligne « Versement reçu » d'origine, servie quand la caisse n'a rien ventilé,
    ///     ou quand la ventilation saisie ne totalise pas le montant encaissé. Un tableau dont le
    ///     détail contredit le total est un faux : mieux vaut moins de détail qu'un document faux.
    /// </summary>
    private void ComposeAmountsTable(IContainer container)
    {
        if (receipt.HasBalancedLines)
        {
            ComposeVentilatedTable(container);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(PdfColumnWidths.Amount);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Désignation").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
            });

            table.Cell().Element(BodyCell).Text("Versement reçu");
            table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.Amount));

            table.Cell().Element(TotalCell).Text("TOTAL VERSÉ").Bold().FontSize(7.5f)
                .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
            table.Cell().Element(TotalCell).AlignRight()
                .Text($"{FormatMoney(receipt.Amount)} FCFA").Bold().FontSize(10.5f).FontColor(ReceiptTheme.Paid);
        });
    }

    private void ComposeVentilatedTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(46);                     // Désignation du service
                columns.RelativeColumn(32);                     // Période / Note
                columns.ConstantColumn(PdfColumnWidths.Amount); // Montant — largeur fixe, jamais de repli
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Désignation / Service").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).Text("Période / Note").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant réglé").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
            });

            foreach (var line in receipt.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.Designation);

                // Libellé de ligne, à défaut période du versement, à défaut CELLULE VIDE — la cascade
                // est résolue par le DTO (Application), le document ne fait que la mettre en page.
                var label = receipt.ResolveLineLabel(line);
                table.Cell().Element(BodyCell).Text(label ?? string.Empty)
                    .FontSize(7.5f).FontColor(ReceiptTheme.Muted);

                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.Amount));
            }

            table.Cell().Element(TotalCell).Text("TOTAL VERSÉ").Bold().FontSize(7.5f)
                .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
            table.Cell().Element(TotalCell);
            table.Cell().Element(TotalCell).AlignRight()
                .Text($"{FormatMoney(receipt.Amount)} FCFA").Bold().FontSize(10.5f).FontColor(ReceiptTheme.Paid);
        });
    }

    // Filets HORIZONTAUX seuls (maquette validée) : un tableau à grille pleine alourdit une pièce
    // qui ne compte que cinq lignes.
    private static IContainer HeaderCell(IContainer c) =>
        c.Background(ReceiptTheme.HeadFill).BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
         .PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule).PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer TotalCell(IContainer c) =>
        c.BorderTop(1f).BorderColor(ReceiptTheme.RuleStrong).PaddingTop(4).PaddingBottom(1).PaddingHorizontal(6);

    private string FaitA()
    {
        var date = FormatDate(receipt.PaidAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    private static string MethodLabel(string method) => method switch
    {
        "Cash" => "Espèces",
        "Cheque" => "Chèque",
        "Transfer" => "Virement",
        "MobileMoney" => "Mobile Money (Wave / Orange Money)",
        _ => method
    };

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
````


#### `src/SamaEcole.Infrastructure/Documents/PaymentReceiptPdfGenerator.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IPaymentReceiptPdfGenerator"/> (ticket JGK-F02). Licence
/// Community posée une fois dans le constructeur statique.
///
/// Le rendu passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission
/// d'un reçu OFFICIEL (nouvelle passe sans logo), et un rendu vide LÈVE une exception journalisée —
/// jamais un tableau de 0 octet qui partirait en 200 muet et bloquerait l'aperçu client sur
/// « document vide (0 octet) » sans laisser de trace.
/// </summary>
public class PaymentReceiptPdfGenerator(ILogger<PaymentReceiptPdfGenerator> logger) : IPaymentReceiptPdfGenerator
{
    static PaymentReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentReceiptDto receipt, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"reçu de paiement {receipt.ReceiptNumber} (matricule {receipt.Matricule}, école {receipt.SchoolName})",
            () => new PaymentReceiptDocument(receipt, logo).GeneratePdf(),
            logo is not null ? () => new PaymentReceiptDocument(receipt, null).GeneratePdf() : null);
}
````


#### `src/SamaEcole.Infrastructure/Documents/EnrollmentReceiptDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Attestation d'inscription &amp; d'admission officielle en PDF (ticket JGK-E02). Pièce ADMINISTRATIVE
/// délivrée par le SECRÉTARIAT, qui n'encaisse aucun fonds : elle atteste d'une inscription et annonce
/// ce qu'il y a à régler auprès de la comptabilité. Le constat d'encaissement, lui, appartient
/// exclusivement au reçu de caisse (<see cref="PaymentReceiptDocument"/>), seule pièce comptable.
///
/// Refonte validée le 25/08/2026 (maquette « Pièces de caisse repensées », docs/design-references/
/// README.md §1). Ce qui a changé, et pourquoi :
///
///   * Le tableau du CUMUL ANNUEL a disparu. « Mensualité (× 9 mois) = 135 000 », puis un total de
///     271 000, effrayaient le tuteur sans l'informer. Deux blocs le remplacent :
///       — « Règlement à effectuer » : l'engagement initial
///         (<see cref="EnrollmentReceiptDto.InitialSettlementTotal"/>), soit le frais ponctuel entier
///         ou UNE mensualité par ligne récurrente ;
///       — « Échéancier mensuel » : les tarifs mensuels UNITAIRES et leur total
///         (<see cref="EnrollmentReceiptDto.MonthlyTotal"/>), jamais multipliés.
///   * <c>TotalDue</c> et <c>RemainingBalance</c> ne sont PLUS imprimés. Ils restent au DTO pour la
///     Finance ; le tuteur, lui, ne lit plus le cumul annuel sur son papier.
///   * <c>CollectedLines</c> / <c>TotalCollected</c> ne sont plus lus du tout : le secrétariat
///     n'encaisse pas, ils valent zéro à l'instant où ce document est délivré.
///
/// COULEUR : aucune touche de vert nulle part. Le vert signifie « acquitté » dans
/// <see cref="ReceiptTheme"/>, et cette pièce n'acquitte rien. Le total à régler reste à l'encre, et
/// seul l'échéancier — prospectif — porte l'indigo.
///
/// Aucun badge d'état de paiement non plus : réimprimée trois mois plus tard, l'attestation
/// afficherait un état faux. Le badge ne qualifie que l'acte administratif, vrai à toute date.
/// </summary>
public class EnrollmentReceiptDocument(EnrollmentReceiptDto receipt, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Attestation d'inscription — {ReceiptReference()}",
        Author = receipt.SchoolName
    };

    /// <summary>
    /// Référence affichée sur le badge « N° … » et dans le titre du document. Sur une inscription
    /// « envoyée en Caisse » (paiement différé), AUCUN numéro de reçu officiel n'a été consommé
    /// (AGENTS.md règle #3 — le registre gapless est réservé à un mouvement d'argent réel) : la
    /// colonne porte un jeton provisoire <c>EN-ATTENTE-{guid}</c>. On l'affiche alors
    /// « en attente de règlement », comme l'écran (<c>receiptReference()</c> dans enrollments.js) ;
    /// le numéro officiel apparaît au premier encaissement, à la Caisse.
    /// </summary>
    private string ReceiptReference()
    {
        // Sans numéro du tout, aucun reçu officiel n'a été émis non plus : même libellé que le jeton
        // provisoire, plutôt qu'un badge « N° » vide.
        if (receipt.ReceiptNumber is not { } number
            || number.StartsWith("EN-ATTENTE-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(receipt.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase))
        {
            return "en attente de règlement";
        }

        return number;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            // A5 paysage, marge 10 mm : zone utile de 190 × 128 mm, identique à la maquette validée.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(ReceiptTheme.Ink).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                ReceiptTheme.ComposeHeader(
                    column,
                    receipt.SchoolName,
                    receipt.SchoolAddress,
                    PhoneFormatter.FormatSenegal(receipt.SchoolPhone),
                    receipt.SchoolEmail,
                    receipt.SchoolNinea,
                    receipt.SchoolRegistreCommerce,
                    logo,
                    ("Inscription enregistrée", ReceiptTheme.BadgeStyle.Info),
                    ($"N° {ReceiptReference()}", ReceiptTheme.BadgeStyle.Neutral),
                    ($"Année {receipt.SchoolYearLabel}", ReceiptTheme.BadgeStyle.Neutral));

                column.Item().PaddingTop(7).Element(ComposeCartouche);
                column.Item().PaddingTop(7).Element(ComposeDeclaration);

                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(ComposeSettlementBlock);
                    row.ConstantItem(16);
                    row.RelativeItem().Element(ComposeMonthlyPlanBlock);
                });

                column.Item().PaddingTop(9).BorderTop(0.75f).BorderColor(ReceiptTheme.Rule).PaddingTop(7)
                    .Element(c => ReceiptTheme.ComposeSignatures(c, FaitA(), "Le Secrétariat", "Le Directeur"));

                column.Item().PaddingTop(6).Element(c => ReceiptTheme.Footnote(c,
                    "Le secrétariat n'encaisse aucun fonds : tout règlement s'effectue auprès de la comptabilité, "
                    + "seule habilitée à délivrer un reçu de caisse valant validation définitive."));
            });
        });
    }

    private void ComposeCartouche(IContainer container)
    {
        var cells = new List<(string, string)>
        {
            ("Élève", receipt.StudentFullName),
            ("Matricule", NoBreakText.NoBreak(receipt.Matricule)),
            // Classe passerelle / accélérée : le nom porte la mention du dispositif, parce que c'est SUR
            // CE PAPIER que le tuteur constate que l'année qu'il règle en couvre deux niveaux.
            ("Classe & niveau",
                $"{ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)} — {receipt.ClassroomLevel}")
        };

        // Cellule omise quand aucun tuteur n'est renseigné : une étiquette sans valeur ne dit rien.
        if (!string.IsNullOrWhiteSpace(receipt.GuardianName) || !string.IsNullOrWhiteSpace(receipt.GuardianPhone))
        {
            var phone = PhoneFormatter.FormatSenegal(receipt.GuardianPhone);
            cells.Add(("Tuteur", ReceiptTheme.JoinPresent(receipt.GuardianName, phone)));
        }

        ReceiptTheme.ComposeCartouche(container, cells.ToArray());
    }

    /// <summary>
    /// Le cœur juridique de l'attestation : la phrase que le tuteur présente comme preuve d'inscription
    /// (visa, bourse, changement d'établissement…).
    /// </summary>
    private void ComposeDeclaration(IContainer container)
    {
        container.BorderLeft(1.5f).BorderColor(ReceiptTheme.Rule).PaddingLeft(7).Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(ReceiptTheme.InkSoft).LineHeight(1.3f));
            text.Justify();
            text.Span("L'administration de l'établissement atteste que l'élève ");
            text.Span(receipt.StudentFullName).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(" (matricule ");
            text.Span(NoBreakText.NoBreak(receipt.Matricule)).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(") est régulièrement inscrit(e) au sein de notre établissement en classe de ");
            text.Span(ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass))
                .SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(" pour l'année scolaire ");
            text.Span(receipt.SchoolYearLabel).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(".");
        });
    }

    /// <summary>
    /// Bloc 1 — ce qu'il y a à régler MAINTENANT, auprès de la comptabilité. Montants unitaires : le
    /// frais ponctuel entier, une seule mensualité pour une ligne récurrente. Total à l'ENCRE, jamais
    /// en vert : rien n'est acquitté au moment où le secrétariat remet cette feuille.
    /// </summary>
    private void ComposeSettlementBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ReceiptTheme.SectionLabel(
                c, "Règlement à effectuer", "auprès du service de la comptabilité, ce jour"));

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(PdfColumnWidths.Amount);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Poste à régler").Bold().FontSize(6.5f)
                        .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold().FontSize(6.5f)
                        .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                });

                if (receipt.Lines.Count == 0)
                {
                    table.Cell().Element(BodyCell).Text("Aucun frais engagé")
                        .Italic().FontColor(ReceiptTheme.Muted);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(0));
                }
                else
                {
                    foreach (var line in receipt.Lines)
                    {
                        table.Cell().Element(BodyCell).Text(text =>
                        {
                            text.Span(line.Designation);
                            // « (1 mois) » : le tuteur doit voir qu'il ne règle QU'UN mois, pas l'année.
                            if (line.IsRecurring)
                            {
                                text.Span("  (1 mois)").FontSize(7).FontColor(ReceiptTheme.Muted);
                            }
                        });
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.UnitAmount));
                    }
                }

                table.Cell().Element(TotalCell).Text("TOTAL À RÉGLER").Bold().FontSize(7.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
                table.Cell().Element(TotalCell).AlignRight().Text(text =>
                {
                    // Nombre et « FCFA » sur la MÊME ligne : « FCFA » en plus petit pour tenir dans la
                    // colonne Amount sans repli (même hiérarchie que le panneau mensuel), et espace
                    // insécable entre les groupes de chiffres pour que « 67 000 » ne se coupe jamais.
                    text.Span(FormatMoney(receipt.InitialSettlementTotal).Replace(' ', ' '))
                        .Bold().FontSize(10.5f).FontColor(ReceiptTheme.Ink);
                    text.Span(" FCFA").SemiBold().FontSize(7.5f).FontColor(ReceiptTheme.InkSoft);
                });
            });
        });
    }

    /// <summary>
    /// Bloc 2 — l'échéancier, prospectif et indicatif. Tarifs UNITAIRES seuls : c'est la multiplication
    /// par le nombre de mois que la refonte supprime. Le total mensuel est le chiffre le plus lisible de
    /// la page, à la place du cumul annuel qu'il remplace.
    /// </summary>
    private void ComposeMonthlyPlanBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ReceiptTheme.SectionLabel(c, "Échéancier mensuel", "à titre indicatif"));

            column.Item().PaddingTop(4).Column(list =>
            {
                if (receipt.MonthlyLines.Count == 0)
                {
                    list.Item().Text("Aucun frais mensuel souscrit.").Italic().FontColor(ReceiptTheme.Muted);
                    return;
                }

                foreach (var line in receipt.MonthlyLines)
                {
                    list.Item().BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
                        .PaddingVertical(2.5f).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle().Text(line.Designation);
                        row.ConstantItem(88).AlignRight().AlignMiddle().Text(text =>
                        {
                            text.Span(FormatMoney(line.UnitAmount)).SemiBold().FontSize(8.5f);
                            text.Span(" FCFA / mois").FontSize(6.5f).FontColor(ReceiptTheme.Muted);
                        });
                    });
                }
            });

            column.Item().PaddingTop(5).Background(ReceiptTheme.PlanFill)
                .Border(0.5f).BorderColor(ReceiptTheme.PlanRule).Padding(5).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(label =>
                {
                    label.Item().Text("TOTAL À PRÉVOIR CHAQUE MOIS")
                        .Bold().FontSize(6.5f).FontColor(ReceiptTheme.Plan).LetterSpacing(0.07f);
                    label.Item().PaddingTop(1).Text("Règlement du 1er au 5 de chaque mois")
                        .FontSize(6).FontColor(ReceiptTheme.PlanAccent);
                });

                row.ConstantItem(96).AlignRight().AlignMiddle().Text(text =>
                {
                    text.Span(FormatMoney(receipt.MonthlyTotal)).Bold().FontSize(13).FontColor(ReceiptTheme.Plan);
                    text.Span(" FCFA / mois").FontSize(7).FontColor(ReceiptTheme.PlanAccent);
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(ReceiptTheme.HeadFill).BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
         .PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule).PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer TotalCell(IContainer c) =>
        c.BorderTop(1f).BorderColor(ReceiptTheme.RuleStrong).PaddingTop(4).PaddingBottom(1).PaddingHorizontal(6);

    private string FaitA()
    {
        var date = FormatDate(receipt.EnrolledAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
````


#### `src/SamaEcole.Infrastructure/Documents/ReceiptPdfGenerator.cs`

````csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IReceiptPdfGenerator"/> (ticket JGK-E02).
///
/// La licence Community de QuestPDF (gratuite, adaptée à ce produit) DOIT être posée une fois avant
/// toute génération, sinon la bibliothèque lève une exception. Le constructeur statique la fixe.
///
/// Le rendu passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission
/// d'un reçu OFFICIEL (nouvelle passe sans logo), et un rendu vide LÈVE une exception journalisée —
/// jamais un tableau de 0 octet qui partirait en 200 muet.
/// </summary>
public class ReceiptPdfGenerator(ILogger<ReceiptPdfGenerator> logger) : IReceiptPdfGenerator
{
    static ReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentReceiptDto receipt, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"reçu d'inscription {receipt.ReceiptNumber} (matricule {receipt.Matricule}, école {receipt.SchoolName})",
            () => new EnrollmentReceiptDocument(receipt, logo).GeneratePdf(),
            logo is not null ? () => new EnrollmentReceiptDocument(receipt, null).GeneratePdf() : null);
}
````


### A.4.3 Caisse : rapport de clôture et journal du jour

#### `src/SamaEcole.Infrastructure/Documents/DailyClosingReportDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyClosingReportDocument(DailyClosingReportDto report, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Rapport de Clôture de Caisse - {FormatDate(report.Date)}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Portrait());
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(15).Element(ComposeSummaryCards);

                if (report.TotalReversed > 0)
                {
                    column.Item().PaddingTop(6).Text($"Le total encaissé est NET de {FormatMoney(report.TotalReversed)} FCFA d'annulations de versements antérieurs (détail plus bas).")
                        .FontSize(8).Italic().FontColor(Colors.Red.Darken2);
                }

                if (report.ActualCashAmount.HasValue)
                {
                    column.Item().PaddingTop(15).Element(ComposeCashControl);
                }

                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().PaddingRight(10).Element(ComposeMethodBreakdown);
                    row.RelativeItem().PaddingLeft(10).Element(ComposeCategoryBreakdown);
                });

                column.Item().PaddingTop(20).Element(ComposeTransactionGrid);

                if (report.Reversals is { Count: > 0 })
                {
                    column.Item().PaddingTop(20).Element(ComposeReversals);
                }

                column.Item().PaddingTop(40).Element(ComposeSignatures);
            });
            
            page.Footer()
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" sur ");
                    x.TotalPages();
                });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(report.SchoolName.ToUpperInvariant()).Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                header.Item().Text(report.SchoolAddress).FontSize(8).FontColor(Colors.Grey.Medium);
                
                header.Item().PaddingTop(10).Text("Rapport de Clôture de Caisse Quotidien").Bold().FontSize(12);

                header.Item().PaddingTop(5).Text($"Date du jour : {FormatDate(report.Date)}");
                header.Item().Text($"Identifiant unique de session : {report.SessionId}");
                header.Item().Text($"Nom de l'agent caissier : {report.CashierName}");
                header.Item().Text($"Plage horaire de la session : {report.TimeRange}");
            });

            if (logo is not null)
            {
                row.ConstantItem(70).MaxHeight(70).Image(logo).FitArea();
            }
        });
    }

    private void ComposeSummaryCards(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Fonds de caisse initial").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.OpeningBalance)} FCFA").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
            });

            row.Spacing(10);

            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Total Encaissé").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.TotalCollected)} FCFA").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
            });

            row.Spacing(10);

            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Total Espèces en Caisse").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.TotalCashInRegister)} FCFA").Bold().FontSize(12).FontColor(Colors.Orange.Darken2);
            });
        });
    }

    /// <summary>
    /// Ticket JGK-F09 — comptage physique. Comparé aux ESPÈCES attendues (report.TotalCashInRegister),
    /// jamais au total encaissé toutes méthodes : un virement ou un mobile money ne passe jamais par le
    /// tiroir-caisse, il ne peut donc jamais entrer dans un écart de numéraire.
    /// </summary>
    private void ComposeCashControl(IContainer container)
    {
        var discrepancy = report.DiscrepancyAmount ?? 0;
        var discrepancyColor = discrepancy == 0 ? Colors.Green.Darken2 : Colors.Red.Darken2;

        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Text("Contrôle de caisse — comptage physique").Bold().FontSize(10);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Espèces attendues").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"{FormatMoney(report.TotalCashInRegister)} FCFA").Bold().FontSize(11);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Espèces comptées").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"{FormatMoney(report.ActualCashAmount ?? 0)} FCFA").Bold().FontSize(11);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Écart").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text($"{(discrepancy > 0 ? "+" : "")}{FormatMoney(discrepancy)} FCFA").Bold().FontSize(11).FontColor(discrepancyColor);
                });
            });

            if (discrepancy != 0)
            {
                col.Item().PaddingTop(8).Text(text =>
                {
                    text.Span("Motif de l'écart : ").Bold().FontSize(8);
                    text.Span(string.IsNullOrWhiteSpace(report.DiscrepancyReason) ? "—" : report.DiscrepancyReason).FontSize(8);
                });
            }
        });
    }

    private void ComposeMethodBreakdown(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Répartition par Mode de Paiement").Bold().FontSize(10);
            
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).Text("Mode").FontSize(8).FontColor(Colors.Grey.Darken2);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).FontColor(Colors.Grey.Darken2);
                });

                foreach (var method in report.MethodBreakdowns)
                {
                    table.Cell().PaddingVertical(2).Text(method.Method.ToString()).FontSize(8);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{FormatMoney(method.Amount)} FCFA").FontSize(8).Bold();
                }
            });
        });
    }

    private void ComposeCategoryBreakdown(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Répartition par Catégorie de Frais").Bold().FontSize(10);
            
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).Text("Catégorie").FontSize(8).FontColor(Colors.Grey.Darken2);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).FontColor(Colors.Grey.Darken2);
                });

                foreach (var category in report.CategoryBreakdowns)
                {
                    table.Cell().PaddingVertical(2).Text(category.CategoryName).FontSize(8);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{FormatMoney(category.Amount)} FCFA").FontSize(8).Bold();
                }
            });
        });
    }

    private void ComposeTransactionGrid(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Grille Détaillée des Transactions").Bold().FontSize(10);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(PdfColumnWidths.Time);       // Heure
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Reçu — « REC‑2025‑0002 » sur une seule ligne
                    columns.RelativeColumn(2.6f);                       // Élève / Tiers — contenu libre, prend le reste
                    columns.RelativeColumn(1.4f);                       // Catégorie — libellés courts
                    columns.ConstantColumn(60);                         // Mode
                    columns.ConstantColumn(PdfColumnWidths.Amount);     // Montant
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Heure").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Reçu").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Élève / Tiers").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Catégorie").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Mode").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).Bold();
                });

                foreach (var tx in report.Transactions)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Time).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(NoBreakText.NoBreak(tx.ReceiptNumber)).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.StudentName).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Category).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Method).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(FormatMoney(tx.Amount)).FontSize(8).Bold();

                    if (!string.IsNullOrWhiteSpace(tx.CancelledNote))
                    {
                        // Le versement reste compté dans ce journal (chiffres signés à la clôture) : on dit qu'il a été annulé depuis.
                        table.Cell().ColumnSpan(6).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingBottom(2)
                            .Text(tx.CancelledNote).FontSize(7).Italic().FontColor(Colors.Red.Darken2);
                    }
                }
            });
        });
    }

    /// <summary>
    /// Annulations de versements d'une session DÉJÀ clôturée, faites pendant celle-ci : écritures négatives qui
    /// réduisent le total encaissé de CETTE session, sans toucher à la journée d'origine (contre-passation).
    /// </summary>
    private void ComposeReversals(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Annulations de versements antérieurs (écritures négatives)").Bold().FontSize(10).FontColor(Colors.Red.Darken2);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(PdfColumnWidths.Time + 20);  // Date/heure de l'annulation
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Reçu annulé
                    columns.RelativeColumn(2.2f);                       // Élève
                    columns.ConstantColumn(60);                         // Mode
                    columns.ConstantColumn(PdfColumnWidths.Amount);     // Montant (négatif)
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Annulé le").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Reçu").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Élève / Tiers").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Mode").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).Bold();
                });

                foreach (var reversal in report.Reversals!)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(reversal.Time).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(NoBreakText.NoBreak(reversal.ReceiptNumber)).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(reversal.StudentName).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(reversal.Method).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(FormatMoney(reversal.Amount)).FontSize(8).Bold().FontColor(Colors.Red.Darken2);

                    table.Cell().ColumnSpan(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingBottom(2)
                        .Text(reversal.Note).FontSize(7).Italic().FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignCenter().Column(col =>
            {
                col.Item().Text("Visa de l'Agent Caissier").FontSize(9).Bold();
                col.Item().PaddingTop(30).Text("......................................................").FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignCenter().Column(col =>
            {
                col.Item().Text("Visa du Contrôleur / Gérant").FontSize(9).Bold();
                col.Item().PaddingTop(30).Text("......................................................").FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTime date) =>
        date.ToString("dddd d MMMM yyyy", new CultureInfo("fr-FR"));
}

public class DailyClosingReportPdfGenerator(ILogger<DailyClosingReportPdfGenerator> logger) : IDailyClosingReportPdfGenerator
{
    // Manquait ici alors que les 25 autres générateurs QuestPDF du projet le posent tous dans leur
    // constructeur statique : sans lui, QuestPDF.Drawing.DocumentGenerator.ValidateLicense() lève à
    // CHAQUE appel, jamais seulement au premier — ce générateur n'a donc jamais produit un PDF, même en
    // développement. Bug pré-existant, mis au jour en cliquant réellement le rapport de clôture depuis
    // /caisse (27/08/2026) après le câblage de l'écran session de caisse.
    static DailyClosingReportPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Rendu via <see cref="PdfRenderGuard"/> (filet commun à tous les générateurs) : un logo
    /// d'établissement illisible ne doit JAMAIS empêcher l'émission du rapport de clôture (nouvelle
    /// passe sans logo), et un rendu vide (0 octet) LÈVE une exception journalisée — jamais un 200 au
    /// corps vide qui bloquerait l'aperçu client sans laisser de trace.
    /// </summary>
    public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo) =>
        PdfRenderGuard.Render(
            logger,
            $"rapport de clôture de caisse (session {report.SessionId})",
            () => new DailyClosingReportDocument(report, schoolLogo).GeneratePdf(),
            schoolLogo is not null ? () => new DailyClosingReportDocument(report, null).GeneratePdf() : null);
}
````


#### `src/SamaEcole.Infrastructure/Documents/DailyCashRegisterDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyCashRegisterDocument(DailyCashRegisterDto report, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Journal de Caisse - {FormatDate(report.Date)}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Portrait());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(10).AlignCenter()
                    .Text($"JOURNAL DE CAISSE DU {FormatDate(report.Date)}").Bold().FontSize(12).Underline();

                column.Item().PaddingTop(12).Element(ComposeSummaryBlock);

                column.Item().PaddingTop(16).Element(ComposeDetailedTable);

                column.Item().PaddingTop(20).Element(ComposeSignatures);
            });
            
            page.Footer()
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" sur ");
                    x.TotalPages();
                });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(report.SchoolName.ToUpperInvariant()).Bold().FontSize(14);

                var contact = JoinPresent(report.SchoolAddress, PhoneFormatter.FormatSenegal(report.SchoolPhone), report.SchoolEmail);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(8).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(70).MaxHeight(50).Image(logo).FitArea();
            }
            else
            {
                row.ConstantItem(70).AlignRight().AlignMiddle()
                    .Text("[Logo]").FontSize(8).FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeSummaryBlock(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(2).Column(column =>
            {
                column.Item().Text("RÉCAPITULATIF PAR MODE DE PAIEMENT").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
                column.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });

                    foreach (var total in report.TotalsByMethod)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(MethodLabel(total.Method));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(FormatMoney(total.Total)).Bold();
                    }

                    table.Cell().PaddingTop(4).Text("TOTAL ENCAISSÉ").Bold();
                    table.Cell().PaddingTop(4).AlignRight().Text(FormatMoney(report.TotalCollected)).Bold().FontSize(10);
                });
            });

            row.ConstantItem(20);

            row.RelativeItem(1).Background(Colors.Grey.Lighten4).Padding(6).Column(column =>
            {
                column.Item().Text("INFORMATIONS").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(2).Text($"Date : {FormatDate(report.Date)}").FontSize(8);
                column.Item().Text($"Transactions : {report.Payments.Count(p => !p.IsReversal)}").FontSize(8);
                if (report.Payments.Any(p => p.IsReversal))
                {
                    column.Item().Text($"Annulations d'un jour antérieur : {report.Payments.Count(p => p.IsReversal)}").FontSize(8).FontColor(Colors.Red.Darken2);
                }
            });
        });
    }

    private void ComposeDetailedTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text("DÉTAIL DES ENCAISSEMENTS ET ANNULATIONS").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(PdfColumnWidths.Time);       // Heure
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule
                    columns.RelativeColumn(3);                          // Nom — seule colonne à contenu libre, prend le reste
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Reçu N° — « REC‑2025‑0002 », même gabarit qu'un matricule
                    columns.RelativeColumn(1.2f);                       // Mode — libellés courts (« Espèces », « Wave »)
                    columns.ConstantColumn(PdfColumnWidths.Amount);     // Montant
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Heure").Bold();
                    header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8f);
                    header.Cell().Element(HeaderCell).Text("Nom Complet").Bold();
                    header.Cell().Element(HeaderCell).Text("Reçu N°").Bold();
                    header.Cell().Element(HeaderCell).Text("Mode").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                if (report.Payments.Count == 0)
                {
                    table.Cell().ColumnSpan(6).Padding(8).AlignCenter().Text("Aucun encaissement à cette date.").Italic();
                }
                else
                {
                    foreach (var payment in report.Payments)
                    {
                        table.Cell().Element(BodyCell).Text(FormatTime(payment.PaidAt));
                        table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(payment.Matricule)).FontSize(8f);
                        table.Cell().Element(BodyCell).Text(payment.StudentFullName);
                        table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(payment.ReceiptNumber));
                        table.Cell().Element(BodyCell).Text(MethodLabel(payment.Method));
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(payment.Amount)).Bold()
                            .FontColor(payment.IsReversal ? Colors.Red.Darken2 : Colors.Black);

                        if (!string.IsNullOrWhiteSpace(payment.Note))
                        {
                            table.Cell().ColumnSpan(6).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingBottom(2).PaddingHorizontal(2)
                                .Text(payment.Note).FontSize(7).Italic().FontColor(payment.IsReversal ? Colors.Red.Darken2 : Colors.Grey.Darken2);
                        }
                    }
                }
            });
        });

        static IContainer HeaderCell(IContainer c) =>
            c.BorderBottom(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(2);
        static IContainer BodyCell(IContainer c) =>
            c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2);
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text($"Fait le {FormatDate(DateTimeOffset.Now)}").Italic();
                left.Item().PaddingTop(20).Text("Le Caissier / Service Financier").Bold();
            });

            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Le Directeur").Bold();
            });
        });
    }

    private static string MethodLabel(string method) => method switch
    {
        "Cash" => "Espèces",
        "Cheque" => "Chèque",
        "Transfer" => "Virement",
        "MobileMoney" => "Mobile Money",
        _ => method
    };

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        
    private static string FormatTime(DateTimeOffset moment) =>
        moment.ToString("HH:mm", CultureInfo.InvariantCulture);
}
````


#### `src/SamaEcole.Infrastructure/Documents/DailyCashRegisterPdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyCashRegisterPdfGenerator(ILogger<DailyCashRegisterPdfGenerator>? logger = null) : IDailyCashRegisterPdfGenerator
{
    static DailyCashRegisterPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DailyCashRegisterDto data, byte[]? schoolLogo) =>
        PdfRenderGuard.Render(
            logger,
            $"livre de caisse du jour (école {data.SchoolName}, {data.Date:yyyy-MM-dd})",
            () => new DailyCashRegisterDocument(data, schoolLogo).GeneratePdf(),
            schoolLogo is not null ? () => new DailyCashRegisterDocument(data, null).GeneratePdf() : null);
}
````


### A.4.4 Paie : bulletin / reçu de salaire, fiche de suivi des heures, attestation de travail

#### `src/SamaEcole.Infrastructure/Documents/PayslipDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin de Paie (A4 portrait) — détail des cotisations sociales sénégalaises (IPRES, CSS,
/// VRS/CFCE, BRS) et du net à payer, remis mensuellement à l'employé. Contrairement au reçu ou au
/// certificat, ce n'est pas un document destiné à l'élève/tuteur mais un document RH interne : pas de
/// mention du formalisme M.E.N., juste l'identité de l'établissement comme employeur (même en-tête
/// que le reçu de paiement).
/// </summary>
public class PayslipDocument(PayslipDto payslip, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Bulletin de paie {payslip.PayslipNumber}",
        Author = payslip.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                if (payslip.CancelledAt is not null)
                {
                    column.Item().PaddingTop(10).Border(1).BorderColor(Colors.Red.Darken2).Padding(6).Column(c =>
                    {
                        c.Item().AlignCenter().Text("FICHE ANNULÉE — SANS VALEUR").Bold().FontSize(12).FontColor(Colors.Red.Darken2);
                        if (!string.IsNullOrWhiteSpace(payslip.CancellationReason))
                        {
                            c.Item().AlignCenter().Text($"Motif : {payslip.CancellationReason}").FontSize(8).FontColor(Colors.Red.Darken2);
                        }
                    });
                }

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("BULLETIN DE PAIE").Bold().FontSize(15);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(payslip.PayslipNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeEmployeeBlock);
                column.Item().PaddingTop(14).Element(ComposeEarningsTable);
                column.Item().PaddingTop(6).Element(ComposeNetPayBlock);
                if (payslip.PaidAt is not null && payslip.CancelledAt is null)
                {
                    column.Item().PaddingTop(10).Element(ComposePaymentReceipt);
                }
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(payslip.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                if (!string.IsNullOrWhiteSpace(payslip.SchoolAddress))
                {
                    header.Item().Text(payslip.SchoolAddress).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(payslip.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {payslip.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeEmployeeBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                InfoRow(left, "Employé(e)", payslip.EmployeeFullName);
                InfoRow(left, "Qualité", payslip.EmployeeRole);
            });
            row.RelativeItem().Column(right =>
            {
                InfoRow(right, "Type de contrat", ContractTypeLabel());
                if (payslip.HoursWorked > 0)
                {
                    InfoRow(right, "Heures travaillées", payslip.HoursWorked.ToString("0.##", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(100).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeEarningsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();                       // Élément — libellé libre, prend le reste
                columns.ConstantColumn(PdfColumnWidths.Amount); // Part salariale
                columns.ConstantColumn(PdfColumnWidths.Amount); // Part patronale
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Élément").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Part salariale").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Part patronale").Bold();
            });

            Row("Salaire brut", payslip.GrossSalary, null);
            Row("Prime de transport", payslip.TransportAllowance, null);

            if (payslip.DeductionsApplied)
            {
                Row("IPRES (retraite)", -payslip.IpresEmployee, payslip.IpresEmployer);
                Row("CSS (prestations familiales)", null, payslip.CssEmployer);
                Row("VRS / CFCE", null, payslip.Vrs);
                Row("BRS (retenue à la source)", -payslip.Brs, null);
            }
            else
            {
                // Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) : aucune ligne de cotisation à
                // montrer, mais une mention explicite pour que le net égal au brut ne passe pas pour un oubli.
                table.Cell().ColumnSpan(3).Element(BodyCell).Text(
                    "Paie sans retenues : aucune cotisation ni retenue légale (IPRES, CSS, VRS/CFCE, BRS) n'est appliquée.")
                    .Italic().FontColor(Colors.Grey.Darken2);
            }

            void Row(string label, decimal? employee, decimal? employer)
            {
                table.Cell().Element(BodyCell).Text(label);
                table.Cell().Element(BodyCell).AlignRight().Text(employee.HasValue ? FormatMoney(employee.Value) : "—");
                table.Cell().Element(BodyCell).AlignRight().Text(employer.HasValue ? FormatMoney(employer.Value) : "—");
            }
        });
    }

    private void ComposeNetPayBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("NET À PAYER").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(payslip.NetSalary)).Bold().FontSize(13);
        });
    }

    /// <summary>
    /// Reçu de paiement du salaire (Volume 1 §14.6) : le bulletin d'une fiche réglée vaut reçu. Il est signé par
    /// l'employé(e), ce qui permet de payer une personne sans compte (gardien…) en gardant une pièce justificative.
    /// </summary>
    private void ComposePaymentReceipt(IContainer container)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(c =>
        {
            c.Item().Text("REÇU DE PAIEMENT DU SALAIRE").Bold().FontSize(10);
            c.Item().PaddingTop(3).Text(
                $"Reçu de {payslip.SchoolName} la somme nette de {FormatMoney(payslip.NetSalary)}, " +
                $"réglée le {payslip.PaidAt!.Value:dd/MM/yyyy} {PaymentMethodLabel(payslip.PaidWith)}, " +
                $"pour la période de {PeriodLabel()}.").FontSize(9);
        });
    }

    private static string PaymentMethodLabel(string? method) => method switch
    {
        "Cash" => "en espèces",
        "Cheque" => "par chèque",
        "Transfer" => "par virement",
        "MobileMoney" => "par mobile money",
        _ => string.Empty
    };

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Signature de l'Employeur").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Signature de l'Employé(e)").Italic().FontSize(9);
                right.Item().PaddingTop(30).AlignRight().Text("________________________").FontSize(9);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private string PeriodLabel() =>
        new DateTime(payslip.Year, payslip.Month, 1).ToString("MMMM yyyy", French);

    private string ContractTypeLabel() =>
        payslip.ContractType == "Vacataire" ? "Vacataire (horaire)" : "Permanent";

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
````


#### `src/SamaEcole.Infrastructure/Documents/PayslipPdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class PayslipPdfGenerator(ILogger<PayslipPdfGenerator>? logger = null) : IPayslipPdfGenerator
{
    static PayslipPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PayslipDto payslip, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"bulletin de paie {payslip.PayslipNumber} ({payslip.EmployeeFullName}, fiche {payslip.FichePaieId})",
            () => new PayslipDocument(payslip, logo).GeneratePdf(),
            logo is not null ? () => new PayslipDocument(payslip, null).GeneratePdf() : null);
}
````


#### `src/SamaEcole.Infrastructure/Documents/HourRecordSheetDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche de suivi des heures d'un Vacataire (A4 portrait) — document RH interne, même charte que le
/// bulletin de paie (<see cref="PayslipDocument"/>) : pas de bandeau M.E.N.
/// </summary>
public class HourRecordSheetDocument(HourRecordSheetDto sheet, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Fiche d'heures {sheet.SheetNumber}",
        Author = sheet.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("FICHE DE SUIVI DES HEURES").Bold().FontSize(15);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {sheet.SheetNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeEmployeeBlock);
                column.Item().PaddingTop(14).Element(ComposeHoursTable);
                column.Item().PaddingTop(6).Element(ComposeTotalBlock);
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Text(sheet.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeEmployeeBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                InfoRow(left, "Employé(e)", sheet.EmployeeFullName);
                InfoRow(left, "Type de contrat", "Vacataire (horaire)");
            });
            row.RelativeItem().Column(right =>
            {
                InfoRow(right, "Taux horaire", FormatMoney(sheet.HourlyRate) + " / h");
                InfoRow(right, "Total du mois", $"{sheet.TotalHours:0.##} h");
            });
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(100).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeHoursTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(PdfColumnWidths.Date); // Date
                columns.ConstantColumn(52);                   // Heures — « 7.5 », l'en-tête fait la largeur
                columns.RelativeColumn();                     // Note — contenu libre, prend le reste
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Date").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Heures").Bold();
                header.Cell().Element(HeaderCell).Text("Note").Bold();
            });

            if (sheet.Lines.Count == 0)
            {
                table.Cell().ColumnSpan(3).Element(BodyCell).AlignCenter()
                    .Text("Aucune heure enregistrée pour cette période.").Italic().FontColor(Colors.Grey.Medium);
            }

            foreach (var line in sheet.Lines)
            {
                table.Cell().Element(BodyCell).Text(FormatDate(line.Date));
                table.Cell().Element(BodyCell).AlignRight().Text(line.Hours.ToString("0.##", CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(line.Note ?? "—");
            }
        });
    }

    private void ComposeTotalBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("TOTAL HEURES DU MOIS").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text($"{sheet.TotalHours:0.##} h").Bold().FontSize(13);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Signature de l'Employé(e)").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Visa du Superviseur").Italic().FontSize(9);
                right.Item().PaddingTop(30).AlignRight().Text("________________________").FontSize(9);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private string PeriodLabel() =>
        new DateTime(sheet.Year, sheet.Month, 1).ToString("MMMM yyyy", French);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
````


#### `src/SamaEcole.Infrastructure/Documents/HourRecordSheetPdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class HourRecordSheetPdfGenerator(ILogger<HourRecordSheetPdfGenerator>? logger = null) : IHourRecordSheetPdfGenerator
{
    static HourRecordSheetPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(HourRecordSheetDto sheet, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"fiche des heures {sheet.SheetNumber} ({sheet.EmployeeFullName}, {sheet.Month:00}/{sheet.Year}, contrat {sheet.EmployeeContractId})",
            () => new HourRecordSheetDocument(sheet, logo).GeneratePdf(),
            logo is not null ? () => new HourRecordSheetDocument(sheet, null).GeneratePdf() : null);
}
````


#### `src/SamaEcole.Infrastructure/Documents/WorkCertificateDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Attestation de travail (A4 portrait) — document RH interne remis à l'employé (banque, ambassade,
/// démarche administrative personnelle). Même charte que le bulletin de paie : en-tête établissement
/// employeur (NINEA), pas de bandeau M.E.N.
/// </summary>
public class WorkCertificateDocument(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Attestation de travail {certificate.CertificateNumber}",
        Author = certificate.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(24).AlignCenter()
                    .Text("ATTESTATION DE TRAVAIL").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(certificate.CertificateNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(28).Element(ComposeBody);
                column.Item().PaddingTop(40).Element(ComposeSignature);
                column.Item().PaddingTop(30).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, certificate.CertificateNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(certificate.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(certificate.SchoolAddress, PhoneFormatter.FormatSenegal(certificate.SchoolPhone));
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(certificate.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {certificate.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.LineHeight(1.6f));
                text.Justify();
                text.Span("Je soussigné(e), Chef d'Établissement de ");
                text.Span(certificate.SchoolName).Bold();
                if (!string.IsNullOrWhiteSpace(certificate.SchoolAddress))
                {
                    text.Span($" ({certificate.SchoolAddress})");
                }
                text.Span(", certifie que ");
                text.Span(certificate.EmployeeFullName).Bold();
                text.Span(" est employé(e) au sein de notre établissement en qualité de ");
                text.Span(certificate.EmployeeRole).Bold();
                text.Span($", sous contrat de type {ContractTypeLabel()}, depuis le ");
                text.Span(FormatDate(certificate.SinceDate)).Bold();
                text.Span(".");
            });

            column.Item().PaddingTop(14).Text(
                "La présente attestation est délivrée à l'intéressé(e) pour servir et valoir ce que de droit.");
        });
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text(FaitA()).Italic().FontSize(10);
                right.Item().PaddingTop(4).AlignCenter().Text("Le Chef d'Établissement").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string ContractTypeLabel() =>
        certificate.ContractType == "Vacataire" ? "Vacataire (horaire)" : "Permanent";

    private string FaitA()
    {
        var date = FormatDate(certificate.IssuedAt);
        return string.IsNullOrWhiteSpace(certificate.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {certificate.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
````


#### `src/SamaEcole.Infrastructure/Documents/WorkCertificatePdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class WorkCertificatePdfGenerator(ILogger<WorkCertificatePdfGenerator>? logger = null) : IWorkCertificatePdfGenerator
{
    static WorkCertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"attestation de travail {certificate.CertificateNumber} ({certificate.EmployeeFullName}, contrat {certificate.ContractId})",
            () => new WorkCertificateDocument(certificate, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new WorkCertificateDocument(certificate, null, qrCodeImage).GeneratePdf() : null);
}
````


### A.4.5 Fiscalité

#### `src/SamaEcole.Infrastructure/Documents/TaxDeclarationDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Déclaration Fiscale mensuelle (A4 portrait) — état synthétique des charges sociales sénégalaises
/// (IPRES, CSS, VRS, BRS) et de la TVA (collectée/déductible/nette) remis chaque mois à
/// l'administration fiscale. Document Finance interne (comme le bulletin de paie) : pas de mention du
/// formalisme M.E.N., juste l'identité de l'établissement comme déclarant.
/// </summary>
public class TaxDeclarationDocument(TaxDeclarationDto declaration, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Déclaration fiscale {declaration.DeclarationNumber}",
        Author = declaration.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("DÉCLARATION FISCALE MENSUELLE").Bold().FontSize(15);
                column.Item().AlignCenter().Text("État synthétique — VRS, IPRES, CSS, BRS et TVA").FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(declaration.DeclarationNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeChargesTable);
                column.Item().PaddingTop(14).Element(ComposeTvaTable);
                column.Item().PaddingTop(6).Element(ComposeTotalDueBlock);
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(declaration.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                if (!string.IsNullOrWhiteSpace(declaration.SchoolAddress))
                {
                    header.Item().Text(declaration.SchoolAddress).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(declaration.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {declaration.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeChargesTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("CHARGES SOCIALES").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();                       // Cotisation — libellé libre, prend le reste
                    columns.ConstantColumn(PdfColumnWidths.Amount); // Montant — largeur fixe, jamais de repli
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Cotisation").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                Row("IPRES (retraite)", declaration.TotalIpres);
                Row("CSS (prestations familiales)", declaration.TotalCss);
                Row("VRS / CFCE", declaration.TotalVrs);
                Row("BRS (retenue à la source)", declaration.TotalBrs);

                void Row(string label, decimal amount)
                {
                    table.Cell().Element(BodyCell).Text(label);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(amount));
                }
            });
        });
    }

    private void ComposeTvaTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("TAXE SUR LA VALEUR AJOUTÉE (TVA)").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();                       // Élément — libellé libre, prend le reste
                    columns.ConstantColumn(PdfColumnWidths.Amount); // Montant — largeur fixe, jamais de repli
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Élément").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                Row("TVA collectée", declaration.TvaCollected);
                Row("TVA déductible", declaration.TvaDeductible);
                Row("TVA nette", declaration.NetTva);

                void Row(string label, decimal amount)
                {
                    table.Cell().Element(BodyCell).Text(label);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(amount));
                }
            });
        });
    }

    private void ComposeTotalDueBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("TOTAL DÛ À L'ÉTAT").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(declaration.TotalDueToState)).Bold().FontSize(13);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Le Service Finance").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Le Directeur").Italic().FontSize(9);
                right.Item().PaddingTop(30).AlignRight().Text("________________________").FontSize(9);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private string PeriodLabel() =>
        new DateTime(declaration.Year, declaration.Month, 1).ToString("MMMM yyyy", French);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
````


#### `src/SamaEcole.Infrastructure/Documents/TaxDeclarationPdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class TaxDeclarationPdfGenerator(ILogger<TaxDeclarationPdfGenerator>? logger = null) : ITaxDeclarationPdfGenerator
{
    static TaxDeclarationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(TaxDeclarationDto declaration, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"déclaration fiscale {declaration.DeclarationNumber} ({declaration.Month:00}/{declaration.Year}, id {declaration.Id})",
            () => new TaxDeclarationDocument(declaration, logo).GeneratePdf(),
            logo is not null ? () => new TaxDeclarationDocument(declaration, null).GeneratePdf() : null);
}
````


### A.4.6 Recouvrement : sommation et engagement financier

#### `src/SamaEcole.Infrastructure/Documents/DuesNoticeDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Sommation pour impayés (A4 portrait) — lettre de mise en demeure sur papier à en-tête de
/// l'établissement (même charte que le reçu/bulletin de paie : NINEA, pas de bandeau M.E.N., ce
/// n'est pas un acte académique mais une communication financière de l'école en tant qu'entité privée).
/// </summary>
public class DuesNoticeDocument(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Sommation {notice.NoticeNumber}",
        Author = notice.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("SOMMATION POUR IMPAYÉS").Bold().FontSize(15);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(notice.NoticeNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Text(FaitA()).AlignRight().Italic().FontSize(9);

                column.Item().PaddingTop(10).Element(ComposeRecipientBlock);
                column.Item().PaddingTop(14).Element(ComposeBody);
                column.Item().PaddingTop(10).Element(ComposeInstallmentsTable);
                column.Item().PaddingTop(10).Element(ComposeTotalBlock);
                column.Item().PaddingTop(20).Element(ComposeClosing);
                column.Item().PaddingTop(30).Element(ComposeSignature);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, notice.NoticeNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(notice.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(notice.SchoolAddress, PhoneFormatter.FormatSenegal(notice.SchoolPhone));
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(notice.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {notice.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeRecipientBlock(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(10));
            text.Span("À l'attention de : ").SemiBold();
            text.Span(string.IsNullOrWhiteSpace(notice.GuardianName) ? "Tuteur / Responsable légal" : notice.GuardianName);
            if (!string.IsNullOrWhiteSpace(notice.GuardianPhone))
            {
                text.Span($" ({PhoneFormatter.FormatSenegal(notice.GuardianPhone)})");
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.5f));
            text.Justify();
            text.Span("Nous portons à votre connaissance que le compte scolaire de l'élève ");
            text.Span(notice.StudentFullName).Bold();
            text.Span($" (matricule {NoBreakText.NoBreak(notice.Matricule)}, classe {notice.ClassroomName}, année scolaire {notice.SchoolYearLabel}) présente à ce jour ");
            text.Span("une ou plusieurs échéances de frais de scolarité échues et non réglées, détaillées ci-dessous.");
        });
    }

    private void ComposeInstallmentsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();                       // Échéance — libellé libre, prend le reste
                columns.ConstantColumn(PdfColumnWidths.Date);   // Date d'exigibilité — l'année ne saute plus à la ligne
                columns.ConstantColumn(PdfColumnWidths.Amount); // Montant dû
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Échéance").Bold();
                header.Cell().Element(HeaderCell).Text("Date d'exigibilité").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant dû").Bold();
            });

            foreach (var installment in notice.OverdueInstallments)
            {
                table.Cell().Element(BodyCell).Text(installment.Designation);
                table.Cell().Element(BodyCell).Text(FormatDate(installment.DueDate));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(installment.Amount));
            }
        });
    }

    private void ComposeTotalBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("SOLDE TOTAL RESTANT DÛ").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(notice.RemainingBalance)).Bold().FontSize(13);
        });
    }

    private void ComposeClosing(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.5f));
            text.Justify();
            text.Span("Nous vous prions de bien vouloir régulariser cette situation dans les meilleurs délais ");
            text.Span("auprès du service Finance de l'établissement. À défaut de régularisation, l'établissement ");
            text.Span("se réserve le droit d'appliquer les mesures prévues par le règlement intérieur.");
        });
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Le Service Finance").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA() =>
        string.IsNullOrWhiteSpace(notice.SchoolCity)
            ? $"Fait le {FormatDate(notice.IssuedAt)}"
            : $"Fait à {notice.SchoolCity}, le {FormatDate(notice.IssuedAt)}";

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
````


#### `src/SamaEcole.Infrastructure/Documents/DuesNoticePdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DuesNoticePdfGenerator(ILogger<DuesNoticePdfGenerator>? logger = null) : IDuesNoticePdfGenerator
{
    static DuesNoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"sommation pour impayés {notice.NoticeNumber} (matricule {notice.Matricule}, inscription {notice.EnrollmentId})",
            () => new DuesNoticeDocument(notice, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new DuesNoticeDocument(notice, null, qrCodeImage).GeneratePdf() : null);
}
````


#### `src/SamaEcole.Infrastructure/Documents/FinancialCommitmentDocument.cs`

````csharp
using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Engagement financier / reconnaissance de dette (A4 portrait) — trace écrite d'un accord
/// d'échéancier entre l'établissement et le tuteur, sur papier à en-tête de l'école (comme le reçu),
/// sans bandeau M.E.N. : ce n'est pas un acte académique.
/// </summary>
public class FinancialCommitmentDocument(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Engagement financier {commitment.CommitmentNumber}",
        Author = commitment.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(20).AlignCenter()
                    .Text("ENGAGEMENT FINANCIER").Bold().FontSize(15).Underline();
                column.Item().AlignCenter()
                    .Text("(Reconnaissance de dette et échéancier de paiement)").Italic().FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(commitment.CommitmentNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(20).Element(ComposeBody);
                column.Item().PaddingTop(14).Element(ComposeAmountBlock);
                column.Item().PaddingTop(14).Element(ComposeTermsBlock);
                column.Item().PaddingTop(36).Element(ComposeSignatures);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, commitment.CommitmentNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(commitment.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(commitment.SchoolAddress, PhoneFormatter.FormatSenegal(commitment.SchoolPhone));
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(commitment.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {commitment.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.6f));
            text.Justify();
            text.Span("Je soussigné(e), ");
            text.Span(string.IsNullOrWhiteSpace(commitment.GuardianName) ? "le tuteur / la tutrice" : commitment.GuardianName).Bold();
            if (!string.IsNullOrWhiteSpace(commitment.GuardianPhone))
            {
                text.Span($" ({PhoneFormatter.FormatSenegal(commitment.GuardianPhone)})");
            }
            text.Span(", responsable légal(e) de l'élève ");
            text.Span(commitment.StudentFullName).Bold();
            text.Span($" (matricule {NoBreakText.NoBreak(commitment.Matricule)}, classe {commitment.ClassroomName}, année scolaire {commitment.SchoolYearLabel}), ");
            text.Span("reconnais devoir à l'établissement ");
            text.Span(commitment.SchoolName).Bold();
            text.Span(" la somme précisée ci-dessous, et m'engage à la régler selon l'échéancier convenu.");
        });
    }

    private void ComposeAmountBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
            {
                row.RelativeItem().Text("MONTANT DE L'ENGAGEMENT").Bold().FontSize(11);
                row.RelativeItem().AlignRight().Text(FormatMoney(commitment.Amount)).Bold().FontSize(13);
            });
            column.Item().PaddingTop(4).Text($"Échéance convenue : {FormatDate(commitment.DueDate)}").FontSize(9).FontColor(Colors.Grey.Darken2);
        });
    }

    private void ComposeTermsBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(column =>
        {
            column.Item().Text("MODALITÉS CONVENUES").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Text(commitment.Terms).FontSize(10);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().AlignCenter().Text(FaitA()).Italic().FontSize(10);
                left.Item().PaddingTop(4).AlignCenter().Text("Signature du parent / tuteur").FontSize(10);
                left.Item().PaddingTop(30).AlignCenter().Text("________________________").FontSize(9);
            });
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Pour l'établissement").FontSize(10);
                right.Item().PaddingTop(4).AlignCenter().Text("Le Service Finance").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA()
    {
        var date = FormatDate(commitment.SignedAt);
        return string.IsNullOrWhiteSpace(commitment.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {commitment.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
````


#### `src/SamaEcole.Infrastructure/Documents/FinancialCommitmentPdfGenerator.cs`

````csharp
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class FinancialCommitmentPdfGenerator(ILogger<FinancialCommitmentPdfGenerator>? logger = null) : IFinancialCommitmentPdfGenerator
{
    static FinancialCommitmentPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"engagement financier {commitment.CommitmentNumber} (matricule {commitment.Matricule}, engagement {commitment.FinancialCommitmentId})",
            () => new FinancialCommitmentDocument(commitment, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new FinancialCommitmentDocument(commitment, null, qrCodeImage).GeneratePdf() : null);
}
````


## A.5 Migrations EF Core / SQL (RLS, GRANT, contraintes)

> Ordre chronologique (le préfixe est la date). Les fichiers `*.Designer.cs` (instantané du modèle) ne sont pas reproduits : ils sont régénérés par `dotnet ef migrations add`. Chaque `Up()` est à lire pour ses blocs `migrationBuilder.Sql(...)` : `ENABLE ROW LEVEL SECURITY`, `CREATE POLICY …_tenant_isolation`, `GRANT`/`REVOKE`.
> **Dans un nouveau projet :** ne reprenez pas l'historique tel quel ; écrivez des migrations propres à partir de §5 du document principal, en n'accordant **jamais** `DELETE` à l'utilisateur applicatif et en limitant les tables d'historique à `SELECT, INSERT`.


#### `src/SamaEcole.Persistence/Migrations/20260714014909_AddMatriculeSequences.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatriculeSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matricule_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matricule_sequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_matricule_sequences_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matricule_sequences_SchoolId_Kind_Year",
                table: "matricule_sequences",
                columns: new[] { "SchoolId", "Kind", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matricule_sequences");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260714023704_EnableRowLevelSecurity.cs`

````csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-A03 — Row-Level Security PostgreSQL (AGENTS.md règle #2).
    ///
    /// Le Global Query Filter EF Core ne protège que le code C# qui passe par EF. La RLS, elle,
    /// protège la BASE : même une requête SQL brute, même un ORM mal configuré, même un filtre
    /// oublié sur une nouvelle entité, ne peuvent lire les données d'une autre école.
    ///
    /// Le tenant courant est lu dans la variable de session `app.current_school_id`, posée à chaque
    /// ouverture de connexion par TenantConnectionInterceptor à partir du claim JWT. Si elle est
    /// absente ou vide, NULLIF la ramène à NULL : la comparaison vaut NULL et AUCUNE ligne ne passe.
    /// La RLS échoue donc en fermeture, jamais en ouverture.
    ///
    /// Table `users` volontairement EXCLUE : l'authentification cherche un utilisateur par e-mail
    /// AVANT de connaître son école, et `SchoolId` y est nullable (comptes plateforme). Une policy
    /// naïve sur SchoolId rendrait le login impossible. Son cloisonnement mérite une policy dédiée —
    /// à traiter séparément plutôt que de l'improviser ici.
    /// </summary>
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <summary>
        /// Tables tenant couvertes PAR CETTE migration au moment de sa création — pas la liste de
        /// vérité actuelle. Chaque migration suivante qui introduit une table portant un SchoolId
        /// active sa propre RLS + policy directement dans son propre fichier (voir par ex.
        /// AddFeeInstallmentPlans, AddScheduleAndDisbursements, AddSurveillantEntities…) : ne pas
        /// modifier ce tableau pour une migration déjà appliquée. La couverture réelle est vérifiée
        /// dynamiquement, table par table, par tests/SamaEcole.IntegrationTests/Multitenancy/
        /// RlsCoverageTests.cs, qui échoue en CI si une entité ITenantEntity n'a pas sa RLS+policy —
        /// c'est cette suite, pas ce tableau, la source de vérité.
        /// </summary>
        private static readonly string[] TenantTables = ["students", "subscriptions", "matricule_sequences"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER une ligne pour une
                //               autre école, ou de déplacer une ligne existante vers une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif. Le rôle lui-même est créé HORS migration
            // (docker/postgres/init pour le dev et la CI, playbook d'exploitation en production) :
            // un rôle est un objet de cluster, pas de base, et le créer ici imposerait d'écrire son
            // mot de passe en clair dans le dépôt.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA public TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}';
                        EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas se connecter. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {AppRole}';
                        EXECUTE 'REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {AppRole}';
                        EXECUTE 'REVOKE USAGE ON SCHEMA public FROM {AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260715022137_AddEnrollments.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-E01 — inscriptions : tables `enrollments` et `enrollment_fee_lines`, plus le réglage
    /// `TuitionMonthsPerYear` (nombre de mensualités par an) ajouté aux paramètres d'établissement.
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main (comme pour AddFees) :
    ///   1. La policy RLS des DEUX tables tenant (AGENTS.md règle #2), en plus du Global Query Filter.
    ///   2. Les GRANT du rôle applicatif sur ces tables : SELECT, INSERT, UPDATE — jamais DELETE
    ///      (soft delete, règle #6).
    ///
    /// La colonne `xmin` d'`enrollments` est le jeton de verrou optimiste (règle #5) sur TotalDue :
    /// colonne SYSTÈME de PostgreSQL, aucun DDL généré pour elle.
    ///
    /// La valeur par défaut du nouveau réglage est 9 (la convention sénégalaise : rentrée d'octobre,
    /// fin en juin), synchronisée avec SchoolSettingsDefaults.TuitionMonthsPerYear (Domain).
    /// </summary>
    public partial class AddEnrollments : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TuitionMonthsPerYear",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 9);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_students_SchoolId_Id",
                table: "students",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_school_years_SchoolId_Id",
                table: "school_years",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalDue = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.Id);
                    table.UniqueConstraint("AK_enrollments_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_enrollments_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollment_fee_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Designation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    UnitAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollment_fee_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_fee_categories_SchoolId_FeeCategoryId",
                        columns: x => new { x.SchoolId, x.FeeCategoryId },
                        principalTable: "fee_categories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollment_fee_lines_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_fee_lines_SchoolId_EnrollmentId",
                table: "enrollment_fee_lines",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollment_fee_lines_SchoolId_FeeCategoryId",
                table: "enrollment_fee_lines",
                columns: new[] { "SchoolId", "FeeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_ClassroomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_SchoolYearId",
                table: "enrollments",
                columns: new[] { "SchoolId", "SchoolYearId" });

            migrationBuilder.CreateIndex(
                name: "UX_enrollments_single_active_per_year",
                table: "enrollments",
                columns: new[] { "SchoolId", "StudentId", "SchoolYearId" },
                unique: true,
                filter: "NOT \"IsDeleted\" AND \"Status\" <> 'Cancelled'");

            // --- Isolation multi-tenant (AGENTS.md règle #2) : les DEUX nouvelles tables ---
            foreach (var table in new[] { "enrollments", "enrollment_fee_lines" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'écrire pour une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif sur les nouvelles tables : le GRANT ON ALL TABLES d'une migration
            // antérieure ne couvrait pas des tables qui n'existaient pas encore. Modifiables (statut,
            // correction historisée du montant par le Secrétariat) mais jamais supprimables physiquement
            // (soft delete, règle #6).
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON enrollments TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON enrollment_fee_lines TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux inscriptions.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "enrollment_fee_lines", "enrollments" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "enrollment_fee_lines");

            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_students_SchoolId_Id",
                table: "students");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_school_years_SchoolId_Id",
                table: "school_years");

            migrationBuilder.DropColumn(
                name: "TuitionMonthsPerYear",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260715001255_AddFees.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-F01 — paramétrage des frais : `fee_categories`, `class_fees`, `fee_change_history`.
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main :
    ///
    ///   1. La policy RLS des TROIS tables tenant (AGENTS.md règle #2), ajoutées à la protection déjà
    ///      en place pour students, classrooms, subjects, school_years, etc.
    ///
    ///   2. Les GRANT du rôle applicatif, à la maille de chaque table :
    ///        - fee_categories et class_fees : SELECT, INSERT, UPDATE (pas DELETE — soft delete, règle #6) ;
    ///        - fee_change_history : SELECT, INSERT SEULEMENT. C'est un journal APPEND-ONLY
    ///          (Volume 1 §7.4). On RÉVOQUE d'abord tout — les ALTER DEFAULT PRIVILEGES de
    ///          docker/postgres/init accordent UPDATE/DELETE sur toute table future — puis on ne
    ///          redonne que SELECT et INSERT. Même un bug ou une injection ne peut alors réécrire un
    ///          barème historisé, exactement comme pour user_status_history (JGK-A05).
    ///
    /// La colonne `xmin` de class_fees est le jeton de verrou optimiste (règle #5) : c'est la colonne
    /// SYSTÈME de PostgreSQL, présente dans toute table. Npgsql la reconnaît et n'émet aucun DDL pour
    /// elle malgré sa présence dans le modèle de migration.
    /// </summary>
    public partial class AddFees : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fee_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_categories", x => x.Id);
                    table.UniqueConstraint("AK_fee_categories_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_fee_categories_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_fees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_fees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_fees_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_fees_fee_categories_SchoolId_FeeCategoryId",
                        columns: x => new { x.SchoolId, x.FeeCategoryId },
                        principalTable: "fee_categories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_fees_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_change_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassFeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    NewAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_change_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fee_change_history_class_fees_ClassFeeId",
                        column: x => x.ClassFeeId,
                        principalTable: "class_fees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_ClassroomId",
                table: "class_fees",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId", "ClassroomId", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_categories_SchoolId_Name_IsDeleted",
                table: "fee_categories",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_change_history_ClassFeeId_ChangedAt",
                table: "fee_change_history",
                columns: new[] { "ClassFeeId", "ChangedAt" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) : les TROIS tables ---
            foreach (var table in new[] { "fee_categories", "class_fees", "fee_change_history" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'écrire pour une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif. Le GRANT ON ALL TABLES d'une migration antérieure ne couvrait
            // pas des tables qui n'existaient pas encore.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Barème : modifiable, mais jamais supprimable physiquement (soft delete, règle #6).
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON fee_categories TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON class_fees TO {AppRole}';

                        -- Journal APPEND-ONLY : on repart de zéro (les ALTER DEFAULT PRIVILEGES ont pu
                        -- accorder UPDATE/DELETE) puis on ne redonne que SELECT et INSERT.
                        EXECUTE 'REVOKE ALL ON fee_change_history FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON fee_change_history TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux tables de frais.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "fee_change_history", "class_fees", "fee_categories" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "fee_change_history");

            migrationBuilder.DropTable(
                name: "class_fees");

            migrationBuilder.DropTable(
                name: "fee_categories");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260715031025_AddEnrollmentReceiptNumber.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentReceiptNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "enrollments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_enrollments_receipt_number",
                table: "enrollments",
                columns: new[] { "SchoolId", "ReceiptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_enrollments_receipt_number",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "enrollments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260715192604_AddPayments.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-F02 — caisse : table `payments` (encaissements) + colonne `AmountPaid` sur
    /// `enrollments` (cumul encaissé, le solde = TotalDue − AmountPaid).
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main (comme AddEnrollments / AddFees) :
    ///   1. La policy RLS de la table tenant `payments` (AGENTS.md règle #2), en plus du filtre EF Core.
    ///   2. Les GRANT du rôle applicatif : SELECT, INSERT, UPDATE — jamais DELETE (soft delete, règle #6).
    ///
    /// Aucun verrou optimiste n'est posé sur `payments` : le solde vit sur `enrollments`, dont la colonne
    /// système `xmin` sérialise deux encaissements concurrents (règle #5) — aucun DDL pour elle.
    /// </summary>
    public partial class AddPayments : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                table: "enrollments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("CK_payments_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_payments_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_EnrollmentId",
                table: "payments",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "UX_payments_receipt_number",
                table: "payments",
                columns: new[] { "SchoolId", "ReceiptNumber" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) : la nouvelle table `payments` ---
            migrationBuilder.Sql("ALTER TABLE payments ENABLE ROW LEVEL SECURITY;");

            // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE).
            // WITH CHECK -> lignes autorisées en écriture : interdit d'encaisser pour une autre école.
            migrationBuilder.Sql("""
                CREATE POLICY payments_tenant_isolation ON payments
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Droits du rôle applicatif : encaisser (INSERT) et corriger un statut (UPDATE), jamais
            // supprimer physiquement (soft delete, règle #6). Le GRANT ON ALL TABLES d'une migration
            // antérieure ne couvrait pas `payments`, qui n'existait pas encore.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON payments TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux paiements.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS payments_tenant_isolation ON payments;");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropColumn(
                name: "AmountPaid",
                table: "enrollments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260719012630_AddFinanceDelegationSwitchesToSchoolSettings.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Matrice d'autorisation "Photoshop" — délégation Finance, par école : AllowFinanceToModifyFees
    /// (PUT /finance/fees/{id}) et AllowFinanceToDeleteFees (DELETE /finance/fees/{id} et
    /// /finance/fee-categories/{id}), lues par CanModifyFeesHandler/CanDeleteFeesHandler
    /// (SamaEcole.Web.Authorization). Même gabarit que AddAllowSecretaryToManageGradingToSchoolSettings :
    /// défaut à FALSE au niveau colonne, provision_school_director n'a pas besoin d'être réémise, la
    /// délégation reste fermée tant que le Directeur ne l'active pas explicitement.
    /// </summary>
    public partial class AddFinanceDelegationSwitchesToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToDeleteFees",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToModifyFees",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowFinanceToDeleteFees",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "AllowFinanceToModifyFees",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260722003805_AddSchoolLegalMentionsAndEnrollmentCollection.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Refonte du reçu d'inscription (A5 paysage) : mentions légales de l'en-tête et ventilation de
    /// l'encaissement du jour.
    ///
    /// • <c>schools</c> — e-mail de contact, NINEA et RCCM, imprimés dans l'en-tête du reçu.
    /// • <c>enrollment_fee_lines</c> — part réellement encaissée au guichet et nombre de mois couverts.
    ///   DEFAULT 0 : les inscriptions antérieures valent « rien encaissé à l'inscription », ce qui est
    ///   exact — leurs versements sont passés par la Caisse et ont leurs propres reçus.
    ///
    /// Aucune nouvelle table : les deux tables sont déjà couvertes par leurs policies RLS (AGENTS.md
    /// règle #2), il n'y a donc rien à ajouter à TenantTables.
    /// </summary>
    public partial class AddSchoolLegalMentionsAndEnrollmentCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "schools",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ninea",
                table: "schools",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistreCommerce",
                table: "schools",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountCollected",
                table: "enrollment_fee_lines",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MonthsCollected",
                table: "enrollment_fee_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "Ninea",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "RegistreCommerce",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "AmountCollected",
                table: "enrollment_fee_lines");

            migrationBuilder.DropColumn(
                name: "MonthsCollected",
                table: "enrollment_fee_lines");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260722125334_AddSignaturesToSchoolSettings.cs`

````csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ajout des URLs d'images pour la signature du directeur, du caissier et du cachet officiel
    /// (SchoolSettings) pour injection dynamique dans les reçus et bulletins.
    /// </summary>
    public partial class AddSignaturesToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashierSignatureUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DirectorSignatureUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialStampUrl",
                table: "school_settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashierSignatureUrl",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "DirectorSignatureUrl",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "OfficialStampUrl",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260723170307_AddModuleCaisse.cs`

````csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleCaisse : Migration
    {
        private static readonly string[] TenantTables = ["cashier_sessions", "payment_breakdowns"];
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CashierSessionId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReferencePeriod",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashier_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cashier_sessions", x => x.Id);
                    table.UniqueConstraint("AK_cashier_sessions_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_cashier_sessions_users_CashierId",
                        column: x => x.CashierId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_breakdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountAllocated = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_breakdowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_breakdowns_fee_categories_FeeCategoryId",
                        column: x => x.FeeCategoryId,
                        principalTable: "fee_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_breakdowns_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CashierSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CashierSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_cashier_sessions_CashierId",
                table: "cashier_sessions",
                column: "CashierId");

            migrationBuilder.CreateIndex(
                name: "UX_cashier_sessions_SchoolId_CashierId_Open",
                table: "cashier_sessions",
                columns: new[] { "SchoolId", "CashierId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_payment_breakdowns_FeeCategoryId",
                table: "payment_breakdowns",
                column: "FeeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_breakdowns_PaymentId",
                table: "payment_breakdowns",
                column: "PaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_payments_cashier_sessions_SchoolId_CashierSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CashierSessionId" },
                principalTable: "cashier_sessions",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON {table} TO {AppRole}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {table}.', '{AppRole}';
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropForeignKey(
                name: "FK_payments_cashier_sessions_SchoolId_CashierSessionId",
                table: "payments");

            migrationBuilder.DropTable(
                name: "cashier_sessions");

            migrationBuilder.DropTable(
                name: "payment_breakdowns");

            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CashierSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CashierSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReferencePeriod",
                table: "payments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260724014300_AddScheduleAndDisbursements.cs`

````csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAndDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Disbursements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Beneficiary = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ReceiptUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disbursements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsTeacherSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_classrooms_ClassroomId",
                        column: x => x.ClassroomId,
                        principalTable: "classrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleSlots_teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disbursements_SchoolId_Date",
                table: "Disbursements",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_ClassroomId",
                table: "ScheduleSlots",
                column: "ClassroomId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SchoolId_ClassroomId",
                table: "ScheduleSlots",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SchoolId_TeacherId",
                table: "ScheduleSlots",
                columns: new[] { "SchoolId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_SubjectId",
                table: "ScheduleSlots",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSlots_TeacherId",
                table: "ScheduleSlots",
                column: "TeacherId");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            string[] tenantTables = ["Disbursements", "ScheduleSlots"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON "{table}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($"""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sama_ecole_app') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON "{table}" TO sama_ecole_app';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {table}.', 'sama_ecole_app';
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["Disbursements", "ScheduleSlots"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "Disbursements");

            migrationBuilder.DropTable(
                name: "ScheduleSlots");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260724182209_AddPayrollAndTax.cs`

````csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAndTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_contracts_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_contracts_teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_contracts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxe_declarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TotalIpres = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCss = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalVrs = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalBrs = table.Column<decimal>(type: "numeric", nullable: false),
                    TvaCollected = table.Column<decimal>(type: "numeric", nullable: false),
                    TvaDeductible = table.Column<decimal>(type: "numeric", nullable: false),
                    NetTva = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalDueToState = table.Column<decimal>(type: "numeric", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_taxe_declarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_taxe_declarations_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiche_paies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric", nullable: false),
                    GrossSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    IpresEmployee = table.Column<decimal>(type: "numeric", nullable: false),
                    IpresEmployer = table.Column<decimal>(type: "numeric", nullable: false),
                    CssEmployer = table.Column<decimal>(type: "numeric", nullable: false),
                    Vrs = table.Column<decimal>(type: "numeric", nullable: false),
                    Brs = table.Column<decimal>(type: "numeric", nullable: false),
                    NetSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiche_paies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiche_paies_employee_contracts_EmployeeContractId",
                        column: x => x.EmployeeContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiche_paies_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_SchoolId",
                table: "employee_contracts",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts",
                column: "TeacherId",
                unique: true,
                filter: "\"TeacherId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_EmployeeContractId",
                table: "fiche_paies",
                column: "EmployeeContractId");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId",
                table: "fiche_paies",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_taxe_declarations_SchoolId",
                table: "taxe_declarations",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_taxe_declarations_SchoolId_Month_Year",
                table: "taxe_declarations",
                columns: new[] { "SchoolId", "Month", "Year" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            string[] tenantTables = ["employee_contracts", "fiche_paies", "taxe_declarations"];
            string appRole = "sama_ecole_app";
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON "{{table}}" TO {{appRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{appRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["employee_contracts", "fiche_paies", "taxe_declarations"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "fiche_paies");

            migrationBuilder.DropTable(
                name: "taxe_declarations");

            migrationBuilder.DropTable(
                name: "employee_contracts");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260726022144_AddDocumentsModuleEntities.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentsModuleEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EarlyDepartures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DepartureTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PickedUpBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarlyDepartures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EarlyDepartures_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialCommitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Terms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialCommitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialCommitments_enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParentSummons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentSummons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentSummons_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherHourRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherHourRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeacherHourRecords_employee_contracts_EmployeeContractId",
                        column: x => x.EmployeeContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyDepartures_SchoolId_StudentId",
                table: "EarlyDepartures",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyDepartures_StudentId",
                table: "EarlyDepartures",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCommitments_EnrollmentId",
                table: "FinancialCommitments",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCommitments_SchoolId_EnrollmentId",
                table: "FinancialCommitments",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentSummons_SchoolId_StudentId",
                table: "ParentSummons",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentSummons_StudentId",
                table: "ParentSummons",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherHourRecords_EmployeeContractId",
                table: "TeacherHourRecords",
                column: "EmployeeContractId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherHourRecords_SchoolId_EmployeeContractId",
                table: "TeacherHourRecords",
                columns: new[] { "SchoolId", "EmployeeContractId" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddSurveillantEntities. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si l'une d'elles manque.
            string[] tenantTables = ["EarlyDepartures", "ParentSummons", "FinancialCommitments", "TeacherHourRecords"];
            string appRole = "sama_ecole_app";
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{appRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{appRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["EarlyDepartures", "ParentSummons", "FinancialCommitments", "TeacherHourRecords"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "EarlyDepartures");

            migrationBuilder.DropTable(
                name: "FinancialCommitments");

            migrationBuilder.DropTable(
                name: "ParentSummons");

            migrationBuilder.DropTable(
                name: "TeacherHourRecords");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260726031324_AddVatFieldsToPaymentsAndDisbursements.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVatFieldsToPaymentsAndDisbursements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "payments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "payments",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Disbursements",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Disbursements",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_vat_rate_range",
                table: "payments",
                sql: "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_disbursements_vat_rate_range",
                table: "Disbursements",
                sql: "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_vat_rate_range",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_disbursements_vat_rate_range",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Disbursements");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260727033446_AddSmsNotifications.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SmsCreditBalance",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnAttendanceAlert",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnDuesReminder",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmsOnPaymentReceipt",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "sms_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SegmentCount = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sms_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sms_messages_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sms_messages_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_SchoolId_SentAt",
                table: "sms_messages",
                columns: new[] { "SchoolId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sms_messages_SchoolId_StudentId",
                table: "sms_messages",
                columns: new[] { "SchoolId", "StudentId" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddBuildingsAndRooms. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si celle-ci manque.
            //
            // `school_settings` est déjà sous policy depuis AddSchoolSettings : les quatre colonnes
            // ajoutées ci-dessus en héritent, rien à poser de plus pour elles.
            const string appRole = "sama_ecole_app";

            migrationBuilder.Sql("ALTER TABLE \"sms_messages\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("""
                CREATE POLICY sms_messages_tenant_isolation ON "sms_messages"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // SELECT/INSERT/UPDATE uniquement — aucun DELETE : le soft delete n'émet jamais de SQL
            // DELETE (AGENTS.md règle #6).
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "sms_messages" TO {{appRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table sms_messages.', '{{appRole}}';
                    END IF;
                END
                $inner$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sms_messages");

            migrationBuilder.DropColumn(
                name: "SmsCreditBalance",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnAttendanceAlert",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnDuesReminder",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "SmsOnPaymentReceipt",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260727155334_AddFeeInstallmentPlans.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeInstallmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DebtorReminderThresholdDays",
                table: "school_settings",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.CreateTable(
                name: "debtor_reminder_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debtor_reminder_batches", x => x.Id);
                    table.UniqueConstraint("AK_debtor_reminder_batches_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batches_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batches_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_installment_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedFromClassroomTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_installment_plans", x => x.Id);
                    table.UniqueConstraint("AK_fee_installment_plans_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_fee_installment_plans_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fee_installment_plans_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debtor_reminder_batch_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    DebtorReminderBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuardianPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RemainingBalance = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DaysOverdue = table.Column<int>(type: "integer", nullable: false),
                    SmsMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debtor_reminder_batch_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_debtor_reminder_batches_SchoolI~",
                        columns: x => new { x.SchoolId, x.DebtorReminderBatchId },
                        principalTable: "debtor_reminder_batches",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_enrollments_SchoolId_Enrollment~",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_debtor_reminder_batch_items_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_installments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeInstallmentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNo = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_installments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fee_installments_fee_installment_plans_SchoolId_FeeInstallm~",
                        columns: x => new { x.SchoolId, x.FeeInstallmentPlanId },
                        principalTable: "fee_installment_plans",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fee_installments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batch_items_SchoolId_DebtorReminderBatchId",
                table: "debtor_reminder_batch_items",
                columns: new[] { "SchoolId", "DebtorReminderBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batch_items_SchoolId_EnrollmentId",
                table: "debtor_reminder_batch_items",
                columns: new[] { "SchoolId", "EnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batches_SchoolId_ClassroomId",
                table: "debtor_reminder_batches",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_debtor_reminder_batches_SchoolId_Status_GeneratedAt",
                table: "debtor_reminder_batches",
                columns: new[] { "SchoolId", "Status", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_fee_installment_plans_single_active_per_enrollment",
                table: "fee_installment_plans",
                columns: new[] { "SchoolId", "EnrollmentId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_fee_installments_SchoolId_FeeInstallmentPlanId_SequenceNo",
                table: "fee_installments",
                columns: new[] { "SchoolId", "FeeInstallmentPlanId", "SequenceNo" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère JAMAIS ces policies : elles s'écrivent à la main, comme dans
            // AddBuildingsAndRooms. Une table tenant protégée par le seul filtre EF fuit dès la
            // première requête SQL brute — RlsCoverageTests échoue si l'une d'elles manque.
            string[] tenantTables =
            [
                "fee_installment_plans", "fee_installments",
                "debtor_reminder_batches", "debtor_reminder_batch_items"
            ];
            string appRole = "sama_ecole_app";
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // SELECT/INSERT/UPDATE uniquement — aucun DELETE : le soft delete n'émet jamais de SQL
                // DELETE (AGENTS.md règle #6).
                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{appRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{appRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables =
            [
                "fee_installment_plans", "fee_installments",
                "debtor_reminder_batches", "debtor_reminder_batch_items"
            ];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "debtor_reminder_batch_items");

            migrationBuilder.DropTable(
                name: "fee_installments");

            migrationBuilder.DropTable(
                name: "debtor_reminder_batches");

            migrationBuilder.DropTable(
                name: "fee_installment_plans");

            migrationBuilder.DropColumn(
                name: "DebtorReminderThresholdDays",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260727184149_AddEmployeeContractLifecycle.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket Volume 1 §14.1 — clôture et modification d'un contrat, avec historique. `EndDate` marque
    /// une clôture définitive (plus aucune fiche de paie générable, voir GenerateFichePaieCommandHandler) ;
    /// les index uniques sur TeacherId/UserId sont étendus à "EndDate IS NULL" pour qu'un contrat clôturé
    /// n'empêche plus la reprise de la même personne. `employee_contract_histories` est APPEND-ONLY,
    /// même raisonnement que user_status_history/fee_change_histories : le rôle applicatif ne reçoit que
    /// SELECT/INSERT, jamais UPDATE ni DELETE — un historique de rémunération doit être opposable.
    /// </summary>
    public partial class AddEmployeeContractLifecycle : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "employee_contracts",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_contract_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousBaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    PreviousHourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    PreviousTransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    NewBaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    NewHourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    NewTransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_contract_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_contract_histories_employee_contracts_EmployeeCont~",
                        column: x => x.EmployeeContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts",
                column: "TeacherId",
                unique: true,
                filter: "\"TeacherId\" IS NOT NULL AND \"EndDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND \"EndDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contract_histories_EmployeeContractId_ChangedAt",
                table: "employee_contract_histories",
                columns: new[] { "EmployeeContractId", "ChangedAt" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) : écrite à la main, comme dans
            // AddFeeInstallmentPlans — EF ne génère jamais ces policies. RlsCoverageTests échoue si
            // elle manque.
            migrationBuilder.Sql("ALTER TABLE employee_contract_histories ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY employee_contract_histories_tenant_isolation ON employee_contract_histories
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- On repart de zéro : les ALTER DEFAULT PRIVILEGES ont pu accorder UPDATE/DELETE.
                        EXECUTE 'REVOKE ALL ON employee_contract_histories FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON employee_contract_histories TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''historique des contrats sera inaccessible à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS employee_contract_histories_tenant_isolation ON employee_contract_histories;");

            migrationBuilder.DropTable(
                name: "employee_contract_histories");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "employee_contracts");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts",
                column: "TeacherId",
                unique: true,
                filter: "\"TeacherId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260825153953_AddPaymentBreakdownLabel.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentBreakdownLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "payment_breakdowns",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "payment_breakdowns");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260826135319_AddPaymentIdempotencyKey.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_payments_idempotency_key",
                table: "payments",
                columns: new[] { "SchoolId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_payments_idempotency_key",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "payments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260829205445_AddCashierSessionDiscrepancy.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierSessionDiscrepancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualCashAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscrepancyAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscrepancyReason",
                table: "cashier_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCashAmount",
                table: "cashier_sessions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualCashAmount",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "DiscrepancyAmount",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "DiscrepancyReason",
                table: "cashier_sessions");

            migrationBuilder.DropColumn(
                name: "ExpectedCashAmount",
                table: "cashier_sessions");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260918190305_AddInternatBoarding.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternatBoarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBoardingFee",
                table: "fee_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BoardingStatus",
                table: "enrollments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Externe");

            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "enrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_rooms_SchoolId_Id",
                table: "rooms",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" });

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments",
                columns: new[] { "SchoolId", "RoomId" },
                principalTable: "rooms",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_rooms_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_rooms_SchoolId_Id",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_SchoolId_RoomId",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "IsBoardingFee",
                table: "fee_categories");

            migrationBuilder.DropColumn(
                name: "BoardingStatus",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "enrollments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20260930105018_AddFeeCategoryIsOptional.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeCategoryIsOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOptional",
                table: "fee_categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOptional",
                table: "fee_categories");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261004034202_AddDisbursementReversal.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDisbursementReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfId",
                table: "Disbursements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disbursements_ReversalOfId",
                table: "Disbursements",
                column: "ReversalOfId",
                unique: true,
                filter: "\"ReversalOfId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_disbursements_reversal_negative",
                table: "Disbursements",
                sql: "\"ReversalOfId\" IS NULL OR \"Amount\" < 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Disbursements_Disbursements_ReversalOfId",
                table: "Disbursements",
                column: "ReversalOfId",
                principalTable: "Disbursements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Disbursements_Disbursements_ReversalOfId",
                table: "Disbursements");

            migrationBuilder.DropIndex(
                name: "IX_Disbursements_ReversalOfId",
                table: "Disbursements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_disbursements_reversal_negative",
                table: "Disbursements");

            migrationBuilder.DropColumn(
                name: "ReversalOfId",
                table: "Disbursements");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261004034236_RevokeDeleteOnBusinessTables.cs`

````csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Règle #6 d'AGENTS.md tenue par la BASE : le rôle applicatif ne peut physiquement supprimer AUCUNE ligne
    /// métier (conception soft delete 2026-10-01 §3.1/§3.3). L'audit a montré des DELETE résiduels hérités du
    /// premier <c>GRANT ... ON ALL TABLES</c> et des <c>ALTER DEFAULT PRIVILEGES</c> de l'environnement, dont des
    /// registres financiers (<c>fiche_paies</c>, <c>taxe_declarations</c>, <c>Disbursements</c>).
    ///
    /// L'historique des migrations (<c>__EFMigrationsHistory</c>) est inclus : les migrations s'exécutent sous le
    /// rôle propriétaire, l'application n'a aucune raison de pouvoir en effacer une ligne.
    ///
    /// Les purges légitimes (« remise à neuf » d'une école, suppression d'année en mode test) passent par des
    /// fonctions <c>SECURITY DEFINER</c> exécutées sous le rôle propriétaire : elles n'ont besoin d'aucun droit
    /// du rôle applicatif.
    ///
    /// Révocation DYNAMIQUE sur toutes les tables de <c>public</c> sauf la liste blanche ci-dessous : elle couvre
    /// aussi des tables que seuls les privilèges par défaut d'un environnement auraient rendues supprimables.
    /// La liste blanche ne contient que des tables TECHNIQUES, sans parcours de corbeille ni valeur comptable.
    /// Un test d'intégration échoue si une table hors liste blanche redevient supprimable.
    ///
    /// Retour arrière : correctif en avant privilégié. Le <c>Down</c> ne ré-accorde volontairement rien, pour ne
    /// pas rétablir un droit de suppression physique sur des registres financiers.
    /// </summary>
    public partial class RevokeDeleteOnBusinessTables : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <summary>Tables techniques où le rôle applicatif garde DELETE (jetons éphémères, compteur de séquence).</summary>
        public static readonly string[] TechnicalTablesKeepingDelete =
            ["refresh_tokens", "password_reset_tokens", "matricule_sequences"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var keep = string.Join(", ", TechnicalTablesKeepingDelete.Select(t => $"'{t}'"));

            migrationBuilder.Sql($$"""
                DO $$
                DECLARE t record;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        RAISE WARNING 'Rôle % absent : aucun privilège à révoquer.', '{{AppRole}}';
                        RETURN;
                    END IF;

                    FOR t IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public'
                          AND c.relkind IN ('r', 'p')
                          AND c.relname NOT IN ({{keep}})
                    LOOP
                        EXECUTE format('REVOKE DELETE ON public.%I FROM {{AppRole}}', t.relname);
                    END LOOP;

                    -- Les tables créées PLUS TARD par ce rôle propriétaire ne doivent plus hériter de DELETE.
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE DELETE ON TABLES FROM {{AppRole}};
                END
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement vide : ne jamais rétablir un droit de suppression physique (roll-forward).
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261018004711_AddPaymentCancellation.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationOutcome",
                table: "payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancellationSessionId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CancellationSessionId",
                table: "payments",
                columns: new[] { "SchoolId", "CancellationSessionId" },
                filter: "\"CancellationSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payments_SchoolId_CancelledAt",
                table: "payments",
                columns: new[] { "SchoolId", "CancelledAt" },
                filter: "\"CancelledAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CancellationSessionId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_SchoolId_CancelledAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationOutcome",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancellationSessionId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "payments");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261019145841_AddAllowFinanceToCancelPaymentsToSchoolSettings.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowFinanceToCancelPaymentsToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToCancelPayments",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowFinanceToCancelPayments",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261102103254_AddPayslipCancellationPaymentAndExternalStaff.cs`

````csharp
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayslipCancellationPaymentAndExternalStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "fiche_paies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "fiche_paies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisbursementId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "fiche_paies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidWith",
                table: "fiche_paies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByFichePaieId",
                table: "fiche_paies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalFullName",
                table: "employee_contracts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalJobTitle",
                table: "employee_contracts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPhone",
                table: "employee_contracts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true,
                filter: "\"CancelledAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "DisbursementId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "PaidWith",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "ReplacedByFichePaieId",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "ExternalFullName",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "ExternalJobTitle",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "ExternalPhone",
                table: "employee_contracts");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true);
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261102112555_AddAllowFinanceToCancelPayslipsToSchoolSettings.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowFinanceToCancelPayslipsToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFinanceToCancelPayslips",
                table: "school_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowFinanceToCancelPayslips",
                table: "school_settings");
        }
    }
}
````


#### `src/SamaEcole.Persistence/Migrations/20261103010545_AddPayrollWithoutDeductions.cs`

````csharp
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollWithoutDeductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeductionsApplied",
                table: "fiche_paies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ApplyStatutoryDeductions",
                table: "employee_contracts",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeductionsApplied",
                table: "fiche_paies");

            migrationBuilder.DropColumn(
                name: "ApplyStatutoryDeductions",
                table: "employee_contracts");
        }
    }
}
````

