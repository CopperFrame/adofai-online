# ADOFAI Online — mobile port and PC mod for A Dance of Fire and Ice

Unofficial, fan-made project for **A Dance of Fire and Ice** (7th Beat Games): play the game on **Android** and play
custom levels together **online** — on the phone, on PC, or both in the same room.

> Not affiliated with, or endorsed by, 7th Beat Games. This repository contains **no game code, assets or builds**.
> You need your own copy of the game. **Português:** [leia abaixo](#português).

**Steam guide (install and use):** [English](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680) · [Português](https://steamcommunity.com/sharedfiles/filedetails/?id=3837145070) · [한국어](https://steamcommunity.com/sharedfiles/filedetails/?id=3837149882)

## What's in this repository

| Folder | What it is |
|---|---|
| [`mod/`](mod) | **PC mod** (BepInEx plugin). Adds online rooms to the Steam version of the game. [Install guide](mod/README.md) · [latest release](../../releases/latest) |
| [`android/`](android) | **Android port** (touch controls, on-screen pause button, level import/download, online rooms). See [`android/README.md`](android/README.md). |
| [`shared/`](shared) | The online code used by **both** the mobile port and the PC mod (rooms, race ghosts, spectator, chat, modifiers, level browser, English/Portuguese screens). |
| [`server/`](server) | The room servers: `node` (self-hosted, e.g. a free Oracle Cloud VM) and `cloudflare` (Workers + Durable Objects). Same protocol. |

## Online features (mobile and PC)

- Rooms with up to 4 players: public (listed by region) or private (join with a code)
- Race mode: other players appear as ghosts with their name above them
- Spectator mode: join a match in progress or keep watching after you finish
- Room chat
- Host modifiers: **Immortal** (keep going after a miss) and **Speed** (0.5x – 2x)
- Custom levels from your device, or from ADOFAI.gg / TUF (downloaded automatically); levels from other sources are
  uploaded to the room automatically (max 150 MB, kept for 10 minutes)
- Screens in **English, Portuguese and Korean** (follow the game language)
- PC and phone can play in the same room

## Servers

Both the mod and the mobile port use the project's free public servers by default (a free Oracle Cloud VM in São
Paulo, with a Cloudflare Workers fallback). They are community servers with no guarantees and per-IP limits. You can
host your own for free — see [`server/node`](server/node) and [`server/cloudflare`](server/cloudflare). All players in
a room must use the same server.

## How the pieces fit together

```
            ┌────────── shared/ (online code) ──────────┐
            │                                           │
   android/ (mobile port)                      mod/ (PC, BepInEx + Harmony)
            │                                           │
            └──────────────►  server/  ◄────────────────┘
```

The mobile port compiles `shared/` directly inside the game project; the PC mod compiles the same files together
with a small plugin that hooks the original game with Harmony.

## License

MIT — see [LICENSE](LICENSE). It covers only the code in this repository; "A Dance of Fire and Ice" belongs to
7th Beat Games.

---

## Português

Projeto **não oficial**, feito por fã, para **A Dance of Fire and Ice** (7th Beat Games): jogar no **Android** e
jogar fases personalizadas **online** — no celular, no PC, ou os dois na mesma sala.

> Sem afiliação com a 7th Beat Games. Este repositório **não contém código, arquivos nem builds do jogo**. Você
> precisa da sua própria cópia do jogo.

**Guia na Steam (instalação e uso):** [Português](https://steamcommunity.com/sharedfiles/filedetails/?id=3837145070) · [English](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680) · [한국어](https://steamcommunity.com/sharedfiles/filedetails/?id=3837149882)

### O que tem no repositório

| Pasta | O que é |
|---|---|
| [`mod/`](mod) | **Mod para PC** (plugin BepInEx). Adiciona salas online à versão da Steam. [Guia de instalação](mod/README.md) · [última release](../../releases/latest) |
| [`android/`](android) | **Port para Android** (controles por toque, botão de pausa na tela, importar/baixar fases, salas online). Veja [`android/README.md`](android/README.md). |
| [`shared/`](shared) | O código online usado pelo port mobile **e** pelo mod de PC (salas, fantasmas, espectador, chat, modificadores, navegador de fases, telas em inglês e português). |
| [`server/`](server) | Os servidores de salas: `node` (próprio, por exemplo numa VM gratuita da Oracle) e `cloudflare` (Workers + Durable Objects). Mesmo protocolo. |

### Recursos online (celular e PC)

- Salas de até 4 jogadores: públicas (listadas por região) ou privadas (entra-se com um código)
- Modo corrida: os outros jogadores aparecem como fantasmas, com o nome acima
- Modo espectador: entre numa partida em andamento ou continue assistindo depois de terminar
- Chat da sala
- Modificadores do anfitrião: **Imortal** (continua a pista mesmo depois de errar) e **Velocidade** (de 0.5x a 2x)
- Fases do seu aparelho ou do ADOFAI.gg / TUF (baixadas sozinhas); fases de outras fontes são enviadas
  automaticamente à sala (máximo de 150 MB, guardadas por 10 minutos)
- Telas em **inglês, português e coreano** (seguem o idioma do jogo)
- PC e celular podem jogar na mesma sala

### Servidores

O mod e o port mobile usam, por padrão, os servidores públicos gratuitos do projeto (uma VM gratuita da Oracle Cloud
em São Paulo, com a Cloudflare Workers de reserva). São servidores comunitários, sem garantia e com limites por IP.
Você pode hospedar o seu de graça: veja [`server/node`](server/node) e [`server/cloudflare`](server/cloudflare).
Todos os jogadores de uma sala precisam usar o mesmo servidor.

### Como as partes se encaixam

O port mobile compila o `shared/` dentro do projeto do jogo; o mod de PC compila os mesmos arquivos junto com um
pequeno plugin que se liga ao jogo original pelo Harmony.

### Licença

MIT, veja o [LICENSE](LICENSE). Vale só para o código deste repositório; "A Dance of Fire and Ice" pertence à
7th Beat Games.
