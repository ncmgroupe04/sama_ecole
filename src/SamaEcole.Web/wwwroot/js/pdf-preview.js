/**
 * Aperçu PDF partagé — moteur unique de TOUTES les modales de prévisualisation (reçus, bulletins,
 * attestations, billets, sommations…).
 *
 * POURQUOI CE FICHIER EXISTE
 * --------------------------
 * L'aperçu reposait sur un `<iframe src="blob:…">` recopié à l'identique dans quatre écrans
 * (Dashboard, Caisse, Enrollments, Students). Ce montage échoue de façon *silencieuse et non
 * reproductible* : un iframe ne remonte aucun événement `error` quand le visualiseur PDF interne du
 * navigateur refuse de s'afficher (extension de blocage, PDF viewer désactivé dans Firefox, mode
 * « télécharger au lieu d'ouvrir » de Chrome, navigateur mobile sans plugin PDF…). L'écran restait
 * blanc et l'application affichait un message générique — « votre navigateur restreint l'affichage »
 * — y compris quand la vraie cause était toute autre (jeton expiré, 404, document vide).
 *
 * On ne délègue donc plus l'affichage au navigateur : PDF.js décode le document et le peint dans des
 * `<canvas>`. Un canvas s'affiche partout, sans plugin, sans extension, sans politique de cadre — le
 * rendu ne peut plus « sauter ». En contrepartie il faut gérer nous-mêmes l'échelle, la pagination
 * et l'impression : c'est tout l'objet de ce fichier.
 *
 * CHOIX D'IMPLÉMENTATION
 * ----------------------
 * - **Auto-hébergé, jamais un CDN.** La CSP (`script-src 'self'`, JGK-F01) bloque tout script
 *   externe, et le raisonnement de `vendor/README.md` s'applique tel quel.
 * - **Chargé à la demande** (`import()` dynamique au premier aperçu) : PDF.js pèse ~500 Ko + ~1,3 Mo
 *   de worker, hors de question de les imposer à chaque page sur une connexion mobile (Volume 5 §1).
 * - **Repli sans PDF.js** : si le module ne se charge pas, la modale reste utilisable (nouvel onglet
 *   + téléchargement) au lieu de se bloquer.
 * - **Rendu progressif** : la page 1 s'affiche dès qu'elle est prête, les suivantes s'ajoutent en
 *   arrière-plan. Un PDF de bulletins de classe (60+ pages) ne fige plus l'interface.
 * - **Erreurs distinctes** : chaque cause a son message. Un aperçu qui échoue doit dire *pourquoi*,
 *   sinon on rejoue indéfiniment le bug précédent.
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

    // Version épinglée : voir wwwroot/js/vendor/README.md. Le paramètre ?v= sert de cache-buster —
    // asp-append-version ne s'applique pas à un import() dynamique, dont l'URL est une chaîne JS.
    const PDFJS_VERSION = '6.1.200';
    const PDFJS_MODULE = `/js/vendor/pdf.min.mjs?v=${PDFJS_VERSION}`;
    const PDFJS_WORKER = `/js/vendor/pdf.worker.min.mjs?v=${PDFJS_VERSION}`;
    const PDFJS_STANDARD_FONTS = '/js/vendor/pdfjs-standard-fonts/';

    /** Bornes de zoom, en multiples de l'échelle « ajustée à la largeur » (1 = pleine largeur). */
    const ZOOM_MIN = 0.5;
    const ZOOM_MAX = 3;
    const ZOOM_STEP = 0.25;

    /**
     * Plafond du suréchantillonnage HiDPI. Sans plafond, un écran 3x fabrique des canvas 9 fois plus
     * lourds en mémoire — un PDF de 60 pages y épuise le tas du navigateur mobile.
     */
    const MAX_PIXEL_RATIO = 2;

    /** Largeur de repli quand le conteneur n'est pas encore mesurable (modale en cours d'ouverture). */
    const FALLBACK_WIDTH = 640;

    let pdfjsPromise = null;

    /**
     * Charge PDF.js une seule fois par page, et configure son worker.
     * Le worker est servi depuis notre propre origine : la CSP l'autorise via `worker-src 'self'`, et
     * PDF.js n'a alors pas besoin de l'envelopper dans un Blob (ce qu'il ne fait que pour un worker
     * d'origine tierce).
     */
    function loadPdfJs() {
        if (!pdfjsPromise) {
            pdfjsPromise = import(PDFJS_MODULE)
                .then(lib => {
                    lib.GlobalWorkerOptions.workerSrc = PDFJS_WORKER;
                    return lib;
                })
                .catch(err => {
                    pdfjsPromise = null; // permet une nouvelle tentative au prochain aperçu
                    throw err;
                });
        }
        return pdfjsPromise;
    }

    /** Vrai si les 5 premiers octets sont la signature « %PDF- » d'un fichier PDF. */
    async function looksLikePdf(blob) {
        const head = new Uint8Array(await blob.slice(0, 5).arrayBuffer());
        return head[0] === 0x25 && head[1] === 0x50 && head[2] === 0x44 && head[3] === 0x46 && head[4] === 0x2d;
    }

    /**
     * Récupère le document et vérifie qu'il s'agit bien d'un PDF exploitable AVANT de le confier au
     * moteur de rendu — chaque vérification correspond à une panne réellement observée en production.
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
        // mailles du filet (page HTML de session expirée, corps JSON d'erreur) : le dire franchement
        // vaut mieux que laisser PDF.js échouer sur un « InvalidPDFException » incompréhensible.
        // On teste la signature « %PDF- » des octets reçus plutôt que l'en-tête Content-Type : depuis
        // X-Pdf-Preview, le serveur répond volontairement en `application/octet-stream`, et la
        // signature est de toute façon une vérification plus fiable qu'un en-tête déclaratif.
        if (!(await looksLikePdf(blob))) {
            const detail = (await blob.text().catch(() => '')).slice(0, 200);
            throw new Error(`Le serveur n'a pas renvoyé un PDF valide${detail ? ` : ${detail}` : '.'}`);
        }

        // Le type MIME est réaffirmé côté client : Blob.type conditionne l'ouverture dans un nouvel
        // onglet et l'impression.
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

    /** Traduit les erreurs techniques de PDF.js en langage utilisable par un secrétariat. */
    function describeRenderError(err) {
        const name = err?.name || '';
        if (name === 'PasswordException') {
            return 'Ce document est protégé par un mot de passe et ne peut pas être prévisualisé.';
        }
        if (name === 'InvalidPDFException') {
            return "Le fichier reçu n'est pas un PDF valide — il a probablement été tronqué pendant le transfert.";
        }
        return `L'aperçu n'a pas pu être généré (${err?.message || 'cause inconnue'}).`;
    }

    window.pdfPreview = {
        /**
         * Fabrique l'état + les méthodes d'aperçu d'UN composant Alpine.
         *
         * Les objets non sérialisables (document PDF, Blob, jeton d'annulation) vivent dans la
         * fermeture, jamais dans l'objet retourné : Alpine enveloppe les données d'un composant dans
         * un Proxy réactif profond, et proxifier un `PDFDocumentProxy` casserait ses champs privés.
         * Seules des valeurs primitives — celles que la vue affiche — traversent la réactivité.
         */
        state() {
            let doc = null;          // PDFDocumentProxy en cours
            let blob = null;         // Blob source, conservé pour imprimer / télécharger / ouvrir
            let lastRequest = null;  // { url, title, downloadName } pour le bouton « Réessayer »
            let renderToken = null;  // jeton d'annulation du rendu en cours
            let resizeHandler = null;

            /** Invalide le rendu en cours : les pages restantes ne seront pas peintes. */
            function cancelRender() {
                if (renderToken) {
                    renderToken.cancelled = true;
                    renderToken.task?.cancel();
                    renderToken = null;
                }
            }

            /** Libère document, Blob URL et écouteurs. Appelé à la fermeture et avant tout rechargement. */
            function releaseDocument(component) {
                cancelRender();
                if (resizeHandler) {
                    window.removeEventListener('resize', resizeHandler);
                    resizeHandler = null;
                }
                doc?.destroy();
                doc = null;
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
                /** Vrai quand PDF.js est indisponible : la modale propose alors onglet + téléchargement. */
                pdfDegraded: false,
                pdfPreviewUrl: null,
                pdfPageCount: 0,
                pdfRenderedCount: 0,
                pdfZoom: 1,

                // --------------------------------------------------------------------- ouverture
                /**
                 * Ouvre la modale et affiche `url`. La modale s'ouvre TOUJOURS, y compris en erreur :
                 * l'utilisateur doit pouvoir lire la cause et cliquer « Réessayer » plutôt que de
                 * subir un clic sans effet.
                 */
                async openPdfPreview(url, title, downloadName, requestInit) {
                    lastRequest = { url, title, downloadName, requestInit };

                    // Un aperçu PDF est presque toujours ouvert DEPUIS une autre modale (fiche élève,
                    // reçu, tableau des paiements) : sans cette fermeture, les deux `modal-shell`
                    // s'empilent visuellement l'un sur l'autre.
                    window.closeAllModals?.();

                    releaseDocument(this);
                    this.pdfPreviewTitle = title || 'Document officiel';
                    this.pdfDownloadName = downloadName || 'document.pdf';
                    this.pdfErrorMessage = null;
                    this.pdfDegraded = false;
                    this.pdfPageCount = 0;
                    this.pdfRenderedCount = 0;
                    this.pdfZoom = 1;
                    this.pdfStatus = 'loading';
                    this.showPdfModal = true;

                    // Le conteneur de pages n'est mesurable qu'une fois la modale affichée.
                    await this.$nextTick();
                    this.$refs.pdfPages && (this.$refs.pdfPages.innerHTML = '');

                    try {
                        blob = await fetchPdfBlob(url, requestInit);
                        this.pdfPreviewUrl = URL.createObjectURL(blob);
                    } catch (err) {
                        console.error('[pdf-preview] récupération du document :', err);
                        this.pdfErrorMessage = err.message;
                        this.pdfStatus = 'error';
                        return;
                    }

                    let pdfjsLib;
                    try {
                        pdfjsLib = await loadPdfJs();
                    } catch (err) {
                        // Le document est là, seul le moteur de rendu manque : on dégrade au lieu
                        // d'échouer — nouvel onglet et téléchargement restent parfaitement utiles.
                        console.error('[pdf-preview] chargement de PDF.js :', err);
                        this.pdfDegraded = true;
                        this.pdfErrorMessage = "Le moteur d'aperçu n'a pas pu être chargé. Le document reste téléchargeable et consultable dans un nouvel onglet.";
                        this.pdfStatus = 'error';
                        return;
                    }

                    try {
                        // `data` détache l'ArrayBuffer transmis : on repart d'une copie fraîche du Blob
                        // pour que `blob` reste exploitable par l'impression et le téléchargement.
                        const bytes = new Uint8Array(await blob.arrayBuffer());
                        doc = await pdfjsLib.getDocument({
                            data: bytes,
                            standardFontDataUrl: PDFJS_STANDARD_FONTS
                        }).promise;

                        this.pdfPageCount = doc.numPages;
                        await this.renderPdfPages();

                        // Une modale redimensionnée (rotation mobile, fenêtre élargie) doit repeindre
                        // à la bonne échelle, sinon les pages restent floues ou débordent.
                        resizeHandler = debounce(() => {
                            if (this.showPdfModal && this.pdfStatus === 'ready') this.renderPdfPages();
                        }, 200);
                        window.addEventListener('resize', resizeHandler);
                    } catch (err) {
                        console.error('[pdf-preview] rendu du document :', err);
                        this.pdfErrorMessage = describeRenderError(err);
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

                // ------------------------------------------------------------------------ rendu
                /**
                 * Peint toutes les pages dans le conteneur, l'une après l'autre. Le jeton rend
                 * l'opération annulable : rouvrir un autre document ou zoomer pendant le rendu
                 * abandonne immédiatement le précédent au lieu d'empiler deux séries de pages.
                 */
                async renderPdfPages() {
                    const container = this.$refs.pdfPages;
                    if (!doc || !container) return;

                    cancelRender();
                    const token = { cancelled: false, task: null };
                    renderToken = token;

                    const ratio = Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
                    const available = Math.max((container.clientWidth || FALLBACK_WIDTH) - 8, 280);
                    const painted = document.createDocumentFragment();
                    this.pdfRenderedCount = 0;

                    for (let pageNumber = 1; pageNumber <= doc.numPages; pageNumber++) {
                        if (token.cancelled) return;

                        const page = await doc.getPage(pageNumber);
                        if (token.cancelled) return;

                        const scale = (available / page.getViewport({ scale: 1 }).width) * this.pdfZoom;
                        const viewport = page.getViewport({ scale: Math.min(Math.max(scale, 0.1), 10) });

                        const canvas = document.createElement('canvas');
                        canvas.width = Math.floor(viewport.width * ratio);
                        canvas.height = Math.floor(viewport.height * ratio);
                        // Le canvas est dimensionné en pixels physiques pour la netteté, puis ramené à
                        // sa taille logique en CSS : sans cela il s'afficherait deux fois trop grand.
                        canvas.style.width = `${Math.floor(viewport.width)}px`;
                        canvas.style.height = `${Math.floor(viewport.height)}px`;
                        canvas.className = 'mx-auto mb-4 max-w-full rounded-lg bg-white shadow-sm ring-1 ring-slate-200';
                        canvas.setAttribute('role', 'img');
                        canvas.setAttribute('aria-label', `Page ${pageNumber} sur ${doc.numPages}`);

                        const task = page.render({
                            canvasContext: canvas.getContext('2d'),
                            viewport,
                            transform: ratio === 1 ? null : [ratio, 0, 0, ratio, 0, 0]
                        });
                        token.task = task;

                        try {
                            await task.promise;
                        } catch (err) {
                            if (token.cancelled || err?.name === 'RenderingCancelledException') return;
                            throw err;
                        }
                        if (token.cancelled) return;

                        // La première page remplace d'un coup l'ancien contenu (rendu « propre »), les
                        // suivantes s'ajoutent : l'utilisateur voit son document sans attendre la fin.
                        if (pageNumber === 1) {
                            painted.appendChild(canvas);
                            container.replaceChildren(painted);
                            this.pdfStatus = 'ready';
                        } else {
                            container.appendChild(canvas);
                        }
                        this.pdfRenderedCount = pageNumber;
                        page.cleanup();
                    }
                },

                // ------------------------------------------------------------------------- zoom
                zoomPdfIn() { this.setPdfZoom(this.pdfZoom + ZOOM_STEP); },
                zoomPdfOut() { this.setPdfZoom(this.pdfZoom - ZOOM_STEP); },
                zoomPdfReset() { this.setPdfZoom(1); },

                setPdfZoom(value) {
                    const next = Math.min(Math.max(Math.round(value * 100) / 100, ZOOM_MIN), ZOOM_MAX);
                    if (next === this.pdfZoom) return;
                    this.pdfZoom = next;
                    if (this.pdfStatus === 'ready') this.renderPdfPages();
                },

                get pdfZoomLabel() { return `${Math.round(this.pdfZoom * 100)} %`; },
                get canZoomPdfIn() { return this.pdfZoom < ZOOM_MAX; },
                get canZoomPdfOut() { return this.pdfZoom > ZOOM_MIN; },

                // ---------------------------------------------------------------------- actions
                /**
                 * Impression. Un canvas s'imprime mal (image pixellisée, pas de sélection de texte) :
                 * on imprime donc le PDF d'origine via un iframe caché, autorisé par `frame-src blob:`.
                 * Si le navigateur refuse d'y rendre le PDF, on retombe sur un nouvel onglet — le
                 * document reste imprimable, ce que l'ancienne version ne garantissait pas.
                 */
                printPreviewPdf() {
                    if (!this.pdfPreviewUrl) return;

                    document.getElementById('pdf-preview-print-frame')?.remove();

                    const frame = document.createElement('iframe');
                    frame.id = 'pdf-preview-print-frame';
                    frame.style.position = 'fixed';
                    frame.style.right = '0';
                    frame.style.bottom = '0';
                    frame.style.width = '0';
                    frame.style.height = '0';
                    frame.style.border = '0';
                    frame.src = this.pdfPreviewUrl;

                    const openInTab = () => {
                        frame.remove();
                        const win = window.open(this.pdfPreviewUrl, '_blank');
                        if (win) win.focus();
                    };

                    // Certains navigateurs ne déclenchent jamais `load` sur un PDF qu'ils refusent
                    // d'afficher : sans ce garde-fou, « Imprimer » resterait sans effet.
                    const guard = setTimeout(openInTab, 3000);

                    frame.onload = () => {
                        clearTimeout(guard);
                        try {
                            frame.contentWindow.focus();
                            frame.contentWindow.print();
                        } catch {
                            openInTab();
                        }
                    };
                    frame.onerror = () => { clearTimeout(guard); openInTab(); };

                    document.body.appendChild(frame);
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
                    document.getElementById('pdf-preview-print-frame')?.remove();
                    if (this.$refs.pdfPages) this.$refs.pdfPages.replaceChildren();
                    this.pdfStatus = 'idle';
                    this.pdfErrorMessage = null;
                    this.pdfDegraded = false;
                    this.pdfPageCount = 0;
                    this.pdfRenderedCount = 0;
                }
            };
        }
    };

    function debounce(fn, delay) {
        let timer = null;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delay);
        };
    }
})();
