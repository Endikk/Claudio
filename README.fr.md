<div align="center">

# ✳︎ Claudio

**Tes vrais quotas Claude, sur ton bureau Windows.**

[![Téléchargements](https://img.shields.io/github/downloads/Endikk/Claudio/total?label=downloads&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Stars](https://img.shields.io/github/stars/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/stargazers)
[![Version](https://img.shields.io/github/v/release/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-11-black?style=flat-square)](https://www.microsoft.com/fr-fr/windows)
[![Licence](https://img.shields.io/badge/licence-MIT-black?style=flat-square)](LICENSE)

🇬🇧 [Read this README in English](README.md)

<p align="center"><img src="docs/claudio-typing.gif" width="132" alt="Claudio, la mascotte en pixel art, qui tape sur son ordinateur"></p>

<p align="center"><img src="docs/video-readme.fr.gif" width="620" alt="Claudio en une minute : la bande compacte, la carte d'usage et son détail, les onglets Sessions et Ports, l'île en haut de l'écran, la salle des agents où chacun a son bureau, et la mascotte à 100 %"></p>

</div>

Un widget Windows flottant pour ton usage de Claude. Sans bordure, toujours au-dessus, déplaçable,
en mode compact ou complet. Les jauges affichent les **vrais quotas** du compte — les mêmes
chiffres que claude.ai ▸ Usage et `/usage` — tandis que le détail des tokens vient des transcripts
locaux de Claude Code, sous Windows et dans WSL.

- **Des chiffres réels, ou aucun.** Les pourcentages viennent de la seule API d'Anthropic. Quand
  elle ne dit rien, les jauges affichent « — » plutôt qu'une estimation.
- **Onglet Sessions.** Chaque session Claude Code ouverte sur le PC, celles qui attendent ta
  réponse en premier, chacune avec ses agents. « Open » ramène au premier plan l'app où elle
  tourne ; « Interrupt » lui envoie Ctrl+C.
- **Salle des agents.** Une fenêtre à part : un open space par projet, chaque agent à un bureau
  dans l'espace de son travail, son écran montrant ce qu'il fait (code, tests, build, benchmark,
  infra…), et qui change de bureau quand son travail change. La ville à sa fenêtre s'allume et
  s'endort avec l'heure.
- **Notifications.** Session à 80 %, limite atteinte avant le reset, session revenue, une session
  qui t'attend, un agent silencieux ou terminé. Un interrupteur pour chacune.
- **Onglet Ports.** Liste les ports TCP laissés en écoute par Claude Code, sessions orphelines
  comprises, et les serveurs MCP qu'une session fermée a laissés tourner, et les ferme d'un clic.
  L'attribution lit les marqueurs Claude hérités dans l'environnement du process : rien d'autre
  sur le PC n'est listé.
- **Rien ne sort du PC.** Pas de télémétrie, pas de serveur tiers, aucune conversation envoyée.
  Le seul texte que Claudio lit dans tes conversations est la dernière demande de chaque session
  ouverte, affichée dans l'onglet Sessions. Les seules requêtes réseau vont à l'API d'Anthropic.

## Installer

```powershell
scoop bucket add claudio https://github.com/Endikk/scoop-claudio
scoop install claudio
```

<details>
<summary>Autres routes</summary>

**Installeur :** télécharge `Claudio-win-x64-Setup.exe` (ou `win-arm64`) depuis la
[dernière release](https://github.com/Endikk/Claudio/releases/latest) et lance-le. Claudio
s'installe pour toi seul, sans droits administrateur, pose son icône sur le Bureau et dans le menu
Démarrer, puis se met à jour tout seul.

Claudio n'est **pas signé** — distribution gratuite, pas de certificat de signature de code — donc
SmartScreen peut prévenir au premier lancement : *Informations complémentaires* ▸ *Exécuter quand
même*. Tu préfères éviter ? Compile toi-même ci-dessous, le code est court et auditable.

**Depuis les sources (Windows 11, SDK .NET 10) :**

```powershell
git clone https://github.com/Endikk/Claudio.git
cd Claudio
dotnet publish src/Claudio.App -c Release -p:Platform=x64 -r win-x64 -o publish
.\publish\Claudio.exe
```

winget n'est pas encore disponible.

</details>

## Utiliser

L'app n'a pas de fenêtre dans la barre des tâches. Elle s'affiche en carte flottante, en icône dans
la zone de notification près de l'horloge, ou en île en haut de l'écran. Un clic droit sur l'une
ou l'autre permet de la déplacer.

| Geste | Effet |
|---|---|
| Glisser la carte | Déplacer le widget |
| Clic sur la bande minimale | Passer en mode complet |
| `usage` / `ports` / `sessions` | Basculer entre les quotas, ce que Claude a laissé tourner et les sessions ouvertes |
| Clic droit | Rafraîchir · Mode · Emplacement (carte, zone de notification, haut de l'écran) · Notifications · Afficher ou masquer · Export CSV de l'usage · Connexion · Toujours au-dessus · Lancement au démarrage · Quitter |
| Ctrl+Alt+C, depuis n'importe quelle app | Afficher ou masquer Claudio |
| Ctrl+Alt+R, depuis n'importe quelle app | Ouvrir ou fermer la salle des agents |
| Survol de l'île | Ouvrir la session, son rythme et son reset, et les autres quotas sous l'île |
| Clic sur l'avatar | Carte du compte |
| Clic sur « Details » | Répartition par modèle et principaux projets |

Rafraîchissement toutes les 3 minutes, et immédiat au réveil du PC.

## Documentation

En anglais uniquement.

- [Fonctionnement](docs/how-it-works.md) — sources des données, invariants des quotas,
  passerelle statusline, repère de rythme, mode démonstration, vie privée.
- [Développement](docs/development.md) — lancer, produire l'installeur, structure du projet,
  contraintes de fenêtre à connaître avant d'y toucher.

## Historique des stars

<a href="https://www.star-history.com/?repos=Endikk%2FClaudio&type=date&releases=&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&theme=dark&logscale&legend=bottom-right" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&logscale&legend=bottom-right" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&logscale&legend=bottom-right" />
 </picture>
</a>

## Branches

| Branche | Rôle |
|---|---|
| `main` | Stable. Ce qui est publié et ce que Scoop installe. |
| `develop` | La branche vivante. Toute nouveauté y arrive d'abord et y reste tant qu'elle n'a pas servi pour de vrai ; `main` ne reçoit que ce qui a tenu. |

Les pull requests visent `develop`.

## Contribuer

Un bug, une idée, un chiffre qui ne correspond pas à claude.ai ? Ouvre une
[issue](https://github.com/Endikk/Claudio/issues) — une capture et
`%LOCALAPPDATA%\Claudio\api.log` sont les bienvenus. Les PR sont ouvertes.

MIT, maintenu par [@Endikk](https://github.com/Endikk).

<p align="center"><img src="docs/claudio-overload.gif" width="212" alt="Claudio à 100 % : l’ordinateur explose et la mascotte reste en cendres, les yeux en croix"></p>
