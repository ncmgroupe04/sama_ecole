// Reproduit l'état « menu réduit » sur <html> AVANT le rendu du <body>.
//
// Sans ce script, au chargement d'une page en mode réduit, l'<aside> s'affichait d'abord à sa
// largeur dépliée (w-72, avec les libellés) le temps qu'Alpine hydrate son :class, puis se
// repliait d'un coup : le « flash » signalé. Script CLASSIQUE et SYNCHRONE, placé tout en haut du
// <head> (avant auth.js) : il s'exécute avant que le navigateur ne peigne la barre latérale.
//
// La classe .sidebar-collapsed posée ici sur <html> déclenche immédiatement les règles CSS de
// Styles/input.css (largeur, libellés masqués, icônes centrées…). Alpine prend ensuite le relais
// sans transition visible puisque la géométrie est déjà la bonne. Le bouton Réduire/Déplier
// (_Layout.cshtml) garde cette classe synchronisée avec l'état Alpine à chaque bascule.
//
// Réservé au desktop (>= lg / 1024px) : en dessous, la barre est un tiroir superposé, jamais un
// rail à réduire — on ne veut pas masquer ses libellés.
try {
  if (
    localStorage.getItem('sidebarCollapsed') === 'true' &&
    window.matchMedia('(min-width: 1024px)').matches
  ) {
    document.documentElement.classList.add('sidebar-collapsed');
  }
} catch (_) {
  // localStorage indisponible (mode privé strict, etc.) : on ignore, Alpine appliquera l'état.
}
