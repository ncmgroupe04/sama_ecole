// Mode sombre : remappage central des couleurs (variables CSS + classe `.dark` sur <html>) — voir
// tailwind.dark-theme.js. Les vues gardent leurs classes habituelles (bg-white, text-slate-700…).
const darkTheme = require("./tailwind.dark-theme");

/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: "class",
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
      // Variantes sombres des couleurs de fond / texte / trait (tailwind.dark-theme.js).
      ...darkTheme.extend,
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
      // Design fluide 2026 : ombres teintées de bleu marine (jamais un gris neutre), une pour les
      // panneaux, une pour les aplats bleus (bouton principal, élément actif).
      // Arrondis très fins, uniformes sur toute la plateforme (demande produit : « presque
      // imperceptibles »). Redéfinir l'échelle ici change d'un coup les ~1000 usages de rounded-lg/xl/2xl/3xl
      // des vues, des composants et des Tag Helpers. `rounded-full` (avatars, pastilles, jauges) est conservé.
      borderRadius: {
        sm: "2px",
        DEFAULT: "3px",
        md: "4px",
        lg: "4px",
        xl: "5px",
        "2xl": "6px",
        "3xl": "6px"
      },
      boxShadow: {
        fluid: "0 1px 2px rgb(10 26 68 / 0.04), 0 14px 32px -22px rgb(21 52 140 / 0.16)",
        "fluid-blue": "0 8px 18px -8px rgb(0 23 75 / 0.55)"
      },
      colors: {
        primary: {
          // UNE seule couleur de marque : le marine du panneau de connexion (#00174B). 500 = 900 ; 600 est
          // l'état survol (un cran plus clair, #002B7A) et 700 l'état pressé. Les teintes 50 à 300 restent des
          // fonds clairs de pastilles et de survols.
          DEFAULT: "#00174B",
          50: "#EEF4FF",
          100: "#DBE1FF",
          200: "#C8D3FF",
          300: "#B4C5FF",
          400: "#5B76A8",
          500: "#00174B",
          600: "#002B7A",
          700: "#001239",
          800: "#00174B",
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
        // Cairo : glyphes arabes uniquement (unicode-range, voir Styles/input.css) — le latin reste en Inter.
        // Design fluide 2026 : Figtree (variable, AUTO-HÉBERGÉE) passe en tête ; Inter reste en repli
        // (glyphes hors latin étendu, italiques).
        sans: ["Figtree", "Inter", "Cairo", "Segoe UI", "system-ui", "sans-serif"],
        // Harmonisation demandée : `font-mono` ne bascule PLUS vers une chasse fixe. C'est la même
        // Inter que le reste de la plateforme ; les chiffres tabulaires (pour aligner les colonnes
        // de nombres — matricules, montants FCFA, n° de reçu, dates, ~22 vues) sont ajoutés via une
        // règle `.font-mono { font-variant-numeric: tabular-nums }` dans Styles/input.css. Une seule
        // redéfinition ici plutôt que 22 vues éditées, et tout futur `font-mono` hérite du bon rendu.
        mono: ["Figtree", "Inter", "Segoe UI", "system-ui", "sans-serif"],
        // Grilles de saisie RTL (notes des matières arabes) : Cairo EN TÊTE, Inter derrière. Cairo n'étant
        // déclarée que sur les blocs arabes (unicode-range), les chiffres et le latin restent rendus en Inter.
        arabic: ["Cairo", "Inter", "Segoe UI", "system-ui", "sans-serif"]
      },
      fontSize: {
        "jgk-title": ["22px", { lineHeight: "1.3" }],
        "jgk-subtitle": ["18px", { lineHeight: "1.4" }],
        "jgk-body": ["14px", { lineHeight: "1.5" }],
        "jgk-table": ["13px", { lineHeight: "1.4" }]
      }
    }
  },
  plugins: [darkTheme.plugin]
};
