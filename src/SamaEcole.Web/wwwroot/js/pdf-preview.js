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
 * `fetch()` (en-tête d'auth + `X-Pdf-Preview: 1`), on en fait un `Blob` typé `application/pdf`, et on
 * ouvre CETTE blob URL. Bonus : une blob URL n'est pas un téléchargement HTTP — un gestionnaire de
 * téléchargement (Internet Download Manager & co.) ne peut pas l'intercepter.
 *
 * Les vérifications d'avant ouverture (HTTP OK, corps non vide, signature `%PDF-`) sont conservées :
 * un échec produit un toast explicite plutôt qu'un onglet blanc.
 *
 * USAGE — un composant Alpine étale `window.pdfPreview.state()` (rétro-compatible : mêmes noms de
 * méthodes qu'avant, `openPdfPreview` / `openPdfModalWithBlob` ouvrent maintenant un onglet) :
 * <code>
 * Alpine.data('students', () => ({
 *     ...window.pdfPreview.state(),
 *     openReceipt(id) { this.openPdfPreview(`/api/v1/…/${id}/pdf`, 'Reçu', 'Recu.pdf'); }
 * }));
 * </code>
 */
(function () {
    'use strict';

    /** Vrai si les 5 premiers octets sont la signature « %PDF- » d'un fichier PDF. */
    async function looksLikePdf(blob) {
        const head = new Uint8Array(await blob.slice(0, 5).arrayBuffer());
        return head[0] === 0x25 && head[1] === 0x50 && head[2] === 0x44 && head[3] === 0x46 && head[4] === 0x2d;
    }

    /**
     * Récupère le document et vérifie qu'il s'agit bien d'un PDF exploitable AVANT de l'ouvrir —
     * chaque vérification correspond à une panne réellement observée en production.
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
        // X-Pdf-Preview: 1 — le serveur renvoie alors les octets en `application/octet-stream` `inline`
        // (voir PdfPreviewDispositionFilter) : un gestionnaire de téléchargement n'y voit plus un
        // fichier PDF et cesse d'intercepter le fetch.
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
        // « %PDF- » des octets reçus plutôt que l'en-tête Content-Type (volontairement `octet-stream`
        // depuis X-Pdf-Preview) — et c'est de toute façon une vérification plus fiable.
        if (!(await looksLikePdf(blob))) {
            const detail = (await blob.text().catch(() => '')).slice(0, 200);
            throw new Error(`Le serveur n'a pas renvoyé un PDF valide${detail ? ` : ${detail}` : '.'}`);
        }

        // Le type MIME est réaffirmé côté client : c'est lui qui fait que le nouvel onglet traite la
        // blob URL comme un PDF (et l'affiche dans la visionneuse native) plutôt que comme un binaire.
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

    /**
     * Récupère le PDF puis l'ouvre dans un nouvel onglet (visionneuse native). Repli en téléchargement
     * si le navigateur bloque la fenêtre. Toute erreur est signalée par un toast.
     */
    async function openInBrowser(url, downloadName, requestInit) {
        let blob;
        try {
            blob = await fetchPdfBlob(url, requestInit);
        } catch (err) {
            console.error('[pdf-preview]', err);
            (window.toast?.error || window.alert)(err.message);
            return;
        }

        const blobUrl = URL.createObjectURL(blob);
        const win = window.open(blobUrl, '_blank');
        if (win) {
            win.focus();
        } else {
            // Fenêtre bloquée : on retombe sur un téléchargement, le document n'est jamais perdu.
            const link = document.createElement('a');
            link.href = blobUrl;
            link.download = downloadName || 'document.pdf';
            document.body.appendChild(link);
            link.click();
            link.remove();
        }
        // Laisse au nouvel onglet le temps de charger la blob URL avant de la révoquer.
        setTimeout(() => URL.revokeObjectURL(blobUrl), 60000);
    }

    window.pdfPreview = {
        /**
         * Méthodes étalées dans un composant Alpine. Noms inchangés pour ne pas toucher les ~14
         * appelants — `openPdfPreview` / `openPdfModalWithBlob` ouvrent désormais un onglet, pas une
         * modale. `closePdfPreview` est un no-op conservé (encore appelé par enrollments.js).
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
