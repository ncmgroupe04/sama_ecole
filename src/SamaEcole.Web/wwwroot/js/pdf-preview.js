/**
 * Aperçu PDF partagé — moteur unique de TOUTES les modales de prévisualisation (reçus, bulletins,
 * attestations, billets, sommations…).
 *
 * POURQUOI CE FICHIER EXISTE
 * --------------------------
 * L'affichage est délégué à la VISIONNEUSE PDF NATIVE du navigateur (Chrome/Edge/Firefox), montée
 * dans un `<iframe>` à l'intérieur de la modale. Le rendu `<canvas>` via PDF.js a été retiré : il
 * ajoutait ~1,8 Mo de dépendances, sa propre gestion d'échelle/pagination/impression, et échouait
 * quand même dans certains environnements (gestionnaire de téléchargement qui coupe le fetch).
 * L'iframe natif affiche le document avec ses propres commandes de zoom, d'impression et de
 * téléchargement, et gère seul l'adaptation à la largeur — aucun document ne déborde.
 *
 * DEUX POINTS NON ÉVIDENTS
 * -----------------------
 * 1. L'iframe pointe sur une **blob URL locale**, jamais sur l'URL de l'endpoint : on ne peut pas
 *    poser l'en-tête `Authorization: Bearer` sur une navigation d'iframe. On récupère donc d'abord
 *    les octets par `fetch()` (en-tête d'auth + `X-Pdf-Preview: 1`), on en fait un Blob typé
 *    `application/pdf`, et l'iframe charge ce blob. Bonus : une blob URL n'est pas un téléchargement
 *    HTTP, un gestionnaire de téléchargement (IDM & co.) ne peut pas l'intercepter.
 * 2. `<object data=…>` serait équivalent mais la CSP du projet pose `object-src 'none'` (JGK-F01) —
 *    le navigateur le bloque. Seul `<iframe>` passe (`frame-src 'self' blob:`).
 *
 * FILET DE SÉCURITÉ
 * ----------------
 * `fetchPdfBlob` vérifie AVANT affichage : HTTP OK, corps non vide, signature `%PDF-`. Chaque cause
 * a son message. Si l'iframe ne signale pas `load` sous quelques secondes (navigateur sans
 * visionneuse PDF intégrée, très rare), une astuce « ouvrez dans un nouvel onglet » apparaît — la
 * modale ne se bloque jamais sur un cadre blanc.
 *
 * USAGE — un composant Alpine réutilise l'ensemble en étalant `window.pdfPreview.state()` :
 * <code>
 * Alpine.data('students', () => ({
 *     ...window.pdfPreview.state(),
 *     openReceipt(id) { this.openPdfPreview(`/api/v1/…/${id}/pdf`, 'Reçu', 'Recu.pdf'); }
 * }));
 * </code>
 * puis, dans la vue : <code>@await Html.PartialAsync("_PdfPreviewModal")</code>.
 */
(function () {
    'use strict';

    /** Délai après lequel, sans événement `load` de l'iframe, on suggère l'ouverture en nouvel onglet. */
    const VIEWER_LOAD_HINT_MS = 3500;

    /** Vrai si les 5 premiers octets sont la signature « %PDF- » d'un fichier PDF. */
    async function looksLikePdf(blob) {
        const head = new Uint8Array(await blob.slice(0, 5).arrayBuffer());
        return head[0] === 0x25 && head[1] === 0x50 && head[2] === 0x44 && head[3] === 0x46 && head[4] === 0x2d;
    }

    /**
     * Récupère le document et vérifie qu'il s'agit bien d'un PDF exploitable AVANT de le confier à
     * l'iframe — chaque vérification correspond à une panne réellement observée en production.
     */
    async function fetchPdfBlob(url, requestInit) {
        if (!url || url.includes('undefined') || url.includes('null')) {
            throw new Error(`L'identifiant du document est invalide — l'aperçu ne peut pas être demandé (${url}).`);
        }

        // Un jeton périmé produirait un 401 traduit en « aperçu indisponible » : on le renouvelle avant.
        if (window.auth?.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await window.api.refreshOrRedirect();
        }

        // requestInit permet un aperçu servi par une route POST à corps JSON (ex. bulletin de notes,
        // POST /report-cards/generate) : method/body/headers viennent de l'appelant, l'en-tête
        // Authorization reste géré ici.
        //
        // X-Pdf-Preview: 1 — signale au serveur que ce téléchargement alimente l'aperçu intégré, pas
        // un enregistrement de fichier. Le serveur renvoie alors les octets en `application/octet-stream`
        // `inline` : un gestionnaire de téléchargement (Internet Download Manager, extension « grab »,
        // mode « télécharger les PDF » du navigateur) n'y voit plus un fichier PDF et cesse d'intercepter
        // le fetch — sans quoi il coupe la requête de la page et propose un enregistrement à la place.
        let response;
        try {
            response = await fetch(url, {
                method: (requestInit && requestInit.method) || 'GET',
                headers: {
                    Authorization: `Bearer ${window.auth?.accessToken}`,
                    ...(requestInit && requestInit.headers),
                    'X-Pdf-Preview': '1'
                },
                body: requestInit && requestInit.body,
                credentials: 'same-origin'
            });
        } catch {
            throw new Error('Connexion interrompue pendant le téléchargement du document. Vérifiez votre connexion, puis réessayez.');
        }

        if (!response.ok) {
            const detail = await readErrorMessage(response);
            throw new Error(`Le serveur a refusé la génération du document (erreur ${response.status})${detail ? ` : ${detail}` : '.'}`);
        }

        const blob = await response.blob();
        if (!blob || blob.size === 0) {
            throw new Error('Le document généré par le serveur est vide (0 octet). Réessayez ; si le problème persiste, signalez-le.');
        }

        // Un 200 qui ne transporte pas un PDF signale une erreur applicative passée à travers les
        // mailles du filet (page HTML de session expirée, corps JSON d'erreur). On teste la signature
        // « %PDF- » des octets reçus plutôt que l'en-tête Content-Type : depuis X-Pdf-Preview, le
        // serveur répond volontairement en `application/octet-stream`, et la signature est de toute
        // façon une vérification plus fiable qu'un en-tête déclaratif.
        if (!(await looksLikePdf(blob))) {
            const detail = (await blob.text().catch(() => '')).slice(0, 200);
            throw new Error(`Le serveur n'a pas renvoyé un PDF valide${detail ? ` : ${detail}` : '.'}`);
        }

        // Le type MIME est réaffirmé côté client : c'est lui qui fait que l'iframe, le nouvel onglet et
        // l'impression traitent la blob URL comme un PDF (et non comme un téléchargement binaire).
        return new Blob([blob], { type: 'application/pdf' });
    }

    /** Extrait le message le plus lisible d'une réponse d'erreur (format normalisé Volume 4 §0.4). */
    async function readErrorMessage(response) {
        const text = await response.text().catch(() => '');
        if (!text) return '';
        try {
            const payload = JSON.parse(text);
            return payload.message || payload.detail || payload.title || '';
        } catch {
            return text.slice(0, 200);
        }
    }

    window.pdfPreview = {
        /**
         * Fabrique l'état + les méthodes d'aperçu d'UN composant Alpine. Le Blob et le jeton de la
         * dernière demande vivent dans la fermeture ; seules des primitives traversent la réactivité.
         */
        state() {
            let blob = null;         // Blob source, conservé pour imprimer / télécharger / ouvrir
            let lastRequest = null;  // { url, title, downloadName, requestInit } pour « Réessayer »
            let loadHintTimer = null;

            /** Libère Blob URL et minuteur. Appelé à la fermeture et avant tout rechargement. */
            function releaseDocument(component) {
                if (loadHintTimer) {
                    clearTimeout(loadHintTimer);
                    loadHintTimer = null;
                }
                blob = null;
                if (component.pdfPreviewUrl) {
                    URL.revokeObjectURL(component.pdfPreviewUrl);
                    component.pdfPreviewUrl = null;
                }
            }

            return {
                // ----------------------------------------------------------------- état (réactif)
                showPdfModal: false,
                pdfPreviewTitle: '',
                pdfDownloadName: 'document.pdf',
                /** 'idle' | 'loading' | 'ready' | 'error' */
                pdfStatus: 'idle',
                pdfErrorMessage: null,
                /** blob: URL — src de l'iframe, cible du téléchargement et du « nouvel onglet ». */
                pdfPreviewUrl: null,
                /** Vrai quand l'iframe n'a pas signalé `load` : on suggère alors le nouvel onglet. */
                pdfViewerHint: false,

                // --------------------------------------------------------------------- ouverture
                /**
                 * Ouvre la modale et affiche `url`. La modale s'ouvre TOUJOURS, y compris en erreur :
                 * l'utilisateur doit pouvoir lire la cause et cliquer « Réessayer ».
                 */
                async openPdfPreview(url, title, downloadName, requestInit) {
                    lastRequest = { url, title, downloadName, requestInit };

                    // Un aperçu est presque toujours ouvert DEPUIS une autre modale (fiche élève, reçu,
                    // tableau des paiements) : sans cette fermeture, les deux `modal-shell` s'empilent.
                    window.closeAllModals?.();

                    releaseDocument(this);
                    this.pdfPreviewTitle = title || 'Document officiel';
                    this.pdfDownloadName = downloadName || 'document.pdf';
                    this.pdfErrorMessage = null;
                    this.pdfViewerHint = false;
                    this.pdfStatus = 'loading';
                    this.showPdfModal = true;

                    try {
                        blob = await fetchPdfBlob(url, requestInit);
                        this.pdfPreviewUrl = URL.createObjectURL(blob);
                        this.pdfStatus = 'ready';
                        await this.$nextTick();
                        this.armViewerHint();
                    } catch (err) {
                        console.error('[pdf-preview] récupération du document :', err);
                        this.pdfErrorMessage = err.message;
                        this.pdfStatus = 'error';
                    }
                },

                /** Alias historique — conservé pour ne pas toucher aux appelants existants. */
                async openPdfModalWithBlob(url, title, downloadName, requestInit) {
                    await this.openPdfPreview(url, title, downloadName, requestInit);
                },

                /** Rejoue la dernière demande (bouton « Réessayer »). */
                async retryPdfPreview() {
                    if (!lastRequest) return;
                    await this.openPdfPreview(lastRequest.url, lastRequest.title, lastRequest.downloadName, lastRequest.requestInit);
                },

                // -------------------------------------------------------------- visionneuse iframe
                /** Armé après le rendu de l'iframe : si `load` ne vient pas, on montre l'astuce. */
                armViewerHint() {
                    if (loadHintTimer) clearTimeout(loadHintTimer);
                    loadHintTimer = setTimeout(() => {
                        if (this.pdfStatus === 'ready') this.pdfViewerHint = true;
                    }, VIEWER_LOAD_HINT_MS);
                },

                /** `x-on:load` de l'iframe : la visionneuse native a affiché le document. */
                pdfViewerLoaded() {
                    if (loadHintTimer) {
                        clearTimeout(loadHintTimer);
                        loadHintTimer = null;
                    }
                    this.pdfViewerHint = false;
                },

                // ---------------------------------------------------------------------- actions
                /**
                 * Impression. La visionneuse native de l'iframe a déjà son propre bouton ; ce bouton
                 * déclenche la même impression. Si le navigateur refuse `print()` sur le document
                 * embarqué, on retombe sur l'ouverture en nouvel onglet.
                 */
                printPreviewPdf() {
                    const frame = this.$refs.pdfFrame;
                    try {
                        if (frame && frame.contentWindow) {
                            frame.contentWindow.focus();
                            frame.contentWindow.print();
                            return;
                        }
                    } catch {
                        /* certains navigateurs bloquent print() sur un PDF embarqué : repli ci-dessous */
                    }
                    this.openPdfInNewTab();
                },

                downloadPreviewPdf() {
                    if (!this.pdfPreviewUrl) return;
                    const link = document.createElement('a');
                    link.href = this.pdfPreviewUrl;
                    link.download = this.pdfDownloadName || 'document.pdf';
                    document.body.appendChild(link);
                    link.click();
                    link.remove();
                },

                openPdfInNewTab() {
                    if (!this.pdfPreviewUrl) return;
                    const win = window.open(this.pdfPreviewUrl, '_blank');
                    if (win) win.focus();
                },

                // ------------------------------------------------------------------- fermeture
                closePdfPreview() {
                    this.showPdfModal = false;
                    releaseDocument(this);
                    this.pdfStatus = 'idle';
                    this.pdfErrorMessage = null;
                    this.pdfViewerHint = false;
                }
            };
        }
    };
})();
