/**
 * Garde d'écran de connexion — inutile de réafficher /login à quelqu'un qui a déjà une session
 * ouverte. Était un <script> INLINE dans Login.cshtml ; externalisé pour la même raison que
 * require-session.js : la Content-Security-Policy n'autorise pas script-src 'unsafe-inline'
 * (SecurityHeadersMiddleware) — c'est précisément cette directive qui neutralise un <script>
 * injecté par XSS, donc jamais d'exception ponctuelle plutôt que d'en écrire un nouveau.
 *
 * Chargé SANS defer, juste après auth.js : redirige avant le rendu du corps, comme le faisait
 * l'inline (pas de clignotement du formulaire de connexion avant la redirection).
 */
window.auth.redirectIfAuthenticated();
