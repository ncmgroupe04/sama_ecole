/**
 * Suppression guidée (spec 2026-10-17, §5) — logique PARTAGÉE par tous les écrans qui suppriment un élément.
 * Modal : Views/Shared/_GuidedDeleteModal.cshtml. Trois appels distincts, jamais fusionnés côté serveur :
 *   GET    /{resource}/{id}/deletion-impact      → analyse (lecture seule)
 *   POST   /{resource}/{id}/unlink-dependencies  → rupture explicite des liens non financiers
 *   DELETE /{resource}/{id}?rowVersion=          → suppression (ne rompt jamais rien)
 *
 * Usage dans un composant Alpine :
 *     Alpine.data('x', () => ({
 *         ...window.guidedDelete.mixin({
 *             resource: 'students',   // ressource par défaut ; surchargeable à l'ouverture (plusieurs éléments par page)
 *             financialHint: '…',   // que faire quand une donnée financière bloque (optionnel)
 *             readyText: '…'        // phrase de confirmation simple (optionnel)
 *         }),
 *         async afterGuidedDelete(target) { … }   // à fournir : relire la liste, annoncer ; target = { id, name, rowVersion }
 *     }))
 *
 * Phases de `guidedDelete.phase` : 'loading' (analyse), 'financial' (blocage : aucune option de rupture),
 * 'links' (liens à rompre, case de confirmation obligatoire), 'ready' (aucun lien : confirmation simple).
 */
(function () {
    window.guidedDelete = {
        mixin(config) {
            const defaultTexts = () => ({
                financialHint: config.financialHint
                    || "Une donnée financière enregistrée interdit la suppression : traitez-la d'abord, la suppression sera alors possible.",
                readyText: config.readyText
                    || "Cet élément sera supprimé et n'apparaîtra plus nulle part. Une trace de la suppression est conservée."
            });

            return {
                guidedDelete: null, // { id, name, rowVersion, phase, impact, confirmed, error, busy }
                guidedDeleteTexts: defaultTexts(),

                /**
                 * Ouvre la fenêtre. `resource`, `financialHint` et `readyText` surchargent la configuration pour CET élément (une page
                 * qui supprime plusieurs types d'éléments : bâtiments et salles, biens et catégories…). `rowVersion` peut être absent
                 * (certaines suppressions n'ont pas de jeton de concurrence) : aucun paramètre n'est alors envoyé.
                 */
                async openGuidedDelete({ id, name, rowVersion, resource, financialHint, readyText }) {
                    this.guidedDelete = {
                        id, name, rowVersion, resource: resource || config.resource,
                        phase: 'loading', impact: null, confirmed: false, error: null, busy: false
                    };
                    const texts = defaultTexts();
                    this.guidedDeleteTexts = { financialHint: financialHint || texts.financialHint, readyText: readyText || texts.readyText };
                    await this.refreshGuidedDeleteImpact();
                },

                closeGuidedDelete() {
                    this.guidedDelete = null;
                },

                /** Vrai quand tous les liens listés peuvent être rompus ici (sinon l'utilisateur doit d'abord les traiter ailleurs). */
                guidedDeleteAllBreakable() {
                    const impact = this.guidedDelete && this.guidedDelete.impact;
                    return !!impact && impact.links.every((link) => link.breakable);
                },

                /** Relit l'analyse serveur et choisit la phase d'affichage. */
                async refreshGuidedDeleteImpact() {
                    const state = this.guidedDelete;
                    if (!state) return;
                    try {
                        const impact = await window.api.get(`/${state.resource}/${state.id}/deletion-impact`);
                        state.impact = impact;
                        state.phase = impact.financialBlock ? 'financial' : impact.canDelete ? 'ready' : 'links';
                        state.error = null;
                    } catch (err) {
                        state.phase = 'links';
                        state.impact = state.impact || { links: [] };
                        state.error = (err && err.message) || "Impossible d'analyser les liens.";
                    }
                },

                /** Rupture des liens PUIS suppression : deux appels serveur distincts, après la confirmation cochée par l'utilisateur. */
                async confirmGuidedDelete() {
                    const state = this.guidedDelete;
                    if (!state || state.busy) return;
                    // Blocage financier ou analyse en cours : rien à rompre ni à supprimer (le serveur refuserait de toute façon).
                    if (state.phase === 'financial' || state.phase === 'loading') return;

                    if (state.phase === 'links' && !state.confirmed) {
                        state.error = 'Cochez la case pour confirmer la rupture de ces liens.';
                        return;
                    }

                    state.busy = true;
                    state.error = null;
                    try {
                        if (state.phase === 'links') {
                            await window.api.post(`/${state.resource}/${state.id}/unlink-dependencies`, {});
                        }
                        const query = state.rowVersion === undefined || state.rowVersion === null ? '' : `?rowVersion=${state.rowVersion}`;
                        await window.api.delete(`/${state.resource}/${state.id}${query}`);
                        const target = { id: state.id, name: state.name, rowVersion: state.rowVersion, resource: state.resource };
                        this.guidedDelete = null;
                        await this.afterGuidedDelete(target);
                    } catch (err) {
                        const message = (err && err.message) || 'Erreur lors de la suppression.';
                        if (err && err.code === 'CONCURRENCY_CONFLICT') {
                            state.error = "Cette fiche vient d'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                        } else {
                            // Un lien ou un paiement est apparu entre-temps : on réaffiche l'analyse à jour, message serveur inclus.
                            state.confirmed = false;
                            await this.refreshGuidedDeleteImpact();
                            if (this.guidedDelete) this.guidedDelete.error = message;
                        }
                    } finally {
                        if (this.guidedDelete) this.guidedDelete.busy = false;
                    }
                }
            };
        }
    };
})();
