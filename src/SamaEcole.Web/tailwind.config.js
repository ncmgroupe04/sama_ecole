/** @type {import('tailwindcss').Config} */
module.exports = {
  // Analyse toutes les vues Razor + tout JS/TS ajouté plus tard dans wwwroot, ainsi que les Tag
  // Helpers (TagHelpers/*.cs) : ModalShellTagHelper compose ses classes Tailwind dans une chaîne C#,
  // pas dans un .cshtml — sans cette entrée, ces classes seraient absentes du CSS compilé.
  content: [
    "./Views/**/*.cshtml",
    "./Pages/**/*.cshtml",
    "./wwwroot/js/**/*.js",
    "./TagHelpers/**/*.cs"
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
      //
      // `primary` porte désormais une VRAIE rampe (50→900), pas juste un ton : les nouveaux
      // composants (StatCard, Badge…) ont besoin de teintes intermédiaires (ex. fond clair de
      // carte, bordure au survol) sans jamais sortir du token. `DEFAULT` reste #6366F1 : tout
      // `bg-primary`/`text-primary`/`border-primary`/`focus:ring-primary` déjà écrit dans les
      // vues continue de resolver EXACTEMENT à la même couleur qu'avant, aucune régression.
      // Les valeurs 50–900 sont la rampe indigo standard de Tailwind — 500 coïncide déjà avec
      // #6366F1, donc DEFAULT=500 n'introduit aucune dérive de teinte.
      //
      // success/warning/danger restent des couleurs SÉMANTIQUES (statut), pas des rampes de
      // marque : un seul ton + une variante `-bg` (fond clair, pour les badges pastilles).
      colors: {
        primary: {
          DEFAULT: "#6366F1",
          50: "#EEF2FF",
          100: "#E0E7FF",
          200: "#C7D2FE",
          300: "#A5B4FC",
          400: "#818CF8",
          500: "#6366F1",
          600: "#4F46E5",
          700: "#4338CA",
          800: "#3730A3",
          900: "#312E81"
        },
        success: { DEFAULT: "#1E8E3E", bg: "#EAF7EE" }, // Vert — validation, paiement effectué, abonnement actif
        warning: { DEFAULT: "#E8710A", bg: "#FDF0E4" }, // Orange — échéances proches, paiement partiel
        danger: { DEFAULT: "#D93025", bg: "#FBEAE9" },  // Rouge — erreurs, suppression, abonnement expiré/restreint
        neutral: "#6B7280"      // Gris — textes secondaires, séparateurs
      },
      fontFamily: {
        // Inter (variable, AUTO-HÉBERGÉE — voir les @font-face en tête de Styles/input.css), repli
        // système Segoe UI (Volume 5 §2.2). Elle était déjà déclarée ici mais jamais chargée : la
        // plateforme rendait en Segoe UI.
        sans: ["Inter", "Segoe UI", "system-ui", "sans-serif"],
        // Harmonisation demandée : `font-mono` ne bascule PLUS vers une chasse fixe. C'est la même
        // Inter que le reste de la plateforme ; les chiffres tabulaires (pour aligner les colonnes
        // de nombres — matricules, montants FCFA, n° de reçu, dates, ~22 vues) sont ajoutés via une
        // règle `.font-mono { font-variant-numeric: tabular-nums }` dans Styles/input.css. Une seule
        // redéfinition ici plutôt que 22 vues éditées, et tout futur `font-mono` hérite du bon rendu.
        mono: ["Inter", "Segoe UI", "system-ui", "sans-serif"]
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
