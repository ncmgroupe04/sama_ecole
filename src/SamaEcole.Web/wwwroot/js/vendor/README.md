# Dépendances front auto-hébergées

> **PDF.js a été retiré (2026-08).** La modale d'aperçu PDF (`wwwroot/js/pdf-preview.js`) ne peint
> plus les pages dans un `<canvas>` : elle confie l'affichage à la **visionneuse PDF native du
> navigateur** via un `<iframe>` pointant sur une blob URL locale. Plus de moteur ~1,8 Mo à vendre,
> plus de worker, plus de polices standard. Historique dans le commit qui supprime
> `pdf.min.mjs` / `pdf.worker.min.mjs` / `pdfjs-standard-fonts/`.

## chart.min.js — Chart.js 4.4.7 (build UMD)

Graphique de projection (12 mois) de la console Super Admin (`superadmin-dashboard.js`). Auto-hébergé
pour la même raison qu'Alpine.js ci-dessous — CSP `script-src 'self'` (JGK-F01, `SecurityHeadersMiddleware`)
: un `<script src="https://cdn...">` y est de toute façon **bloqué par le navigateur**, CDN tiers ou
non, ce n'est donc pas seulement une question de cohérence mais de fonctionnement réel. Chargé en
`<script>` classique (pas de `defer`/`import()` différé) car la vue en a besoin dès `initializeChart()`,
appelé à la fin de `load()`.

Mettre à jour = remplacer le fichier par un commit relu, jamais par une résolution automatique :

```bash
curl -sL https://cdn.jsdelivr.net/npm/chart.js@<version>/dist/chart.umd.min.js \
  -o src/SamaEcole.Web/wwwroot/js/vendor/chart.min.js
```

Puis mettre à jour le numéro de version ci-dessus.

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
