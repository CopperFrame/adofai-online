# ADOFAI Online — multiplayer rooms for A Dance of Fire and Ice (PC mod)

Play custom levels together: create or join a room, pick a level, and race. Everyone's planets show up on the
track as ghosts, and anyone who joins late (or finishes early) can watch live.

> Unofficial fan-made mod. Not affiliated with 7th Beat Games. It contains no game code or assets.
> **Português:** [leia abaixo](#português). **한국어:** [아래 참고](#한국어).

**Steam guide:** [https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680)

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
   The mod's screens are available in **English, Portuguese and Korean**: by default they follow the game language
   (Portuguese or Korean if the game is in one of those, English otherwise). To force one, set `Language = en`, `pt` or `ko` in
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

> As telas do mod existem em **inglês, português e coreano**: por padrão seguem o idioma do jogo (português ou coreano se o jogo estiver
> nesses idiomas, inglês nos demais). Para forçar um, use `Language = en`, `pt` ou `ko` em `[General]` no arquivo
> `BepInEx/config/adofai.port.online.cfg`.

---

## 한국어

**A Dance of Fire and Ice** (PC, Steam)용 **비공식** 온라인 방 모드입니다. 7th Beat Games와 관련이 없으며, 게임 코드나 파일을 포함하지 않습니다.

### 기능

- 최대 4명까지 방 생성: 공개(지역별 목록) 또는 비공개(코드로 입장)
- 레이스 모드: 다른 플레이어가 이름과 함께 고스트로 표시됩니다
- 관전 모드: 진행 중인 경기에 들어가거나, 완주한 뒤에도 계속 관전할 수 있습니다
- 방 채팅
- 방장이 고르는 모디파이어: **무적**(실수해도 계속 진행)과 **속도**(0.5x ~ 2x)
- PC에 있는 커스텀 레벨 또는 ADOFAI.gg / TUF의 레벨 사용(없는 사람은 자동으로 다운로드). 그 외의 레벨은 방 서버로 자동 업로드됩니다(최대 150 MB, 10분 보관)
- 화면은 영어, 포르투갈어, **한국어**를 지원하며 게임 언어를 따릅니다(`Language = ko`로 강제할 수 있음)
- 프로젝트의 Android 포트와 같은 방에서 함께 플레이할 수 있습니다

### 설치 (Windows, Steam)

1. **BepInEx 5 (x64)** 를 게임 폴더(`A Dance of Fire and Ice.exe`가 있는 폴더)에 압축 해제합니다: <https://github.com/BepInEx/BepInEx/releases>. 게임을 한 번 실행하면 BepInEx가 폴더를 만듭니다.
2. `AdofaiOnlineMod.dll`을 `BepInEx/plugins/` 폴더에 넣습니다.
3. (선택) 다른 서버를 쓰려면 게임을 한 번 실행했다가 종료한 뒤 `BepInEx/config/adofai.port.online.cfg`를 수정합니다. 같은 방의 모든 플레이어는 같은 서버를 사용해야 합니다.
4. 게임을 실행하고 커스텀 레벨 선택 화면을 엽니다. 오른쪽 위에 **온라인**과 **커뮤니티 레벨**(ADOFAI.gg / TUF) 버튼이 나타납니다.

제거하려면 `AdofaiOnlineMod.dll`을 지우면 됩니다(또는 `BepInEx` 폴더, `winhttp.dll`, `doorstop_config.ini`).

게임 업데이트로 모드가 동작하지 않을 수 있습니다. 문제를 제보할 때는 게임 버전과 `BepInEx/LogOutput.log`를 함께 알려 주세요.
