# ADOFAI Online — multiplayer rooms for A Dance of Fire and Ice (PC mod)

Play custom levels together: create or join a room, pick a level, and race. Everyone's planets show up on the
track as ghosts, and anyone who joins late (or finishes early) can watch live.

> Unofficial fan-made mod. Not affiliated with 7th Beat Games. It contains no game code or assets.
> **Português:** [leia abaixo](#português).

## Features

- Rooms with up to 4 players: public (listed by region) or private (join with a code)
- Race mode: other players appear as ghosts with their name above them
- Spectator mode: join a match in progress or keep watching after you finish
- Room chat
- Host-selected modifiers: **Immortal** (keep going after a miss) and **Speed** (0.5x – 2x)
- The host can pick any level installed on their PC, or one from ADOFAI.gg / TUF (players who don't have it
  download it automatically)
- If the level is not on ADOFAI.gg / TUF, it is uploaded to the room server automatically so everyone can
  download it (max 150 MB, kept for 10 minutes)
- Works with the Android port of the same project: PC and phone can play in the same room

## Install (Windows, Steam)

1. Install **BepInEx 5 (x64)** — download `BepInEx_win_x64_5.4.23.x.zip` from
   <https://github.com/BepInEx/BepInEx/releases> and extract it into the game folder
   (the folder with `A Dance of Fire and Ice.exe`). Run the game once so BepInEx creates its folders.
2. Copy `AdofaiOnlineMod.dll` (from the release zip) into `BepInEx/plugins/`.
3. (Optional) To use a different server, start the game once, close it, and edit `BepInEx/config/adofai.port.online.cfg`:
   see [Servers](#servers). Everyone in a room must use the same server.
4. Start the game, open the level selector (custom levels) — two buttons appear at the top right:
   **Online** and **Fases da comunidade** ("community levels": the ADOFAI.gg / TUF browser).
   The mod's screens are available in **English and Portuguese**: by default they follow the game language
   (Portuguese if the game is in Portuguese, English otherwise). To force one, set `Language = en` or `pt` in
   `BepInEx/config/adofai.port.online.cfg` (section `[General]`).

To uninstall: delete `AdofaiOnlineMod.dll` (or the whole `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini`).

Tested with game build 26397875 (Unity 6000.3.25f1) and BepInEx 5.4.23.6. Game updates can break mods;
please open an issue with the game version and `BepInEx/LogOutput.log`.

## How it works

The mod is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that uses [Harmony](https://github.com/pardeike/Harmony)
patches to hook the level start, the fail/restart flow and a few input/audio spots. The online code itself
(`shared/`, one level up) is the same code used by the Android port, and talks JSON over WebSocket to a small room server.

## Servers

The mod works out of the box with the project's public servers (a free Oracle Cloud VM in São Paulo, with a
Cloudflare Workers fallback). They are free community servers with no guarantees and per-IP limits (rooms per hour,
uploads per hour, simultaneous connections); levels sent to a room are kept for only 10 minutes.

To use another server, edit `BepInEx/config/adofai.port.online.cfg` (created on the first run):

```ini
[Server]
Primary = https://your-server.example
Fallback =
```

`Primary` is tried first and `Fallback` (optional) if it does not answer. **All players in a room must use the same
server.**

You can host your own for free:

- `server/node` (repository root) — Node.js server (a free Oracle Cloud VM is enough). `setup.sh` installs everything with HTTPS.
- `server/cloudflare` (repository root) — the same protocol on Cloudflare Workers + Durable Objects.

## Build from source

Requirements: the game with BepInEx installed, .NET SDK 8+, and a bash shell (Git Bash).

```bash
bash mod/build.sh        # writes mod/dist/AdofaiOnlineMod.dll
```

Nothing from the game is redistributed: the build references the DLLs of your own installation.

## License

MIT — see [LICENSE](../LICENSE).

---

## Português

Mod **não oficial** de salas online para A Dance of Fire and Ice (PC). Não é afiliado à 7th Beat Games e não
contém código nem arquivos do jogo.

### Recursos

- Salas de até 4 jogadores: públicas (listadas por região) ou privadas (entra-se com um código)
- Modo corrida: os outros jogadores aparecem como fantasmas, com o nome acima
- Modo espectador: entre numa partida em andamento ou continue assistindo depois de terminar
- Chat da sala
- Modificadores escolhidos pelo anfitrião: **Imortal** (continua a pista mesmo depois de errar) e **Velocidade**
  (de 0.5x a 2x)
- O anfitrião pode escolher qualquer fase instalada no PC dele, ou uma do ADOFAI.gg / TUF (quem não tiver a fase
  baixa sozinho)
- Se a fase não estiver no ADOFAI.gg / TUF, ela é enviada automaticamente ao servidor da sala para todos
  baixarem (máximo de 150 MB, guardada por 10 minutos)
- Funciona junto com o port Android do mesmo projeto: PC e celular podem jogar na mesma sala

### Instalação (Windows, Steam)

1. Instale o **BepInEx 5 (x64)**: baixe o `BepInEx_win_x64_5.4.23.x.zip` em
   <https://github.com/BepInEx/BepInEx/releases> e extraia na pasta do jogo (a que tem o
   `A Dance of Fire and Ice.exe`). Abra o jogo uma vez para o BepInEx criar as pastas.
2. Copie o `AdofaiOnlineMod.dll` (do zip da release) para `BepInEx/plugins/`.
3. (Opcional) Para usar outro servidor, abra o jogo uma vez, feche e edite
   `BepInEx/config/adofai.port.online.cfg` (veja [Servidores](#servidores)). Todos da sala precisam usar o
   mesmo servidor.
4. Abra o jogo e entre no seletor de fases personalizadas: aparecem dois botões no canto superior direito,
   **Online** e **Fases da comunidade** (o navegador de fases do ADOFAI.gg / TUF).

Para desinstalar, apague o `AdofaiOnlineMod.dll` (ou a pasta `BepInEx`, o `winhttp.dll` e o
`doorstop_config.ini`).

Testado na versão 26397875 do jogo (Unity 6000.3.25f1) com BepInEx 5.4.23.6. Atualizações do jogo podem quebrar
mods; ao relatar um problema, informe a versão do jogo e anexe o `BepInEx/LogOutput.log`.

### Servidores

O mod funciona sem configurar nada com os servidores públicos do projeto (uma VM gratuita da Oracle Cloud em São
Paulo, com a Cloudflare Workers de reserva). São servidores comunitários gratuitos, sem garantia, com limites por
IP (salas por hora, envios por hora e conexões simultâneas); as fases enviadas para uma sala ficam guardadas só
por 10 minutos.

Para usar outro servidor, edite `BepInEx/config/adofai.port.online.cfg` (criado na primeira execução):

```ini
[Server]
Primary = https://seu-servidor.exemplo
Fallback =
```

O `Primary` é tentado primeiro e o `Fallback` (opcional) só se ele não responder. **Todos os jogadores de uma sala
precisam usar o mesmo servidor.**

Você pode hospedar o seu de graça: `server/node` (na raiz do repositório) (servidor Node.js; uma VM gratuita da Oracle basta, e o
`setup.sh` instala tudo com HTTPS) ou `server/cloudflare` (na raiz do repositório) (o mesmo protocolo em Cloudflare Workers).

### Compilar o código

Requisitos: o jogo com o BepInEx instalado, .NET SDK 8+ e um terminal bash (Git Bash). Rode `bash mod/build.sh`; a
DLL sai em `mod/dist/AdofaiOnlineMod.dll`. Nada do jogo é redistribuído: a compilação usa as DLLs da sua própria
instalação.

### Licença

MIT, veja o [LICENSE](../LICENSE).

> As telas do mod existem em **inglês e português**: por padrão seguem o idioma do jogo (português se o jogo estiver
> em português, inglês nos demais). Para forçar um, use `Language = en` ou `pt` em `[General]` no arquivo
> `BepInEx/config/adofai.port.online.cfg`.
