# Dépendances front auto-hébergées

## pdf.min.mjs / pdf.worker.min.mjs — PDF.js 6.1.200 (build "legacy")

Moteur de rendu de la modale d'aperçu PDF partagée (`wwwroot/js/pdf-preview.js`), qui peint les pages
dans des `<canvas>` plutôt que de confier l'affichage à un `<iframe src="blob:…">` — cette dernière
approche échouait silencieusement selon le navigateur (viewer PDF interne désactivé, extension de
blocage, restriction mobile) sans qu'aucune cause ne remonte à l'application.

Auto-hébergé pour la même raison qu'Alpine.js (CSP `script-src 'self'`, JGK-F01) — voir plus bas.
Chargé à la demande (`import()` dynamique au premier aperçu, jamais au chargement de la page) : les
~500 Ko du module + ~1,3 Mo du worker seraient hors de propos sur une connexion mobile sénégalaise
moyenne (Volume 5 §1) pour un écran qui n'ouvre peut-être aucun PDF dans la session.

Build **"legacy"** du paquet (`legacy/build/…`, pas `build/…`) : le build standard cible les
navigateurs très récents (ES2020+ strict) et refuse de s'initialiser sur un Android WebView ou un
Safari mobile un peu daté — exactement le parc que ce projet doit couvrir (Volume 5 §1). Le build
legacy est ~10 % plus lourd, compensé par le chargement à la demande ci-dessus.

`pdfjs-standard-fonts/` (dossier voisin) fournit les 14 polices PDF standard (Times/Helvetica/Courier
et variantes) pour les documents qui les référencent sans les embarquer — chargé à la demande par
PDF.js lui-même, jamais au démarrage. Les `cmaps/` (polices CJK) ne sont PAS vendus : aucun document
généré par QuestPDF n'en a besoin (polices Times New Roman / Arial embarquées, alphabet latin).

Mettre à jour = remplacer les deux fichiers par un commit relu :

```bash
npm pack pdfjs-dist@<version>
tar -xzf pdfjs-dist-<version>.tgz
cp package/legacy/build/pdf.min.mjs package/legacy/build/pdf.worker.min.mjs src/SamaEcole.Web/wwwroot/js/vendor/
cp package/standard_fonts/* src/SamaEcole.Web/wwwroot/js/vendor/pdfjs-standard-fonts/
```

Puis mettre à jour `PDFJS_VERSION` dans `wwwroot/js/pdf-preview.js` et le numéro de version ci-dessus.

## alpine.min.js — Alpine.js 3.15.12

Servi depuis le projet, et non depuis un CDN, volontairement : l'écran de connexion reçoit les mots
de passe de toutes les écoles. Un script tiers chargé depuis un domaine que nous ne contrôlons pas
— qui plus est en version flottante (`alpinejs@3.x.x` : n'importe quelle future 3.x, jamais relue)
— pourrait les exfiltrer si le CDN venait à être compromis. L'auto-hébergement supprime aussi une
dépendance réseau externe au chargement, ce qui compte sur une connexion mobile sénégalaise
moyenne (Volume 5 §1).

Mettre à jour = remplacer le fichier par un commit relu, jamais par une résolution automatique :

```bash
curl -sL https://cdn.jsdelivr.net/npm/alpinejs@<version>/dist/cdn.min.js \
  -o src/SamaEcole.Web/wwwroot/js/vendor/alpine.min.js
```

Puis mettre à jour le numéro de version ci-dessus.
