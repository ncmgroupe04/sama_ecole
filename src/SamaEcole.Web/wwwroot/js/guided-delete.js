/**
 * Suppression guidée (spec 2026-10-17, §5) — logique PARTAGÉE par tous les écrans qui suppriment un élément.
 * Modal : Views/Shared/_GuidedDeleteModal.cshtml. Trois appels distincts, jamais fusionnés côté serveur :
 *   GET    /{resource}/{id}/deletion-impact      → analyse (lecture seule)
 *   POST   /{resource}/{id}/unlink-dependencies  → rupture explicite des liens non financiers
 *   DELETE /{resource}/{id}?rowVersion=          → suppression (ne rompt jamais rien)
 *
 * Usage dans un composant Alpine :
 *     Alpine.data('x', () => ({
 *         ...window.guidedDelete.mixin({ resource: 'students' }),
 *         async afterGuidedDelete() { … }   // à fournir : relire la liste, annoncer la suppression
 *     }))
 *
 * Phases de `guidedDelete.phase` : 'loading' (analyse), 'financial' (blocage : aucune option de rupture),
 * 'links' (liens à rompre, case de confirmation obligatoire), 'ready' (aucun lien : confirmation simple).
 */
(function () {
    window.guidedDelete = {
        mixin(config) {
            const { resource } = config;

            return {
                guidedDelete: null, // { id, name, rowVersion, phase, impact, confirmed, error, busy }

                async openGuidedDelete({ id, name, rowVersion }) {
                    this.guidedDelete = { id, name, rowVersion, phase: 'loading', impact: null, confirmed: false, error: null, busy: false };
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
                        const impact = await window.api.get(`/${resource}/${state.id}/deletion-impact`);
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
                            await window.api.post(`/${resource}/${state.id}/unlink-dependencies`, {});
                        }
                        await window.api.delete(`/${resource}/${state.id}?rowVersion=${state.rowVersion}`);
                        this.guidedDelete = null;
                        await this.afterGuidedDelete();
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
