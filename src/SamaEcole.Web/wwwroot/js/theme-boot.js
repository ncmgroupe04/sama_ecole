// Applique le thème (clair / sombre) sur <html> AVANT le rendu du <body>.
//
// Sans ce script, une page chargée en mode sombre s'afficherait d'abord en clair le temps qu'Alpine démarre,
// puis basculerait d'un coup : un flash blanc très désagréable dans une pièce sombre. Même schéma que
// sidebar-boot.js : script CLASSIQUE et SYNCHRONE, tout en haut du <head> (la CSP interdit les scripts en
// ligne, donc un fichier externe). La classe `.dark` déclenche les variables de couleur de Styles/input.css
// (voir tailwind.dark-theme.js). theme.js (store Alpine « theme ») prend ensuite le relais pour le sélecteur.
//
// Préférence mémorisée dans le navigateur (localStorage « sama-theme » : light | dark | system). Valeur par
// défaut : « light » — le mode sombre est un choix explicite tant que tous les écrans n'ont pas été validés
// dessus ; passer DEFAULT à « system » (ici ET dans theme.js) pour suivre le thème du système par défaut.
try {
  var DEFAULT = 'light';
  var stored = localStorage.getItem('sama-theme');
  var mode = stored === 'light' || stored === 'dark' || stored === 'system' ? stored : DEFAULT;
  var dark = mode === 'dark' || (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  if (dark) document.documentElement.classList.add('dark');
} catch (_) {
  // localStorage indisponible (mode privé strict…) : thème clair, le sélecteur reste utilisable pour la session.
}
