# Dépendances front auto-hébergées

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
