/**
 * Modale UNIVERSELLE « Accès refusé » — montée une seule fois par _Layout (_AccessDeniedModal.cshtml).
 *
 * Tout 403 dont le corps normalisé porte le code FORBIDDEN — c.-à-d. une ForbiddenException côté
 * serveur : compte non rattaché à une fiche enseignant, classe/matière non assignée, créneau d'un
 * collègue… (docs/Volume_4_API_Design.md §22) — ouvre CETTE modale, avec le message d'action
 * corrective renvoyé par l'API, au lieu d'être rendu en bandeau rouge par chaque écran. window.api
 * marque alors l'erreur `handledGlobally` : toMessage()/toFieldErrors() renvoient vide, donc le
 * bandeau (ou le toast) local de l'écran ne s'affiche pas en double.
 *
 * Le pont passe par un évènement `window` : api.js est un script classique exécuté avant Alpine, il
 * ne peut pas toucher au store au moment où il tourne.
 */
document.addEventListener('alpine:init', () => {
    Alpine.store('accessDenied', {
        message: null,
        show(message) {
            this.message = message || "Vous n'avez pas l'autorisation d'effectuer cette action.";
        },
        clear() {
            this.message = null;
        }
    });

    window.addEventListener('sama:access-denied', (event) => {
        Alpine.store('accessDenied').show(event.detail && event.detail.message);
    });
});
