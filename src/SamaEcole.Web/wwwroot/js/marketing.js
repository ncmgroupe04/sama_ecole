/**
 * Comportements de la vitrine publique (/vitrine, /modules) — menu mobile, filtre de modules,
 * onglets, accordéons et apparition au défilement.
 *
 * POURQUOI UN FICHIER, ET PAS DU JAVASCRIPT DANS LES VUES ?
 * La CSP de l'application (SecurityHeadersMiddleware) ne comporte pas 'unsafe-inline' pour les
 * scripts : un <script> écrit dans une vue Razor serait rejeté par le navigateur, sans erreur
 * visible côté serveur. Tout comportement des pages publiques passe donc par ici.
 *
 * PRINCIPE DE CONCEPTION : le contenu est déjà dans le HTML rendu par le serveur. Ces composants ne
 * CONSTRUISENT rien — ils filtrent, déplient et révèlent du DOM existant. Une vitrine dont les
 * modules ne s'afficheraient qu'après exécution du JavaScript serait invisible pour un moteur de
 * recherche et vide pour un visiteur dont le script échoue à charger.
 *
 * Aucune dépendance : Alpine (déjà servi localement) et l'IntersectionObserver du navigateur.
 */
(function () {
    'use strict';

    /** Préférence système « animations réduites », relue à chaque appel (elle peut changer en cours de session). */
    function prefersReducedMotion() {
        return typeof window.matchMedia === 'function'
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // APPARITION AU DÉFILEMENT
    //
    // Hors d'Alpine, et volontairement : ces éléments doivent être VISIBLES même si Alpine ne se
    // charge pas. Le CSS pose donc l'état visible par défaut, et c'est ce script qui, une fois
    // certain de pouvoir animer, marque le document `data-reveal-ready` — ce qui active la règle
    // d'opacité initiale. Un échec de chargement laisse une page complète et lisible, pas blanche.
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    function initReveal() {
        var targets = document.querySelectorAll('[data-reveal]');
        if (!targets.length) return;

        // Sans IntersectionObserver (ou avec animations réduites), on ne fait rien : tout reste
        // affiché par le CSS de base. C'est la dégradation la plus sûre qui soit.
        if (prefersReducedMotion() || typeof window.IntersectionObserver !== 'function') return;

        document.documentElement.setAttribute('data-reveal-ready', 'true');

        var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) return;
                entry.target.setAttribute('data-revealed', 'true');
                // Une seule apparition par élément : rien ne doit « rejouer » au défilement inverse.
                observer.unobserve(entry.target);
            });
        }, { rootMargin: '0px 0px -10% 0px', threshold: 0.05 });

        targets.forEach(function (el) { observer.observe(el); });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initReveal);
    } else {
        initReveal();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // COMPOSANTS ALPINE
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    document.addEventListener('alpine:init', function () {

        /**
         * En-tête collant : tiroir mobile et état « la page a défilé » (qui pose l'ombre).
         *
         * Sur les pages publiques c'est bien la FENÊTRE qui défile — contrairement à l'application
         * connectée, où _Layout.cshtml confie le défilement à <main> et où window.scrollY reste
         * figé à 0 (voir backToTop dans help.js). Ne pas transposer ce composant tel quel.
         */
        Alpine.data('marketingNav', function () {
            return {
                open: false,
                scrolled: false,
                onScroll: null,
                onKey: null,

                init() {
                    this.onScroll = function () {
                        var next = window.scrollY > 8;
                        // Écriture seulement au changement : un écouteur de défilement se déclenche
                        // des dizaines de fois par seconde et réécrire la même valeur dans un proxy
                        // Alpine en réveille malgré tout les effets.
                        if (next !== this.scrolled) this.scrolled = next;
                    }.bind(this);

                    // Échap referme le tiroir : sans cela, un utilisateur au clavier se retrouve
                    // piégé dans un menu qu'aucune touche ne ferme.
                    this.onKey = function (event) {
                        if (event.key === 'Escape' && this.open) this.close();
                    }.bind(this);

                    window.addEventListener('scroll', this.onScroll, { passive: true });
                    document.addEventListener('keydown', this.onKey);
                    this.onScroll();
                },

                destroy() {
                    if (this.onScroll) window.removeEventListener('scroll', this.onScroll);
                    if (this.onKey) document.removeEventListener('keydown', this.onKey);
                },

                toggle() { this.open = !this.open; },
                close() { this.open = false; }
            };
        });

        /**
         * Explorateur de modules — filtre par catégorie et dépliage d'une carte.
         *
         * Le filtre agit par `x-show` sur des cartes DÉJÀ rendues par Razor : aucune carte n'est
         * créée ici, et la page reste complète sans JavaScript (toutes les catégories visibles).
         */
        Alpine.data('moduleExplorer', function (initial) {
            return {
                category: initial || 'tous',
                expanded: null,

                select(key) {
                    this.category = key;
                    // Une carte dépliée dans une catégorie que l'on quitte laisserait un trou :
                    // on replie systématiquement au changement de filtre.
                    this.expanded = null;
                },

                shows(cardCategory) {
                    return this.category === 'tous' || this.category === cardCategory;
                },

                toggle(slug) {
                    this.expanded = this.expanded === slug ? null : slug;
                },

                isExpanded(slug) { return this.expanded === slug; }
            };
        });

        /**
         * Onglets génériques (aperçu produit, profils utilisateurs). L'onglet actif est une simple
         * clé de chaîne ; les panneaux sont tous présents dans le DOM et alternés par `x-show`.
         */
        Alpine.data('marketingTabs', function (initial) {
            return {
                current: initial || '',

                select(key) { this.current = key; },
                isCurrent(key) { return this.current === key; },

                /**
                 * Flèches gauche/droite entre onglets — attendu d'un motif « tablist » (WAI-ARIA).
                 * Les onglets sont lus dans le DOM plutôt que dupliqués dans une liste JS, pour que
                 * l'ajout d'un onglet dans la vue ne demande aucune retouche ici.
                 */
                move(event, direction) {
                    var buttons = Array.prototype.slice.call(
                        this.$el.closest('[role="tablist"]').querySelectorAll('[role="tab"]'));
                    if (!buttons.length) return;

                    var index = buttons.indexOf(event.target);
                    if (index === -1) return;

                    var next = buttons[(index + direction + buttons.length) % buttons.length];
                    next.focus();
                    next.click();
                }
            };
        });

        /**
         * Grille tarifaire (#tarifs) : onglet principal (public / privé) + filtre de cycles du privé
         * (monocycle / bicycle / complexe). Les cartes sont déjà dans le HTML serveur — ici on ne fait
         * qu'alterner et filtrer. Changer d'onglet remet le filtre sur « tous », pour ne jamais arriver
         * sur un onglet dont le filtre masquerait toutes les cartes.
         */
        Alpine.data('marketingPricing', function (initialAudience) {
            return {
                audience: initialAudience || 'public',
                group: 'tous',

                selectAudience(key) {
                    this.audience = key;
                    this.group = 'tous';
                },
                isAudience(key) { return this.audience === key; },

                selectGroup(key) { this.group = key; },
                isGroup(key) { return this.group === key; },
                shows(key) { return this.group === 'tous' || this.group === key; },

                /** Flèches gauche/droite entre onglets (WAI-ARIA tablist), comme marketingTabs. */
                move(event, direction) {
                    var buttons = Array.prototype.slice.call(
                        event.target.closest('[role="tablist"]').querySelectorAll('[role="tab"]'));
                    var index = buttons.indexOf(event.target);
                    if (index === -1) return;

                    var next = buttons[(index + direction + buttons.length) % buttons.length];
                    next.focus();
                    next.click();
                }
            };
        });

        /**
         * Accordéon (FAQ, détail des modules). Un seul panneau ouvert à la fois : sur une FAQ, tout
         * déplier repousse la question suivante à plusieurs écrans de distance.
         */
        Alpine.data('marketingAccordion', function () {
            return {
                open: null,

                toggle(key) { this.open = this.open === key ? null : key; },
                isOpen(key) { return this.open === key; }
            };
        });
    });
})();
