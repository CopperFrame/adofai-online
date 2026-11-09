# ADOFAI Online — multiplayer rooms for A Dance of Fire and Ice (PC mod)

Play custom levels together: create or join a room, pick a level, and race. Everyone's planets show up on the
track as ghosts, and anyone who joins late (or finishes early) can watch live.

> Unofficial fan-made mod. Not affiliated with 7th Beat Games. It contains no game code or assets.
> **Português:** [leia abaixo](#português).

## Features

- Rooms with up to 4 players, public list or private code, region-aware server list
- Race mode: other players appear as ghosts with their name above them
- Spectator mode: join a match in progress or keep watching after you finish
- Room chat
- Host-selected modifiers: **Immortal** (keep going after a miss) and **Speed** (0.5x – 2x)
- The host picks any level you both have, or one from ADOFAI.gg / TUF (it downloads automatically)
- If the level is not on ADOFAI.gg/TUF, it is sent to the room automatically (max 150 MB, kept for 10 minutes)
- Works with the Android port of the same project: PC and phone can play in the same room

## Install (Windows, Steam)

1. Install **BepInEx 5 (x64)** — download `BepInEx_win_x64_5.4.23.x.zip` from
   <https://github.com/BepInEx/BepInEx/releases> and extract it into the game folder
   (the folder with `A Dance of Fire and Ice.exe`). Run the game once so BepInEx creates its folders.
2. Copy `AdofaiOnlineMod.dll` (from the release zip) into `BepInEx/plugins/`.
3. (Optional) To use a different server, start the game once, close it, and edit `BepInEx/config/adofai.port.online.cfg`:
   see [Servers](#servers). Everyone in a room must use the same server.
4. Start the game, open the level selector (custom levels) — two buttons appear at the top right:
   **Online** and **Fases da comunidade** (community levels).

To uninstall: delete `AdofaiOnlineMod.dll` (or the whole `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini`).

Tested with game build 26397875 (Unity 6000.3.25f1) and BepInEx 5.4.23.6. Game updates can break mods;
please open an issue with the game version and `BepInEx/LogOutput.log`.

## How it works

The mod is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that uses [Harmony](https://github.com/pardeike/Harmony)
patches to hook the level start, the fail/restart flow and a few input/audio spots. The online code itself
(`src/shared`) is the same code used by the Android port, and talks JSON over WebSocket to a small room server.

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

- `server/node` — Node.js server (a free Oracle Cloud VM is enough). `setup.sh` installs everything with HTTPS.
- `server/cloudflare` — the same protocol on Cloudflare Workers + Durable Objects.

## Build from source

Requirements: the game with BepInEx installed, .NET SDK 8+, and a bash shell (Git Bash).

```bash
bash build.sh        # writes dist/AdofaiOnlineMod.dll
```

Nothing from the game is redistributed: the build references the DLLs of your own installation.

## License

MIT — see [LICENSE](LICENSE).

---

## Português

Mod **não oficial** de salas online para A Dance of Fire and Ice (PC).

**Recursos:** salas de até 4 jogadores (públicas ou com código), corrida com fantasmas e nomes, modo espectador
(entrar no meio da partida ou assistir depois de terminar), chat, modificadores do anfitrião (**Imortal** e
**Velocidade** de 0.5x a 2x), download automático de fases do ADOFAI.gg/TUF e envio automático de fases que não
estão nessas fontes. Funciona junto com o port Android: PC e celular na mesma sala.

**Instalação**

1. Instale o **BepInEx 5 (x64)** na pasta do jogo (a que tem o `A Dance of Fire and Ice.exe`):
   <https://github.com/BepInEx/BepInEx/releases>. Abra o jogo uma vez para ele criar as pastas.
2. Copie `AdofaiOnlineMod.dll` para `BepInEx/plugins/`.
3. (Opcional) Para usar outro servidor, abra o jogo uma vez, feche e edite `BepInEx/config/adofai.port.online.cfg`:
   `Primary =` (todos da sala precisam usar o mesmo servidor). O padrão já usa os servidores públicos do projeto.
4. Abra o seletor de fases personalizadas: aparecem os botões **Online** e **Fases da comunidade** no canto
   superior direito.

Para desinstalar, apague a DLL (ou a pasta `BepInEx`, o `winhttp.dll` e o `doorstop_config.ini`).

Testado na versão 26397875 do jogo (Unity 6000.3.25f1) com BepInEx 5.4.23.6. Atualizações do jogo podem
quebrar mods; ao relatar um problema, informe a versão do jogo e anexe o `BepInEx/LogOutput.log`.
