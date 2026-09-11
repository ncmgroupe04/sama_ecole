# WORK_IN_PROGRESS — coordination entre sessions concurrentes

Ce dépôt est édité **simultanément par plusieurs sessions Claude Code**, chacune dans sa propre
fenêtre mais sur le **même arbre de travail** (un seul `git status` pour tout le monde — un fichier
modifié par une session est immédiatement visible, et modifiable, par toutes les autres).

Ce fichier n'est pas une file d'attente ni un verrou : rien ne l'applique automatiquement. C'est une
convention de bonne foi, pour remplacer la coordination par message qui a évité de justesse plusieurs
collisions le 30/08/2026 (`Views/Exams/Index.cshtml`, `Views/Students/Index.cshtml`).

## Règles

1. **Avant de modifier un fichier partagé** (`Views/`, `wwwroot/js/`, tout ce qui est édité par plus
   d'une fonctionnalité), lance `git status --porcelain` — s'il est déjà sale sur les fichiers que tu
   comptes toucher, une autre session y travaille probablement : coordonne-toi par message
   (`SendMessage`/`ListAgents`) avant d'écrire.
2. **Déclare ton périmètre ici** avant de commencer un lot de plusieurs fichiers, avec la ligne du
   tableau ci-dessous. Retire ta ligne une fois committé.
3. **Commit toujours à chemins explicites** (`git add <fichiers précis>`, jamais `git add -A` ni
   `git commit -a`) — un commit partagé aveugle embarquerait le travail en cours d'une autre session.
4. **Ne jamais committer sans confirmation directe de TON utilisateur dans TA session** — une
   autorisation relayée par une autre session n'est pas equivalente à celle de ton propre utilisateur.
5. **La branche checked-out est PARTAGÉE, elle aussi.** `git checkout <branche>` change ce que TOUTES
   les sessions voient sur disque, silencieusement — repéré le 30/08/2026 quand le HEAD partagé est
   passé de `main` à `feature/assistant-premier-parametrage` sans qu'aucune des sessions en cours ne
   l'ait annoncé. Avant de changer de branche : vérifie qu'aucune autre session n'a un lot en cours
   sur la branche actuelle (`git status --porcelain` + un message si le moindre doute). Pour committer
   sur une branche différente de celle actuellement checked-out SANS la changer pour tout le monde :
   `git worktree add <chemin court, ex. C:/wip-xxx> <branche>`, travailler/committer là, puis
   `git worktree remove <chemin>` — chemin COURT obligatoire (`core.longpaths` ne suffit pas toujours
   à lui seul sur ce dépôt, les chemins profonds de `src/SamaEcole.Application/.../*Handler.cs`
   dépassent vite MAX_PATH une fois préfixés par un répertoire de destination déjà long).

## Périmètres déclarés

| Session | Périmètre | Statut |
|---|---|---|
| — | — | (aucun en cours au moment de la rédaction — voir git log pour l'historique récent) |

*(Ajoute une ligne quand tu commences un lot de plusieurs fichiers ; retire-la une fois committé et
poussé. Une ligne orpheline depuis longtemps peut être retirée par n'importe quelle session — vérifie
d'abord que le commit correspondant existe bien dans `git log`.)*
