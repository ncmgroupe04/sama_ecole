// Ferme tous les sous-menus du menu latéral de façon SYNCHRONE dès qu'un lien principal est
// cliqué (bascule d'accordéon ou navigation), avant de rouvrir la seule cible visée sur la frame
// suivante. Sans ce verrou, cliquer rapidement d'une section à l'autre laisse la transition
// max-height précédente en cours pendant le reflow du sous-menu suivant : effet d'étirage/saut
// fantôme (ex. Comptabilité qui se déplie/replie tout seul). Voir Styles/input.css pour les
// règles [data-submenu] / .is-resetting qui portent l'animation.
document.addEventListener('DOMContentLoaded', () => {
  const sidebarNav = document.querySelector('.sidebar-nav');
  const mainLinks = document.querySelectorAll('.sidebar-link-main');
  const submenus = document.querySelectorAll('[data-submenu]');

  mainLinks.forEach(link => {
    link.addEventListener('click', () => {
      const targetSubmenu = link.nextElementSibling;
      const isTargetSubmenu = targetSubmenu && targetSubmenu.hasAttribute('data-submenu');

      // Capturé AVANT de tout refermer : un reclic sur le sous-menu DÉJÀ ouvert doit le
      // laisser fermé (bascule), pas le rouvrir aussitôt — sans cette lecture précoce, le
      // menu actif se comportait comme s'il n'y avait qu'un état "ouvert", jamais "fermé".
      const wasOpen = isTargetSubmenu && !targetSubmenu.classList.contains('is-closed');

      sidebarNav?.classList.add('is-resetting');

      submenus.forEach(menu => {
        menu.classList.add('is-closed');
        menu.style.maxHeight = '0px';
      });

      if (wasOpen) {
        // Bascule vers fermé : rien à rouvrir, juste lever le verrou de transition posé
        // ci-dessus pour que le prochain clic (sur ce menu ou un autre) anime normalement.
        requestAnimationFrame(() => sidebarNav?.classList.remove('is-resetting'));
        return;
      }

      requestAnimationFrame(() => {
        // Lever le verrou AVANT de lire scrollHeight : tant que .is-resetting force
        // display:none sur ce sous-menu, sa hauteur réelle est nulle (un élément caché
        // n'a pas de boîte de layout). Le lire après le setTimeout(50) mesurait donc
        // toujours 0 et le sous-menu restait invisible (max-height:0) après un
        // changement rapide de section — bug constaté en testant le scénario de clics
        // rapides décrit dans le rapport.
        sidebarNav?.classList.remove('is-resetting');

        if (isTargetSubmenu) {
          targetSubmenu.classList.remove('is-closed');
          // Force un recalcul de style : sans ce point de mesure intermédiaire, le
          // dévoilement (display:none -> block) et la fixation de la hauteur finale se
          // fondent en un seul recalcul et la transition max-height ne se joue pas.
          void targetSubmenu.offsetHeight;
          targetSubmenu.style.maxHeight = targetSubmenu.scrollHeight + 'px';
        }
      });
    });
  });
});
