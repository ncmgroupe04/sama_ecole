# Jangalekat.Web

Point d'entrée ASP.NET Core (API REST + MVC/Razor). Orchestration uniquement — aucune logique métier ici.

- `Controllers/` — contrôleurs API minces qui envoient une Command/Query à MediatR et traduisent le résultat en réponse HTTP conforme à openapi.yaml.
- `Views/` — vues Razor (voir docs/Volume_5_UIUX_Design.md pour le design system).
- `Styles/input.css` — source Tailwind CSS (Décision D-13). Ne jamais éditer `wwwroot/css/site.css` directement, il est généré par `npm run build:css`.
- `tailwind.config.js` — thème étendu (couleurs, typographie, points de rupture) aligné sur docs/Volume_5_UIUX_Design.md §2. Toute évolution du design system passe par ce fichier, jamais par du CSS ou des valeurs brutes ajoutées dans une vue.
- `wwwroot/` — assets statiques, y compris le CSS compilé.

Toute erreur renvoyée au client suit le format normalisé de docs/Volume_4_API_Design.md §0.4.

## Développement frontend

```bash
npm install --prefix .
npm run watch:css --prefix .   # recompile Tailwind en continu pendant le développement
```

