/**
 * MODE SOMBRE — remappage CENTRAL des couleurs (aucune vue n'a besoin de classes `dark:`).
 *
 * Principe : les vues écrivent « bg-white », « text-slate-700 », « border-slate-200 »… (≈ 2 900 usages dans
 * 84 fichiers). Plutôt que de doubler chacune d'un `dark:`, on redéfinit ICI la valeur de ces mêmes classes :
 * chaque couleur concernée devient `rgb(var(--…) / <alpha-value>)`, et la classe `.dark` (posée sur <html> par
 * wwwroot/js/theme-boot.js) échange les variables. Les opacités (`bg-white/85`, `border-white/60`) continuent
 * de fonctionner, car `<alpha-value>` est conservé.
 *
 * Trois jeux de variables, car une même teinte n'a pas le même sens selon l'usage :
 *   - « fond »   (bg, dégradés, fill)          : les teintes claires (50–300) deviennent sombres ; les aplats
 *                                                soutenus (600+) restent — un bouton `bg-emerald-600 text-white`
 *                                                doit rester plein sous son texte blanc.
 *   - « encre »  (text, placeholder)           : les teintes foncées (600–900) deviennent claires ; les
 *                                                teintes claires (50–300) restent — elles servent déjà de texte
 *                                                clair sur fond soutenu.
 *   - « trait »  (border, divide, ring, stroke) : comme « fond », en un peu plus clair que la surface.
 *
 * Neutres (slate/gray/zinc/stone) — hiérarchie des surfaces en sombre :
 *   page `bg-*-50` → 950   ·   carte `bg-white` → 900   ·   fond discret `bg-*-100` → 800
 * `text-white` et `bg-black/…` ne bougent pas.
 *
 * Une teinte n'est transformée en variable QUE si elle change en sombre : le CSS reste léger et les valeurs
 * inchangées gardent leur hexadécimal d'origine.
 */
const plugin = require("tailwindcss/plugin");
const colors = require("tailwindcss/colors");

const SHADES = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950];

const toTriple = (hex) => {
  let h = hex.replace("#", "");
  if (h.length === 3) h = h.split("").map((c) => c + c).join("");
  const n = parseInt(h, 16);
  return `${(n >> 16) & 255} ${(n >> 8) & 255} ${n & 255}`;
};

/** Marque de la plateforme — rampe officielle (voir tailwind.config.js) + un 950 ajouté pour le sombre. */
const PRIMARY = {
  50: "#EEF4FF", 100: "#DBE1FF", 200: "#C8D3FF", 300: "#B4C5FF", 400: "#6D94F5",
  500: "#2563EB", 600: "#004AC6", 700: "#003EA8", 800: "#002B7A", 900: "#00174B", 950: "#0A1638"
};

const NEUTRALS = ["slate", "gray", "zinc", "stone"];
const HUES = ["red", "rose", "orange", "amber", "yellow", "green", "emerald", "teal", "cyan", "sky", "blue", "indigo", "violet", "purple", "pink"];

// shade clair → shade (de la même famille) utilisé en sombre. Les shades absentes ne changent pas.
const MAPS = {
  neutral: {
    fond: { 50: 950, 100: 800, 200: 700, 300: 600, 800: 600, 900: 700 },
    encre: { 300: 600, 400: 500, 500: 400, 600: 300, 700: 200, 800: 100, 900: 50, 950: 50 },
    trait: { 50: 900, 100: 800, 200: 700, 300: 600, 400: 500 }
  },
  hue: {
    fond: { 50: 950, 100: 900, 200: 800, 300: 700 },
    encre: { 600: 400, 700: 300, 800: 200, 900: 100, 950: 50 },
    trait: { 50: 950, 100: 900, 200: 800, 300: 700 }
  }
};

const primaryDark = {
  fond: { 50: "#0A1638", 100: PRIMARY[900], 200: PRIMARY[800], 300: PRIMARY[700] },
  encre: { 500: "#6D94F5", 600: "#8EAAF7", 700: "#B4C5FF", 800: "#C8D3FF", 900: "#DBE1FF" },
  trait: { 50: "#0A1638", 100: PRIMARY[900], 200: PRIMARY[800], 300: PRIMARY[700] }
};

const GROUPS = {
  fond: ["backgroundColor", "gradientColorStops", "fill"],
  encre: ["textColor", "placeholderColor"],
  trait: ["borderColor", "divideColor", "ringColor", "stroke"]
};

/** Construit { light, dark, theme } : variables CSS + entrées `theme.extend.<propriété>`. */
function build() {
  const light = {};
  const dark = {};
  const theme = {};
  for (const props of Object.values(GROUPS)) for (const p of props) theme[p] = {};

  const put = (group, family, shadeKey, lightHex, darkHex) => {
    const name = `--${group}-${family}-${shadeKey}`;
    const value = darkHex ? `rgb(var(${name}) / <alpha-value>)` : lightHex;
    if (darkHex) {
      light[name] = toTriple(lightHex);
      dark[name] = toTriple(darkHex);
    }
    for (const p of GROUPS[group]) {
      theme[p][family] = theme[p][family] || {};
      theme[p][family][shadeKey] = value;
    }
  };

  const scaleFamily = (family, scale, kind) => {
    for (const group of Object.keys(GROUPS)) {
      for (const shade of SHADES) {
        if (!scale[shade]) continue;
        const target = MAPS[kind][group][shade];
        // Seules les shades transformées sont émises : les autres héritent du thème par défaut.
        if (target === undefined) continue;
        // Neutres : TOUTES les familles (zinc, gray, stone…) visent l'échelle slate en sombre, pour que fond de
        // page, cartes et traits partagent une même teinte bleutée quelle que soit la famille écrite dans la vue.
        const source = kind === "neutral" ? colors.slate : scale;
        put(group, family, shade, scale[shade], source[target]);
      }
    }
  };

  NEUTRALS.forEach((f) => scaleFamily(f, colors[f], "neutral"));
  HUES.forEach((f) => scaleFamily(f, colors[f], "hue"));

  // `primary` : rampe de marque, cibles en hexadécimal (pas de 950 officiel).
  for (const group of Object.keys(GROUPS)) {
    for (const [shade, darkHex] of Object.entries(primaryDark[group])) {
      put(group, "primary", shade, PRIMARY[shade], darkHex);
    }
  }
  // text-primary (sans suffixe) = DEFAULT = 500.
  theme.textColor.primary.DEFAULT = theme.textColor.primary[500];
  theme.placeholderColor.primary.DEFAULT = theme.placeholderColor.primary[500];

  // Couleurs SÉMANTIQUES de statut (jetons du design system) : l'aplat DEFAULT reste (bouton plein sous texte
  // blanc), le texte s'éclaircit, le fond clair `-bg` (pastille) devient une teinte sombre de la même couleur.
  const semantic = {
    success: { text: "#34D399", bg: { light: "#BDFFDB", dark: "#0A3D2C" } },
    warning: { text: "#FB923C", bg: { light: "#FDF0E4", dark: "#3A2208" } },
    danger: { text: "#F87171", bg: { light: "#FFDAD6", dark: "#451A18" } }
  };
  const base = { success: "#006242", warning: "#E8710A", danger: "#BA1A1A" };
  for (const [name, s] of Object.entries(semantic)) {
    // fond : `bg-danger-bg`
    const bgVar = `--fond-${name}-bg`;
    light[bgVar] = toTriple(s.bg.light);
    dark[bgVar] = toTriple(s.bg.dark);
    for (const p of GROUPS.fond) {
      theme[p][name] = { bg: `rgb(var(${bgVar}) / <alpha-value>)` };
    }
    // encre : `text-danger`
    const txVar = `--encre-${name}-DEFAULT`;
    light[txVar] = toTriple(base[name]);
    dark[txVar] = toTriple(s.text);
    for (const p of GROUPS.encre) {
      theme[p][name] = { DEFAULT: `rgb(var(${txVar}) / <alpha-value>)` };
    }
  }

  // Blanc : carte (`bg-white`) → slate-900 ; trait (`border-white/60`, ring) → slate-700. `text-white` inchangé.
  const whites = [
    ["fond", "--fond-white", "#FFFFFF", colors.slate[900]],
    ["trait", "--trait-white", "#FFFFFF", colors.slate[700]]
  ];
  for (const [group, name, l, d] of whites) {
    light[name] = toTriple(l);
    dark[name] = toTriple(d);
    for (const p of GROUPS[group]) theme[p].white = `rgb(var(${name}) / <alpha-value>)`;
  }

  return { light, dark, theme };
}

const built = build();

module.exports = {
  /** À étendre dans `theme.extend` (backgroundColor, textColor, borderColor…). */
  extend: built.theme,
  /** Plugin : déclare les variables (`:root` = clair, `.dark` = sombre) et `color-scheme`. */
  plugin: plugin(({ addBase }) => {
    addBase({
      ":root": built.light,
      ".dark": { ...built.dark, colorScheme: "dark" }
    });
  })
};
