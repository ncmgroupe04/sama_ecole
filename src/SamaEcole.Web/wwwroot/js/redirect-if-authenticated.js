/**
 * Écran de connexion : inutile de le réafficher à qui a déjà une session — ticket JGK-F01.
 *
 * Était un <script> INLINE dans Login.cshtml ; externalisé pour que la Content-Security-Policy n'ait
 * pas besoin de script-src 'unsafe-inline' (SecurityHeadersMiddleware). Chargé SANS defer, après
 * auth.js : la redirection part avant le rendu du formulaire, sans clignotement.
 */
window.auth.redirectIfAuthenticated();
