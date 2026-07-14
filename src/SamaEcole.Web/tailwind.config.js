/** @type {import('tailwindcss').Config} */
module.exports = {
  // Analyse toutes les vues Razor + tout JS/TS ajouté plus tard dans wwwroot.
  content: [
    "./Views/**/*.cshtml",
    "./Pages/**/*.cshtml",
    "./wwwroot/js/**/*.js"
  ],
  theme: {
    // Points de rupture alignés sur docs/Volume_5_UIUX_Design.md §2.4 — ne pas redéfinir
    // ces valeurs ailleurs (pas de media query brute dans les vues).
    screens: {
      sm: "600px",  // Tablette (Volume 5 §2.4)
      lg: "1024px"  // Desktop (Volume 5 §2.4)
    },
    extend: {
      // Palette alignée sur docs/Volume_5_UIUX_Design.md §2.1 et docs/design-references/
      // (dashboard-reference.jpg fait foi pour la couleur principale) — un agent qui a
      // besoin d'une de ces couleurs utilise la classe utilitaire (ex. bg-primary), jamais
      // un code hexadécimal en dur dans une vue.
      colors: {
        primary: "#6366F1",     // Violet/Indigo — menus, boutons principaux, logo (aligné sur dashboard-reference.jpg)
        success: "#1E8E3E",     // Vert — validation, paiement effectué, abonnement actif
        warning: "#E8710A",     // Orange — échéances proches, paiement partiel
        danger: "#D93025",      // Rouge — erreurs, suppression, abonnement expiré/restreint
        neutral: "#6B7280"      // Gris — textes secondaires, séparateurs
      },
      fontFamily: {
        // Police Inter avec repli système Segoe UI (Volume 5 §2.2)
        sans: ["Inter", "Segoe UI", "system-ui", "sans-serif"]
      },
      fontSize: {
        "jgk-title": ["22px", { lineHeight: "1.3" }],
        "jgk-subtitle": ["18px", { lineHeight: "1.4" }],
        "jgk-body": ["14px", { lineHeight: "1.5" }],
        "jgk-table": ["13px", { lineHeight: "1.4" }]
      }
    }
  },
  plugins: []
};
