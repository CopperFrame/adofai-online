# ADOFAI Online — Multiplayer Mod for A Dance of Fire and Ice

Unofficial, fan-made **BepInEx mod** for the Steam version of **A Dance of Fire and Ice** (7th Beat Games): create or
join a room, pick a custom level and race with friends **online**.

> Not affiliated with, or endorsed by, 7th Beat Games. This repository contains **no game code, assets or builds**.
> You need your own copy of the game. **Português:** [leia abaixo](#português).

**Steam guide (install and use):** [English](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680) · [Português](https://steamcommunity.com/sharedfiles/filedetails/?id=3837145070) · [한국어](https://steamcommunity.com/sharedfiles/filedetails/?id=3837149882)

**Download:** [latest release](../../releases/latest) · **Install guide:** [`mod/README.md`](mod/README.md)

## Features

- Rooms with up to 4 players: public (listed by region) or private (join with a code)
- Race mode: other players appear as ghosts with their name above them
- Spectator mode: join a match in progress or keep watching after you finish
- Room chat
- Host modifiers: **Immortal** (keep going after a miss) and **Speed** (0.5x – 2x)
- Custom levels from your PC, or from ADOFAI.gg / TUF (downloaded automatically); levels from other sources are
  uploaded to the room automatically (max 150 MB, kept for 10 minutes)
- Screens in **English, Portuguese and Korean** (follow the game language)

## What's in this repository

| Folder | What it is |
|---|---|
| [`mod/`](mod) | The mod: BepInEx plugin, Harmony patches, installer, language packs and build scripts. [Install guide](mod/README.md) |
| [`shared/`](shared) | The online code that the mod compiles: rooms, race ghosts, spectator, chat, modifiers, level browser and screens. |
| [`server/`](server) | The room servers: `node` (self-hosted, e.g. a free Oracle Cloud VM) and `cloudflare` (Workers + Durable Objects). Same protocol. |

## Servers

The mod uses the project's free public servers by default (a free Oracle Cloud VM in São Paulo, with a Cloudflare
Workers fallback). They are community servers with no guarantees and per-IP limits. You can host your own for free —
see [`server/node`](server/node) and [`server/cloudflare`](server/cloudflare). All players in a room must use the
same server.

## How it works

The mod is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that hooks the original game with
[Harmony](https://github.com/pardeike/Harmony) and compiles the code in `shared/` together with it. The game talks
JSON over WebSocket to a small room server.

## Build from source

Requirements: the game with BepInEx installed, .NET SDK 8+ and a bash shell (Git Bash).

```bash
bash mod/build.sh        # writes mod/dist/AdofaiOnlineMod.dll
```

Nothing from the game is redistributed: the build references the DLLs of your own installation.

## License

MIT — see [LICENSE](LICENSE). It covers only the code in this repository; "A Dance of Fire and Ice" belongs to
7th Beat Games.

---

## Português

Mod **não oficial**, feito por fã, em **BepInEx** para a versão da Steam de **A Dance of Fire and Ice**
(7th Beat Games): crie ou entre numa sala, escolha uma fase personalizada e corra com os amigos **online**.

> Sem afiliação com a 7th Beat Games. Este repositório **não contém código, arquivos nem builds do jogo**. Você
> precisa da sua própria cópia do jogo.

**Guia na Steam (instalação e uso):** [Português](https://steamcommunity.com/sharedfiles/filedetails/?id=3837145070) · [English](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680) · [한국어](https://steamcommunity.com/sharedfiles/filedetails/?id=3837149882)

**Download:** [última release](../../releases/latest) · **Guia de instalação:** [`mod/README.md`](mod/README.md)

### Recursos

- Salas de até 4 jogadores: públicas (listadas por região) ou privadas (entra-se com um código)
- Modo corrida: os outros jogadores aparecem como fantasmas, com o nome acima
- Modo espectador: entre numa partida em andamento ou continue assistindo depois de terminar
- Chat da sala
- Modificadores do anfitrião: **Imortal** (continua a pista mesmo depois de errar) e **Velocidade** (de 0.5x a 2x)
- Fases do seu PC ou do ADOFAI.gg / TUF (baixadas sozinhas); fases de outras fontes são enviadas
  automaticamente à sala (máximo de 150 MB, guardadas por 10 minutos)
- Telas em **inglês, português e coreano** (seguem o idioma do jogo)

### O que tem no repositório

| Pasta | O que é |
|---|---|
| [`mod/`](mod) | O mod: plugin BepInEx, patches do Harmony, instalador, pacotes de idioma e scripts de build. [Guia de instalação](mod/README.md) |
| [`shared/`](shared) | O código online que o mod compila: salas, fantasmas, espectador, chat, modificadores, navegador de fases e telas. |
| [`server/`](server) | Os servidores de salas: `node` (próprio, por exemplo numa VM gratuita da Oracle) e `cloudflare` (Workers + Durable Objects). Mesmo protocolo. |

### Servidores

O mod usa, por padrão, os servidores públicos gratuitos do projeto (uma VM gratuita da Oracle Cloud em São Paulo,
com a Cloudflare Workers de reserva). São servidores comunitários, sem garantia e com limites por IP. Você pode
hospedar o seu de graça: veja [`server/node`](server/node) e [`server/cloudflare`](server/cloudflare). Todos os
jogadores de uma sala precisam usar o mesmo servidor.

### Como funciona

O mod é um plugin do [BepInEx](https://github.com/BepInEx/BepInEx) que se liga ao jogo original pelo
[Harmony](https://github.com/pardeike/Harmony) e compila junto o código de `shared/`. O jogo fala JSON por
WebSocket com um pequeno servidor de salas.

### Compilar o código

Requisitos: o jogo com o BepInEx instalado, .NET SDK 8+ e um terminal bash (Git Bash). Rode
`bash mod/build.sh`; a DLL sai em `mod/dist/AdofaiOnlineMod.dll`. Nada do jogo é redistribuído: a compilação usa as
DLLs da sua própria instalação.

### Licença

MIT, veja o [LICENSE](LICENSE). Vale só para o código deste repositório; "A Dance of Fire and Ice" pertence à
7th Beat Games.
