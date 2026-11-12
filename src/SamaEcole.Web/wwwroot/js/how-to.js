/**
 * Bouton « Comment faire ? » — aide contextuelle sur TOUS les écrans.
 *
 * Idée (demande produit) : là où un réglage est compliqué, un bouton bien repérable ouvre une fenêtre qui
 * explique la procédure, comme « Comment sont appliqués les coefficients ? ». Plutôt que de recopier du texte
 * dans chaque écran, ce store réutilise le Centre d'aide existant (help.js : 85 articles, chacun avec sa
 * page `href`, sa procédure et ses recommandations) : un seul contenu à maintenir, toujours cohérent avec /aide.
 *
 * Chargement PARESSEUX : help.js est lourd, il n'est téléchargé qu'au premier clic (le Centre d'aide
 * publie ses données dans window.HELP_SECTIONS). Sans article pour la page courante, la fenêtre propose le
 * Centre d'aide plutôt qu'un message vide.
 */
(function () {
    'use strict';

    /** Chemin d'un href d'article, sans la chaîne de requête (« /parametres?tab=securite » → « /parametres »). */
    const pathOf = (href) => String(href || '').split('?')[0];

    /**
     * Guides transverses (suppression guidée, abonnement…) : utiles mais rarement ce que l'on cherche en ouvrant
     * l'aide d'un écran. Ils passent EN DERNIER dans la liste d'une page.
     */
    const LATE = ['suppression-guidee', 'mode-sandbox-golive', 'comprendre-abonnement', 'profil-onboarding-quota',
        'tranches-prix-verrouillage', 'paiement-wave-renouvellement-acces', 'activer-desactiver-module'];

    /** Valeur du paramètre `tab` d'un href d'article, ou ''. */
    const tabOf = (href) => {
        const query = String(href || '').split('?')[1] || '';
        return new URLSearchParams(query).get('tab') || '';
    };

    /**
     * Articles du Centre d'aide qui concernent la page courante. PURE : testable sans navigateur.
     * - même chemin (ou sous-chemin : /classes/12 → /classes) ;
     * - si l'URL porte ?tab=…, les articles de CET onglet passent devant ; sinon ceux sans onglet.
     */
    function matchArticles(sections, pathname, search) {
        const current = String(pathname || '').replace(/\/+$/, '') || '/';
        const tab = new URLSearchParams(search || '').get('tab') || '';
        const found = [];
        (sections || []).forEach((section) => {
            (section.articles || []).forEach((article) => {
                const path = pathOf(article.href).replace(/\/+$/, '');
                if (!path || path === '/aide') return;
                if (current === path || current.startsWith(path + '/')) {
                    found.push({ article, section, tabMatch: tab !== '' && tabOf(article.href) === tab, hasTab: tabOf(article.href) !== '' });
                }
            });
        });
        const late = (f) => Number(LATE.includes(f.article.id));
        found.sort((a, b) => (late(a) - late(b)) || (Number(b.tabMatch) - Number(a.tabMatch)) || (Number(a.hasTab) - Number(b.hasTab)));
        return found.map((f) => f.article);
    }

    /** Article d'identifiant donné, ou null. */
    function findArticle(sections, id) {
        for (const section of sections || []) {
            for (const article of section.articles || []) {
                if (article.id === id) return article;
            }
        }
        return null;
    }

    window.howToMatch = matchArticles;
    window.howToFind = findArticle;

    let loading = null;
    function loadHelpData() {
        if (window.HELP_SECTIONS) return Promise.resolve(window.HELP_SECTIONS);
        if (loading) return loading;
        loading = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = '/js/help.js';
            script.onload = () => (window.HELP_SECTIONS ? resolve(window.HELP_SECTIONS) : reject(new Error('données d\'aide absentes')));
            script.onerror = () => reject(new Error('chargement de l\'aide impossible'));
            document.head.appendChild(script);
        });
        loading.catch(() => { loading = null; });
        return loading;
    }

    document.addEventListener('alpine:init', () => {
        Alpine.store('howTo', {
            visible: false,
            loading: false,
            failed: false,
            articles: [],
            index: 0,

            get article() { return this.articles[this.index] || null; },
            get hasArticle() { return this.articles.length > 0; },
            /** Étapes de la procédure (tableau de textes). */
            get steps() { return (this.article && this.article.procedure) || []; },
            /** Les trois premières recommandations : « à retenir ». */
            get tips() { return ((this.article && this.article.recommandations) || []).slice(0, 3); },

            select(i) { this.index = i; },
            /** Liaison de la liste déroulante (select-field) : valeur texte <-> index. */
            get pick() { return String(this.index); },
            set pick(value) { this.index = Number(value) || 0; },
            dismiss() { this.visible = false; },

            /** Ouvre UN article précis du Centre d'aide (bouton spécifique d'une fonctionnalité, voir HowToButtonTagHelper). */
            async showArticle(id) {
                this.visible = true;
                this.failed = false;
                this.index = 0;
                this.loading = true;
                try {
                    const sections = await loadHelpData();
                    const found = findArticle(sections, id);
                    this.articles = found ? [found] : matchArticles(sections, window.location.pathname, window.location.search);
                } catch (err) {
                    this.failed = true;
                    this.articles = [];
                } finally {
                    this.loading = false;
                }
            },

            async show() {
                this.visible = true;
                this.failed = false;
                this.index = 0;
                this.loading = true;
                try {
                    const sections = await loadHelpData();
                    this.articles = matchArticles(sections, window.location.pathname, window.location.search);
                } catch (err) {
                    this.failed = true;
                    this.articles = [];
                } finally {
                    this.loading = false;
                }
            }
        });
    });
})();
