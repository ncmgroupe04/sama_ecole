/**
 * Ouverture PDF partagée — point d'entrée unique de TOUS les aperçus/impressions de documents
 * (reçus, bulletins, attestations, billets, PV, convocations, exports…).
 *
 * PLUS DE MODALE. Le document s'ouvre dans un NOUVEL ONGLET, rendu par la visionneuse PDF native du
 * navigateur (Chrome/Edge/Firefox) avec ses propres commandes de zoom, d'impression et de
 * téléchargement. Ni canvas PDF.js, ni iframe, ni fenêtre applicative.
 *
 * POURQUOI PASSER PAR UN fetch ET UNE BLOB URL, ET PAS `window.open('/api/…/pdf')` DIRECT
 * ------------------------------------------------------------------------------------------
 * Les endpoints PDF exigent `Authorization: Bearer …` et ce jeton vit dans localStorage : il ne
 * voyage jamais sur une navigation classique (Volume_7 §12bis). On récupère donc les octets par
 * `fetch()` (en-tête d'auth), on en fait un `Blob` typé `application/pdf`, et on ouvre CETTE blob URL
 * dans l'onglet. Une blob URL n'est pas un téléchargement HTTP : un gestionnaire de téléchargement
 * (Internet Download Manager & co.) ne peut pas l'intercepter une fois les octets en mémoire.
 *
 * DÉGUISEMENT ANTI-GESTIONNAIRE DE TÉLÉCHARGEMENT (en-tête `X-Pdf-Preview: 1`)
 * ------------------------------------------------------------------------------------------
 * Internet Download Manager (IDM) & consorts, avec l'« intégration avancée au navigateur »,
 * DÉTOURNENT tout `fetch` dont la réponse ressemble à un fichier (`application/pdf`, mais aussi
 * `application/octet-stream`) : ils happent les octets vers leur file et laissent au `fetch` de la
 * page une réponse VIDE (`204`) → toast « document vide (0 octet) ». On envoie donc `X-Pdf-Preview: 1`
 * sur le fetch d'aperçu : le serveur (PdfPreviewDispositionFilter) répond alors en `text/plain`
 * inline sans nom de fichier — invisible pour ces outils. On relit les octets, on vérifie `%PDF-`,
 * on reconstruit un `Blob { type:'application/pdf' }` local.
 *
 * En plus, deux relances au plus :
 *  - `fetch` COUPÉ sans réponse → 1 relance à l'identique.
 *  - Réponse 2xx mais corps VIDE / pas un `%PDF-` → 1 relance après une courte pause.
 * Passé ça, l'échec est affiché honnêtement (dans l'onglet déjà ouvert, ou via un toast), avec un
 * indice « désactivez l'intégration IDM » quand la réponse était un 204/`Content-Length: 0`.
 *
 * L'onglet est ouvert DÈS LE CLIC (avant tout `await`) avec un écran d'attente : après un `await`,
 * le bloqueur de pop-up du navigateur refuserait `window.open`.
 *
 * USAGE — un composant Alpine étale `window.pdfPreview.state()` (rétro-compatible : mêmes noms de
 * méthodes qu'avant, `openPdfPreview` / `openPdfModalWithBlob` ouvrent un onglet) :
 * <code>
 * Alpine.data('students', () => ({
 *     ...window.pdfPreview.state(),
 *     openReceipt(id) { this.openPdfPreview(`/api/v1/…/${id}/pdf`, 'Reçu', 'Recu.pdf'); }
 * }));
 * </code>
 */
(function () {
    'use strict';

    // Corps 2xx vide / pas un PDF : re-tentable (proxy qui purge, générateur qui hoquette).
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

    /** Neutralise tout balisage avant injection dans la page d'attente (le message vient du serveur). */
    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => (
            { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
        ));
    }

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

    /** Message d'aide quand la réponse trahit un détournement par un gestionnaire de téléchargement. */
    const IDM_HINT = " Un gestionnaire de téléchargement (Internet Download Manager & co.) intercepte "
        + "sans doute l'aperçu : désactivez son intégration au navigateur, ou ajoutez ce site à ses exceptions.";

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
        // POST /report-cards/generate) : method/body/headers viennent de l'appelant, l'en-tête
        // Authorization reste géré ici. Accept est posé avant le spread pour que l'appelant puisse le
        // remplacer ; Authorization après, pour qu'aucun appelant ne l'écrase.
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
                'Connexion interrompue pendant le téléchargement du document.', NETWORK);
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

        // Le type MIME est réaffirmé côté client : c'est lui qui fait que le nouvel onglet traite la
        // blob URL comme un PDF (visionneuse native) plutôt que comme un binaire à télécharger.
        return new Blob([bytes], { type: 'application/pdf' });
    }

    /**
     * Récupère le PDF avec au plus deux relances (voir en-tête de fichier) : une sur `fetch` coupé,
     * une sur corps vide/non-PDF après une courte pause. Toute autre erreur remonte immédiatement.
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

    /** Page d'attente / d'erreur écrite dans l'onglet déjà ouvert (évite un onglet blanc). */
    function renderInterstitial(win, { title, body, spinner }) {
        if (!win || win.closed) {
            return;
        }
        try {
            win.document.open();
            win.document.write(
                '<!doctype html><html lang="fr"><head><meta charset="utf-8">'
                + '<meta name="viewport" content="width=device-width,initial-scale=1">'
                + `<title>${escapeHtml(title)}</title><style>`
                + ':root{color-scheme:light dark}'
                + 'body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;'
                + 'font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;'
                + 'background:Canvas;color:CanvasText}'
                + '.box{max-width:24rem;padding:2rem;text-align:center}'
                + '.sp{width:2.25rem;height:2.25rem;margin:0 auto 1rem;border-radius:50%;'
                + 'border:3px solid rgba(128,128,128,.35);border-top-color:currentColor;'
                + 'animation:spin .8s linear infinite}'
                + '@keyframes spin{to{transform:rotate(360deg)}}'
                + 'p{margin:.25rem 0}'
                + '</style></head><body><div class="box">'
                + (spinner ? '<div class="sp"></div>' : '')
                + `<p>${escapeHtml(body)}</p>`
                + '</div></body></html>');
            win.document.close();
        } catch {
            /* onglet inaccessible (rare) : on laisse tomber, le toast prendra le relais */
        }
    }

    /**
     * Récupère le PDF puis l'ouvre dans l'onglet pré-ouvert (visionneuse native). Repli en
     * téléchargement si le navigateur a bloqué l'onglet. Toute erreur est signalée dans l'onglet
     * (page lisible) ou, à défaut, par un toast.
     */
    async function openInBrowser(url, downloadName, requestInit) {
        // Ouvrir l'onglet MAINTENANT, dans le geste de clic : après l'await du fetch, window.open
        // serait refusé par le bloqueur de pop-up.
        const win = window.open('', '_blank');
        renderInterstitial(win, {
            title: 'Document', spinner: true, body: 'Génération du document en cours…'
        });

        let blob;
        try {
            blob = await fetchPdfBlobResilient(url, requestInit);
        } catch (err) {
            console.error('[pdf-preview]', err);
            // Suffixe générique seulement si on n'a pas déjà donné une piste concrète (indice IDM).
            const retryHint = err.kind === EMPTY && !String(err.message).includes('Internet Download Manager')
                ? ' Réessayez ; si le problème persiste, signalez-le.'
                : '';
            const message = (err.message || "Le document n'a pas pu être ouvert.") + retryHint;
            if (win && !win.closed) {
                renderInterstitial(win, { title: 'Document indisponible', spinner: false, body: message });
            } else {
                (window.toast?.error || window.alert)(message);
            }
            return;
        }

        const blobUrl = URL.createObjectURL(blob);
        let openedInTab = false;
        if (win && !win.closed) {
            try {
                win.location.replace(blobUrl);
                openedInTab = true;
            } catch {
                openedInTab = false;
            }
        }

        if (!openedInTab) {
            // Onglet bloqué ou inaccessible : repli en téléchargement, le document n'est jamais perdu.
            const link = document.createElement('a');
            link.href = blobUrl;
            link.download = downloadName || 'document.pdf';
            document.body.appendChild(link);
            link.click();
            link.remove();
            (window.toast?.error || window.alert)(
                "Le navigateur a bloqué l'ouverture d'un nouvel onglet : le document a été téléchargé à la place.");
        }

        // Laisse au nouvel onglet le temps de charger la blob URL avant de la révoquer.
        setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
    }

    window.pdfPreview = {
        /**
         * Méthodes étalées dans un composant Alpine. Noms inchangés pour ne pas toucher les ~14
         * appelants — `openPdfPreview` / `openPdfModalWithBlob` ouvrent un onglet, pas une modale.
         * `closePdfPreview` est un no-op conservé (encore appelé par enrollments.js).
         */
        state() {
            return {
                async openPdfPreview(url, title, downloadName, requestInit) {
                    await openInBrowser(url, downloadName, requestInit);
                },
                async openPdfModalWithBlob(url, title, downloadName, requestInit) {
                    await openInBrowser(url, downloadName, requestInit);
                },
                closePdfPreview() { /* plus de modale à fermer */ }
            };
        }
    };
})();
