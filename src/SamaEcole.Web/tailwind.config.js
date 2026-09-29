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
      // Palette alignée sur le design system Unikol 2026 (bleu Unikol, plus l'indigo
      // Tailwind générique) — un agent qui a besoin d'une de ces couleurs utilise la
      // classe utilitaire (ex. bg-primary), jamais un code hexadécimal en dur dans une vue.
      //
      // `primary` porte une VRAIE rampe (50→900) : les composants (StatCard, Badge…) ont
      // besoin de teintes intermédiaires (ex. fond clair de carte, bordure au survol) sans
      // jamais sortir du token.
      //
      // Rampe bleu Unikol (remplace l'indigo générique Tailwind qui l'a précédée) : chaque
      // valeur vient du design system officiel (tokens `primary` / `primary-container` /
      // `primary-fixed*` / `on-primary-fixed*`), pas d'une teinte Tailwind stock.
      //   500 DEFAULT = primary-container #2563EB — bouton/lien standard, contraste blanc 5.17:1 (AA)
      //   600         = primary #004AC6 — variante haute-lisibilité (pastille active sous texte
      //                 blanc, hover), contraste blanc 7.52:1 (AAA)
      //   700         = on-primary-fixed-variant #003EA8 — état pressé/actif
      //   900         = on-primary-fixed #00174B — le bleu marine le plus sombre du design system
      //   100 / 300   = primary-fixed #DBE1FF / primary-fixed-dim #B4C5FF — teintes claires
      //                 (fond de carte, bordure au survol)
      //   50/200/400/800 sont interpolés entre les stops officiels ci-dessus pour obtenir
      //   une rampe Tailwind complète et régulière.
      //
      // success/warning/danger restent des couleurs SÉMANTIQUES (statut), pas des rampes de
      // marque : un seul ton + une variante `-bg` (fond clair, pour les badges pastilles).
      //
      // success/danger/neutral alignés sur le design system officiel (tokens `tertiary` /
      // `error` / `secondary`) — comme `primary`, plus de teinte Tailwind stock ici :
      //   success = tertiary #006242, bg = on-tertiary-container #BDFFDB
      //   danger  = error #BA1A1A,    bg = error-container #FFDAD6
      //   neutral = secondary #565E74
      // `warning` n'a pas d'équivalent dans le design system (pas de token ambre officiel) :
      // conservé tel quel, seule couleur de ce groupe qui reste une teinte Tailwind ad hoc.
      colors: {
        primary: {
          DEFAULT: "#2563EB",
          50: "#EEF4FF",
          100: "#DBE1FF",
          200: "#C8D3FF",
          300: "#B4C5FF",
          400: "#6D94F5",
          500: "#2563EB",
          600: "#004AC6",
          700: "#003EA8",
          800: "#002B7A",
          900: "#00174B"
        },
        success: { DEFAULT: "#006242", bg: "#BDFFDB" }, // Vert — validation, paiement effectué, abonnement actif
        warning: { DEFAULT: "#E8710A", bg: "#FDF0E4" }, // Orange — échéances proches, paiement partiel (pas de token officiel)
        danger: { DEFAULT: "#BA1A1A", bg: "#FFDAD6" },  // Rouge — erreurs, suppression, abonnement expiré/restreint
        neutral: "#565E74"      // Gris — textes secondaires, séparateurs
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
