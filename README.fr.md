<div align="center">

# ✳︎ Claudio

**Tes vrais quotas Claude, sur ton bureau Windows.**

[![Téléchargements](https://img.shields.io/github/downloads/Endikk/Claudio/total?label=downloads&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Stars](https://img.shields.io/github/stars/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/stargazers)
[![Version](https://img.shields.io/github/v/release/Endikk/Claudio?include_prereleases&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Windows](https://img.shields.io/badge/Windows-11-black?style=flat-square)](https://www.microsoft.com/fr-fr/windows)
[![Licence](https://img.shields.io/badge/licence-MIT-black?style=flat-square)](LICENSE)

🇬🇧 [Read this README in English](README.md)

<p align="center"><img src="docs/claudio-typing.gif" width="132" alt="Claudio, la mascotte en pixel art, qui tape sur son ordinateur"></p>

<p align="center"><img src="docs/claudio-card.png" width="352" alt="La carte Claudio : session 5 h, quotas hebdomadaires, totaux du jour, courbe sur 7 jours, répartition par modèle et par projet"></p>

</div>

Un widget Windows flottant pour ton usage de Claude, le jumeau de [Claudy](https://github.com/Endikk/Claudy)
sur macOS. Sans bordure, toujours au-dessus, déplaçable, en mode compact ou complet. Les jauges
affichent les **vrais quotas** du compte — les mêmes chiffres que claude.ai ▸ Usage et `/usage` —
tandis que le détail des tokens vient des transcripts locaux de Claude Code, sous Windows et dans WSL.

- **Des chiffres réels, ou aucun.** Les pourcentages viennent de la seule API d'Anthropic. Quand
  elle ne dit rien, les jauges affichent « — » plutôt qu'une estimation. Sur un PC sans Claude Code
  où personne n'est connecté, un jeu d'exemple marqué « demo » montre à quoi ressemble Claudio.
- **Onglet Sessions.** Toutes les sessions Claude Code ouvertes sur le PC, celles qui attendent
  ta réponse d'abord, chacune avec les agents qu'elle fait tourner. « Open » ramène au premier
  plan l'app où elle tourne ; « Interrupt » lui envoie Ctrl+C.
- **Salle des agents.** Une fenêtre à part : un open space par projet, chaque agent à un bureau
  dans la zone de son travail, son écran montrant ce qu'il fait (code, tests, build, benchmark,
  infra…), et qui change de bureau quand son travail change. La ville à la fenêtre s'allume et
  s'endort avec l'heure.
- **Notifications.** Session à 80 %, limite atteinte avant la remise à zéro, session revenue, une
  session qui t'attend, un agent devenu silencieux ou terminé. Un interrupteur chacune.
- **Onglet Ports.** Liste les ports TCP laissés en écoute par Claude Code, sessions orphelines
  comprises, et les serveurs MCP qu'une session fermée a laissés tourner, et les ferme d'un clic.
  L'attribution lit les marqueurs Claude hérités dans l'environnement du process : rien d'autre
  sur le PC n'est listé.
- **Rien ne sort du PC.** Pas de télémétrie, pas de serveur tiers, aucune conversation envoyée.
  Le seul texte que Claudio lit de tes conversations est la dernière demande de chaque session
  ouverte, affichée dans l'onglet Sessions. Les seules requêtes réseau vont à l'API d'Anthropic.

## Installer

```powershell
scoop bucket add claudio https://github.com/Endikk/scoop-claudio
scoop install claudio
```

<details>
<summary>Autres voies</summary>

**Installeur :** télécharge `Claudio-win-x64-Setup.exe` (ou `win-arm64`) depuis les
[releases](https://github.com/Endikk/Claudio/releases) et lance-le. Claudio s'installe pour toi
seul, sans droits administrateur, pose son icône sur le Bureau et dans le menu Démarrer, puis se
met à jour tout seul.

Claudio **n'est pas encore signé** : SmartScreen peut prévenir au premier lancement
(*Informations complémentaires* ▸ *Exécuter quand même*). Tu préfères éviter ? Compile-le
toi-même ci-dessous ; le code est court et vérifiable.

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

Claudio n'a pas de fenêtre dans la barre des tâches. Il s'affiche en carte flottante au-dessus de
l'horloge ; dans la zone de notification, la mascotte et, à côté, le chiffre principal (« 42% »),
où un clic ouvre une carte courte ; ou en îlot suspendu en haut de l'écran, la mascotte et le
chiffre de part et d'autre, qui s'ouvre au survol comme celui de Claudy autour de l'encoche. Un
clic droit sur l'un d'eux le déplace.

| Geste | Effet |
|---|---|
| Glisser la carte | Déplacer le widget |
| Clic sur la session ou sur la bande minimale | Passer du mode complet au mode minimal |
| `Usage` / `Ports` / `Sessions` | Basculer entre les quotas, ce que Claude a laissé tourner, et les sessions ouvertes |
| Clic droit | Rafraîchir · Mode · Emplacement (carte, zone de notification, haut de l'écran) · Notifications · Afficher ou masquer · Salle des agents · Exporter l'usage en CSV · Connexion · Toujours au-dessus · Lancer au démarrage · Quitter |
| Ctrl+Alt+C, depuis n'importe quelle app | Afficher ou masquer Claudio |
| Ctrl+Alt+R, depuis n'importe quelle app | Ouvrir ou fermer la salle des agents |
| Survol de l'îlot en haut de l'écran | Ouvrir la session, son rythme et sa remise à zéro, et les autres quotas |
| Clic sur l'icône près de l'horloge | Afficher ou masquer la carte, ouvrir la carte courte, ou ouvrir l'îlot |
| Clic sur l'avatar | Carte du compte |
| Clic sur « Details » | Répartition par modèle et projets principaux |
| Ctrl+R ou F5 | Rafraîchir |
| Ctrl+Q | Quitter |

Rafraîchit toutes les 3 minutes, et aussitôt au réveil du PC.

## Documentation

La documentation détaillée est en anglais :

- [How it works](docs/how-it-works.md) — sources des données, invariants des quotas, relais de la
  status line, marqueur de rythme, sessions et agents, salle des agents, notifications, raccourcis,
  export CSV, serveurs MCP, l'îlot, mode démo, confidentialité.
- [Development](docs/development.md) — compilation, tests, structure du projet, les contraintes de
  la fenêtre à connaître avant d'y toucher.
- [Architecture](docs/architecture.md) — comment Claudio suit Claudy, fichier pour fichier.

## Branches

| Branche | Rôle |
|---|---|
| `main` | Stable. Ce qui est publié et ce que Scoop installe. |
| `develop` | La branche mobile. Chaque fonctionnalité y arrive d'abord et y reste tant qu'elle n'a pas servi pour de vrai ; `main` ne reçoit que ce qui a tenu. |

Ouvre tes pull requests sur `develop`.

## Contribuer

Un bug, une idée, un chiffre qui ne correspond pas à claude.ai ? Ouvre une
[issue](https://github.com/Endikk/Claudio/issues) — une capture et
`%LOCALAPPDATA%\Claudio\api.log` sont les bienvenues. Les PR sont ouvertes.

MIT, maintenu par [@Endikk](https://github.com/Endikk).

<p align="center"><img src="docs/claudio-overload.gif" width="196" alt="Claudio à 100 % : l'ordinateur explose et la mascotte finit en cendres, les yeux barrés"></p>
