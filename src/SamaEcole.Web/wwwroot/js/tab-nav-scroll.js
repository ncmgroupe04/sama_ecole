/*
 * Barres d'onglets horizontales défilantes (.tab-nav-scroll) — comportement transverse, chargé une
 * fois par _Layout.cshtml (comme sidebar-nav.js), sans dépendance à Alpine.
 *
 * Le composant CSS (Styles/input.css) pose déjà la piste, le masquage de la barre de défilement,
 * `scroll-behavior: smooth` et `touch-action: pan-x`. Ce script ajoute ce qu'une barre horizontale
 * à barre de défilement masquée exige pour rester réellement utilisable — c'était le défaut signalé :
 * sur Paramètres, les derniers onglets (Facturation, Utilisateurs, Journal d'audit, Notifications
 * SMS) étaient hors champ, sans aucun moyen visible de les atteindre.
 *
 *   1. Molette verticale -> défilement HORIZONTAL. Une molette de souris ne produit que du vertical ;
 *      sans cette translation la piste reste figée à la souris seule.
 *   2. Glisser-déposer à la souris pour attraper la piste. SOURIS UNIQUEMENT : sur écran tactile le
 *      défilement natif de `overflow-x` fait déjà le travail, on ne le double pas.
 *   3. Onglet actif (.tab-btn-active) toujours ramené AU CENTRE — au chargement (utile quand l'URL
 *      ouvre un onglet de droite, ex. /parametres?tab=sms) ET à chaque changement. Détecté par
 *      MutationObserver sur la classe des descendants : marche pour goToTab() de Paramètres comme
 *      pour switchTab() ou un simple `tab = '...'`, sans toucher au JS de chaque module.
 *   4. Deux dégradés d'estompement + deux boutons fléchés [<] [>], révélés UNIQUEMENT du côté où il
 *      reste des onglets hors champ (attributs data-overflow-left / -right posés sur l'enveloppe).
 *
 * Le centrage se fait par `scrollBy()` SUR LA PISTE, jamais `Element.scrollIntoView()` : celui-ci
 * remonte la chaîne des ancêtres défilants et ferait sauter toute la page.
 *
 * Idempotent (`data-tabscroll-ready`) et tolérant à un DOM tardif (MutationObserver sur <body> pour
 * les barres rendues après coup — modales, x-if).
 */
(function () {
    'use strict';

    var READY = 'data-tabscroll-ready';
    var REDUCED = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var SMOOTH = REDUCED ? 'auto' : 'smooth';
    // mt-6, mx-6, my-2, -mb-4, m-3… mais NI max-w-*, NI min-w-*, NI ml-auto exclu volontairement.
    var MARGIN_CLASS = /^-?m[trblxy]?-/;

    function el(tag, className) {
        var node = document.createElement(tag);
        node.className = className;
        return node;
    }

    function arrowButton(dir) {
        var b = document.createElement('button');
        b.type = 'button';
        // Noms de classe en toutes lettres (pas de concaténation) : Tailwind purge son
        // @layer components d'après les jetons littéraux trouvés dans les fichiers scannés.
        b.className = dir === 'left'
            ? 'tab-nav-scroll-arrow tab-nav-scroll-arrow-left'
            : 'tab-nav-scroll-arrow tab-nav-scroll-arrow-right';
        // Purement décoratif : les onglets restent atteignables au clavier (le focus fait défiler
        // la piste tout seul) comme à la molette. Pas d'arrêt de tabulation supplémentaire.
        b.setAttribute('aria-hidden', 'true');
        b.tabIndex = -1;
        var d = dir === 'left'
            ? 'M15.4 4.6 8 12l7.4 7.4 1.4-1.4L10.8 12l6-6z'
            : 'M8.6 4.6 16 12l-7.4 7.4L7.2 18l6-6-6-6z';
        b.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true"><path d="' + d + '"/></svg>';
        return b;
    }

    function enhance(scroller) {
        if (!scroller || scroller.nodeType !== 1 || scroller.hasAttribute(READY) || !scroller.parentNode) return;
        scroller.setAttribute(READY, '');

        // --- 1. Enveloppe + indicateurs -------------------------------------------------------
        var wrap = el('div', 'tab-nav-scroll-wrap');
        // Les marges de mise en page vivaient sur la piste ("mt-6 tab-nav-scroll",
        // "tab-nav-scroll mx-6 mb-6") : elles doivent passer sur l'enveloppe, sinon les flèches et
        // les dégradés — positionnés par rapport à l'enveloppe — seraient décalés des vrais bords.
        scroller.className.split(/\s+/).forEach(function (cls) {
            if (cls && MARGIN_CLASS.test(cls)) {
                wrap.classList.add(cls);
                scroller.classList.remove(cls);
            }
        });
        scroller.parentNode.insertBefore(wrap, scroller);
        wrap.appendChild(scroller);

        var fadeLeft = el('div', 'tab-nav-scroll-fade tab-nav-scroll-fade-left');
        var fadeRight = el('div', 'tab-nav-scroll-fade tab-nav-scroll-fade-right');
        var arrowLeft = arrowButton('left');
        var arrowRight = arrowButton('right');
        wrap.appendChild(fadeLeft);
        wrap.appendChild(fadeRight);
        wrap.appendChild(arrowLeft);
        wrap.appendChild(arrowRight);

        function canScroll() {
            return scroller.scrollWidth > scroller.clientWidth + 1;
        }

        // --- 2. Molette verticale -> défilement horizontal --------------------------------
        scroller.addEventListener('wheel', function (e) {
            if (e.shiftKey || Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return; // déjà un geste horizontal
            if (!canScroll()) return;
            e.preventDefault();
            scroller.scrollBy({ left: e.deltaY, behavior: 'auto' });
        }, { passive: false });

        // --- 3. Glisser-déposer à la souris ---------------------------------------------
        var dragging = false, justDragged = false, startX = 0, startLeft = 0, moved = 0;

        scroller.addEventListener('pointerdown', function (e) {
            if (e.pointerType !== 'mouse' || e.button !== 0 || !canScroll()) return;
            dragging = true;
            moved = 0;
            startX = e.clientX;
            startLeft = scroller.scrollLeft;
        });
        scroller.addEventListener('pointermove', function (e) {
            if (!dragging) return;
            var dx = e.clientX - startX;
            if (Math.abs(dx) > moved) moved = Math.abs(dx);
            if (moved > 3) {
                e.preventDefault(); // coupe la sélection de texte pendant le glissé
                scroller.classList.add('is-dragging');
                if (scroller.setPointerCapture) {
                    try { scroller.setPointerCapture(e.pointerId); } catch (_) { /* déjà relâché */ }
                }
            }
            scroller.scrollLeft = startLeft - dx;
        });
        function endDrag() {
            if (!dragging) return;
            dragging = false;
            scroller.classList.remove('is-dragging');
            if (moved > 5) {
                // Un vrai glissé ne doit pas activer l'onglet sous le curseur au relâchement.
                justDragged = true;
                setTimeout(function () { justDragged = false; }, 0);
            }
        }
        scroller.addEventListener('pointerup', endDrag);
        scroller.addEventListener('pointercancel', endDrag);
        scroller.addEventListener('click', function (e) {
            if (justDragged) {
                e.stopPropagation();
                e.preventDefault();
            }
        }, true);

        // --- 4. Boutons fléchés --------------------------------------------------------
        function pageStep() {
            return Math.max(120, Math.round(scroller.clientWidth * 0.8));
        }
        arrowLeft.addEventListener('click', function () {
            scroller.scrollBy({ left: -pageStep(), behavior: SMOOTH });
        });
        arrowRight.addEventListener('click', function () {
            scroller.scrollBy({ left: pageStep(), behavior: SMOOTH });
        });

        // --- 5. Indicateurs de débordement ------------------------------------------
        function updateIndicators() {
            var max = scroller.scrollWidth - scroller.clientWidth;
            var x = scroller.scrollLeft;
            wrap.toggleAttribute('data-overflow-left', x > 1);
            wrap.toggleAttribute('data-overflow-right', x < max - 1);
        }

        // --- 6. Auto-centrage de l'onglet actif -----------------------------------
        function centerActive(smooth) {
            var active = scroller.querySelector('.tab-btn-active');
            if (active && scroller.clientWidth > 0) {
                var a = active.getBoundingClientRect();
                var s = scroller.getBoundingClientRect();
                var delta = (a.left + a.width / 2) - (s.left + s.width / 2);
                if (Math.abs(delta) > 1) {
                    scroller.scrollBy({ left: delta, behavior: smooth ? SMOOTH : 'auto' });
                }
            }
            updateIndicators();
        }

        var raf = null;
        function scheduleCenter() {
            if (raf) cancelAnimationFrame(raf);
            raf = requestAnimationFrame(function () {
                raf = null;
                centerActive(true);
            });
        }

        scroller.addEventListener('scroll', updateIndicators, { passive: true });
        window.addEventListener('resize', updateIndicators);

        // .tab-btn-active bascule d'un bouton à l'autre à chaque changement d'onglet, quel que soit
        // le module (goToTab / switchTab / `tab = …`) : on recentre sur cette bascule.
        if (window.MutationObserver) {
            new MutationObserver(scheduleCenter).observe(scroller, {
                subtree: true, attributes: true, attributeFilter: ['class'], childList: true
            });
        }
        // La piste peut être montée cachée (x-cloak / x-show -> display:none) : sa taille passe de 0
        // à sa vraie valeur quand l'écran l'affiche. On recentre à ce moment-là.
        if (window.ResizeObserver) {
            new ResizeObserver(function () { centerActive(false); }).observe(scroller);
        }

        updateIndicators();
        requestAnimationFrame(function () { centerActive(false); });
        setTimeout(function () { centerActive(false); }, 300);
    }

    function scan(root) {
        if (root && root.querySelectorAll) {
            root.querySelectorAll('.tab-nav-scroll').forEach(enhance);
        }
    }

    function boot() {
        scan(document);
        if (!document.body || !window.MutationObserver) return;
        // Barres rendues après le chargement initial (modales, x-if, contenu injecté).
        new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    var n = added[j];
                    if (!n || n.nodeType !== 1) continue;
                    if (n.classList && n.classList.contains('tab-nav-scroll')) enhance(n);
                    scan(n);
                }
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
