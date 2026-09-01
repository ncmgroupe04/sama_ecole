/**
 * Aperçu PDF partagé — moteur unique de TOUTES les modales de prévisualisation (reçus, bulletins,
 * attestations, billets, sommations, convocations, exports…).
 *
 * AFFICHAGE — MODALE + <iframe> + VISIONNEUSE NATIVE
 * -------------------------------------------------
 * Le document s'affiche dans une modale applicative (`Views/Shared/_PdfPreviewModal.cshtml`), rendu
 * par un `<iframe>` pointant sur une **blob URL locale** et donc par la visionneuse PDF NATIVE du
 * navigateur (zoom / impression / téléchargement intégrés). Pas de PDF.js/canvas (≈1,8 Mo de deps,
 * plus lent). `<object data=…>` serait équivalent mais la CSP pose `object-src 'none'` (JGK-F01) —
 * seul `<iframe>` passe (`frame-src 'self' blob:`). Repli « Ouvrir dans un nouvel onglet » toujours
 * visible : si la visionneuse intégrée ne s'affiche pas (rare), la modale n'est jamais un cul-de-sac.
 *
 * POURQUOI fetch + blob, ET PAS `iframe.src = '/api/…/pdf'` DIRECT
 * --------------------------------------------------------------
 * Les endpoints PDF exigent `Authorization: Bearer …`, jeton en localStorage qui ne voyage pas sur
 * une navigation d'iframe (Volume_7 §12bis). On récupère donc les octets par `fetch()`, on en fait
 * un `Blob { type:'application/pdf' }`, et l'iframe charge CE blob.
 *
 * DÉGUISEMENT ANTI-GESTIONNAIRE DE TÉLÉCHARGEMENT (en-tête `X-Pdf-Preview: 1`)
 * ------------------------------------------------------------------------------
 * Internet Download Manager (IDM) & consorts, avec l'« intégration avancée au navigateur »,
 * DÉTOURNENT tout `fetch` dont la réponse ressemble à un fichier (`application/pdf`, mais aussi
 * `application/octet-stream`) : ils happent les octets vers leur file et laissent au `fetch` de la
 * page une réponse VIDE (`204`) → « document vide (0 octet) ». On envoie donc `X-Pdf-Preview: 1` :
 * le serveur (`PdfPreviewDispositionFilter`) répond alors en `text/plain` inline sans nom de
 * fichier — invisible pour ces outils. On relit les octets bruts, on vérifie la signature `%PDF-`,
 * on reconstruit le `Blob` typé en local.
 *
 * FILET DE SÉCURITÉ
 * ----------------
 * `fetchPdfBlobResilient` vérifie AVANT affichage : HTTP OK, corps non vide, signature `%PDF-`.
 * Chaque cause a son message (dont un indice « désactivez l'intégration IDM » sur un 204). Deux
 * relances au plus : une sur `fetch` coupé, une sur corps vide/non-PDF après une courte pause.
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

    // Corps 2xx vide / pas un PDF : re-tentable (gestionnaire de téléchargement, proxy, hoquet du générateur).
    const EMPTY = 'EMPTY_OR_INVALID';
    // `fetch` rejeté sans réponse : coupure réseau, ou gestionnaire de téléchargement qui happe le flux.
    const NETWORK = 'NETWORK';
    // Erreur définitive (identifiant invalide, 4xx/5xx applicatif) : inutile de réessayer.
    const FATAL = 'FATAL';

    class PdfFetchError extends Error {
        constructor(message, kind) {
            super(message);
            this.name = 'PdfFetchError';
            this.kind = kind;
        }
    }

    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    /** Message d'aide quand la réponse trahit un détournement par un gestionnaire de téléchargement. */
    const IDM_HINT = " Un gestionnaire de téléchargement (Internet Download Manager & co.) intercepte "
        + "sans doute l'aperçu : désactivez son intégration au navigateur, ou ajoutez ce site à ses exceptions.";

    /** Vrai si les 5 premiers octets sont la signature « %PDF- » d'un fichier PDF. */
    function looksLikePdf(bytes) {
        return bytes.length >= 5
            && bytes[0] === 0x25 && bytes[1] === 0x50 && bytes[2] === 0x44 && bytes[3] === 0x46 && bytes[4] === 0x2d;
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

    /**
     * Un aller-retour réseau. Envoie `X-Pdf-Preview: 1` : le serveur répond alors en `text/plain`
     * inline sans nom de fichier (PdfPreviewDispositionFilter), invisible pour IDM & consorts. On
     * relit les octets bruts et on vérifie la signature `%PDF-`.
     */
    async function fetchOnce(url, requestInit) {
        if (!url || url.includes('undefined') || url.includes('null')) {
            throw new PdfFetchError(
                `L'identifiant du document est invalide — l'aperçu ne peut pas être demandé (${url}).`, FATAL);
        }

        // Un jeton périmé produirait un 401 traduit en « aperçu indisponible » : on le renouvelle avant.
        if (window.auth?.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await window.api.refreshOrRedirect();
        }

        // requestInit permet un aperçu servi par une route POST à corps JSON (ex. bulletin de notes,
        // POST /report-cards/generate) : method/body/headers viennent de l'appelant. Accept avant le
        // spread pour que l'appelant puisse le remplacer ; Authorization après, pour qu'aucun appelant
        // ne l'écrase.
        const headers = {
            Accept: 'application/pdf',
            'X-Pdf-Preview': '1',
            ...(requestInit && requestInit.headers),
            Authorization: `Bearer ${window.auth?.accessToken}`
        };

        let response;
        try {
            response = await fetch(url, {
                method: (requestInit && requestInit.method) || 'GET',
                headers,
                body: requestInit && requestInit.body,
                credentials: 'same-origin',
                // Aucun cache intermédiaire ne doit resservir un 200 vide déjà observé une fois.
                cache: 'no-store'
            });
        } catch {
            throw new PdfFetchError(
                'Connexion interrompue pendant le téléchargement du document. Vérifiez votre connexion, puis réessayez.',
                NETWORK);
        }

        if (!response.ok) {
            const detail = await readErrorMessage(response);
            throw new PdfFetchError(
                `Le serveur a refusé la génération du document (erreur ${response.status})${detail ? ` : ${detail}` : '.'}`,
                FATAL);
        }

        let bytes;
        try {
            bytes = new Uint8Array(await response.arrayBuffer());
        } catch {
            // Corps annoncé puis tronqué en route (ERR_CONTENT_LENGTH_MISMATCH, flux coupé) : re-tentable.
            throw new PdfFetchError('Le téléchargement du document a été interrompu avant la fin.', EMPTY);
        }

        // Corps vide sur une réponse "réussie" : soit un gestionnaire de téléchargement a happé les
        // octets (signature : 204, ou Content-Length: 0), soit une boîte intermédiaire les a purgés,
        // soit le générateur a hoqueté. Re-tentable ; on ajoute l'indice IDM quand il s'applique.
        if (bytes.length === 0) {
            const hijacked = response.status === 204 || response.headers.get('content-length') === '0';
            throw new PdfFetchError(
                'Le document reçu par le navigateur est vide (0 octet).' + (hijacked ? IDM_HINT : ''),
                EMPTY);
        }
        if (!looksLikePdf(bytes)) {
            let hint = '';
            try {
                hint = new TextDecoder().decode(bytes.slice(0, 200)).replace(/\s+/g, ' ').trim();
            } catch { /* binaire non textuel : pas d'indice lisible */ }
            throw new PdfFetchError(
                `Le serveur n'a pas renvoyé un PDF valide${hint ? ` : ${hint}` : '.'}`, EMPTY);
        }

        // Le type MIME est réaffirmé côté client : c'est lui qui fait que l'iframe, le nouvel onglet et
        // l'impression traitent la blob URL comme un PDF (et non comme un téléchargement binaire).
        return new Blob([bytes], { type: 'application/pdf' });
    }

    /**
     * Récupère le PDF avec au plus deux relances : une sur `fetch` coupé, une sur corps vide/non-PDF
     * après une courte pause. Toute autre erreur remonte immédiatement.
     */
    async function fetchPdfBlobResilient(url, requestInit) {
        try {
            return await fetchOnce(url, requestInit);
        } catch (err) {
            if (err.kind === NETWORK) {
                return await fetchOnce(url, requestInit); // 1 relance à l'identique
            }
            if (err.kind === EMPTY) {
                await sleep(900);
                return await fetchOnce(url, requestInit); // dernière chance ; l'échec remonte tel quel
            }
            throw err;
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
                        blob = await fetchPdfBlobResilient(url, requestInit);
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

                /**
                 * Affiche un Blob PDF DÉJÀ récupéré, sans aucun aller-retour réseau.
                 *
                 * Pour les documents produits par une écriture NON idempotente : le certificat de
                 * mutation (POST qui grave un numéro officiel séquentiel) ne doit jamais être rejoué —
                 * or fetchPdfBlobResilient relance sur coupure réseau / corps vide. L'appelant fait donc
                 * son unique POST lui-même, puis nous passe les octets obtenus.
                 */
                showPdfBlob(pdfBlob, title, downloadName) {
                    window.closeAllModals?.();
                    releaseDocument(this);

                    this.pdfPreviewTitle = title || 'Document officiel';
                    this.pdfDownloadName = downloadName || 'document.pdf';
                    this.pdfErrorMessage = null;
                    this.pdfViewerHint = false;
                    lastRequest = null; // rien à rejouer : le blob est unique

                    blob = pdfBlob;
                    this.pdfPreviewUrl = URL.createObjectURL(pdfBlob);
                    this.pdfStatus = 'ready';
                    this.showPdfModal = true;
                    this.$nextTick(() => this.armViewerHint());
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
