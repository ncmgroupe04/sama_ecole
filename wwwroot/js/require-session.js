/**
 * Garde de session des écrans applicatifs — ticket JGK-F01.
 *
 * Était un <script> INLINE dans _Layout.cshtml / SubscriptionPending.cshtml ; externalisé pour que la
 * Content-Security-Policy n'ait pas besoin de script-src 'unsafe-inline' (SecurityHeadersMiddleware) :
 * c'est précisément cette directive qui neutralise un <script> injecté par XSS.
 *
 * Chargé SANS defer, après auth.js : redirige vers /login avant le rendu du corps, comme le faisait
 * l'inline. Confort d'affichage, pas une protection — les données ne viennent que de l'API, qui exige
 * le JWT (voir PagesController).
 */
window.auth.requireSession();
