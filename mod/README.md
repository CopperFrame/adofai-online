# ADOFAI Online — multiplayer rooms for A Dance of Fire and Ice (PC mod)

Play custom levels together: create or join a room, pick a level, and race. Everyone's planets show up on the
track as ghosts, and anyone who joins late (or finishes early) can watch live.

> Unofficial fan-made mod. Not affiliated with 7th Beat Games. It contains no game code or assets.
> **Português:** [leia abaixo](#português). **한국어:** [아래 참고](#한국어).

**Steam guide:** [English](https://steamcommunity.com/sharedfiles/filedetails/?id=3837129680) · [Português](https://steamcommunity.com/sharedfiles/filedetails/?id=3837145070) · [한국어](https://steamcommunity.com/sharedfiles/filedetails/?id=3837149882)

## Features

- Rooms with up to 4 players: public (listed by region) or private (join with a code)
- Race mode: other players appear as ghosts with their name above them
- Spectator mode: join a match in progress or keep watching after you finish
- Room chat
- Host-selected modifiers: **Immortal** (keep going after a miss) and **Speed** (0.5x – 2x)
- The host can pick any level installed on their PC, or one from ADOFAI.gg / TUF (players who don't have it
  download it automatically). The picker lists your local **and Steam Workshop** levels with their artwork, with search and All / Local / Workshop filters
- **Level queue:** the host can line up to 8 levels; when a match ends, the next one is picked automatically
- If the level is not on ADOFAI.gg / TUF, it is uploaded to the room server automatically so everyone can
  download it (max 150 MB, kept for 10 minutes)

## Install (Windows, Steam)

### Easy install (recommended)

1. From the [latest release](https://github.com/CopperFrame/adofai-online/releases/latest) download **both**
   `Install-ADOFAI-Online.bat` and `Install-ADOFAI-Online.ps1` into the same folder.
2. Close the game and double-click `Install-ADOFAI-Online.bat`.
   It finds your game, installs BepInEx if needed (a pinned, checksum-verified version), downloads the latest
   mod, **checks its SHA-256** and puts it in `BepInEx/plugins`. Run it again any time to update.
   To remove the mod: `Install-ADOFAI-Online.bat --uninstall`.
3. Start the game, open the custom level selector and click **Online**.

Windows may show a SmartScreen warning for the downloaded files ("More info" → "Run anyway"). The scripts are
plain text, you can read them first: [`mod/installer`](installer).

### Manual install

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

   You can also change the language and servers without editing any file: **Online → Options** (keyboard/controller: Tab, arrows, Enter; Esc to close).
To uninstall: delete `AdofaiOnlineMod.dll` (or the whole `BepInEx` folder, `winhttp.dll`, `doorstop_config.ini`).

Tested with game build 26397875 (Unity 6000.3.25f1) and BepInEx 5.4.23.6. Game updates can break mods;
please open an issue with the game version and `BepInEx/LogOutput.log`.

### The Online buttons do not show up

1. Make sure the game is **closed**, then run `Install-ADOFAI-Online.bat` again (it also updates an old BepInEx and unblocks the files).
2. Start the game, open the **custom level selector** (not the main menu), close the game and double-click
   **`Diagnose-ADOFAI-Online.bat`**. It checks the installation and writes `ADOFAI-Online-diagnostic.txt` on your Desktop
   (no personal names inside). Send that file in an issue or to whoever is helping you.
3. Typical causes: the antivirus removed `winhttp.dll` (add the game folder as an exclusion and install again);
   BepInEx older than 5.4.23 (the installer updates it); the installer found another copy of the game (another Steam
   library); the file is blocked by Windows (right-click the DLL → Properties → Unblock); a game update broke a patch
   (the level selector then shows a notice); Linux/Steam Deck needs the launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
   If `BepInEx/LogOutput.log` does not exist at all, BepInEx never started.

## How it works

The mod is a [BepInEx](https://github.com/BepInEx/BepInEx) plugin that uses [Harmony](https://github.com/pardeike/Harmony)
patches to hook the level start, the fail/restart flow and a few input/audio spots. The online code itself
(`shared/`, one level up) is compiled together with the plugin and talks JSON over WebSocket to a small room server.

## Updates and privacy

When the game starts, the mod asks GitHub (`api.github.com`, once) which release is the latest and, if yours is
older, shows a notice in the custom level selector. Nothing about you is sent (only a standard web request with the
mod version in the User-Agent). To turn it off, set `CheckForUpdates = false` in
`BepInEx/config/adofai.port.online.cfg`.

If a game update breaks part of the mod, the same notice tells you which part and where to find a new version.

## Add your own language

The mod ships with English, Portuguese and Korean, and you can add more **without touching the code**. Start the game once:
the mod creates `BepInEx/config/AdofaiOnline-lang/template.txt` (every text, original Portuguese = English).
Copy it to `<code>.txt` (for example `es.txt`), add a line `# name: Español`, translate the text after ` = `
(untranslated lines stay in English), then pick the language in **Online → Options** or set `Language = es` in
`adofai.port.online.cfg`. Translations are welcome as pull requests (put the file in `mod/lang/`).
Note: the font only covers Latin scripts and Korean for now.

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
  baixa sozinho). O seletor lista suas fases locais **e da oficina do Steam** com a arte de cada uma, com busca e filtros Todas / Locais / Oficina
- **Fila de fases:** o anfitrião pode enfileirar até 8 fases; quando a partida acaba, a próxima entra sozinha
- Se a fase não estiver no ADOFAI.gg / TUF, ela é enviada automaticamente ao servidor da sala para todos
  baixarem (máximo de 150 MB, guardada por 10 minutos)

### Instalação (Windows, Steam)

#### Instalação fácil (recomendada)

1. Na [última release](https://github.com/CopperFrame/adofai-online/releases/latest), baixe **os dois**:
   `Install-ADOFAI-Online.bat` e `Install-ADOFAI-Online.ps1`, na mesma pasta.
2. Feche o jogo e dê dois cliques no `Install-ADOFAI-Online.bat`.
   Ele acha o jogo, instala o BepInEx se precisar (versão fixa, com verificação), baixa o mod mais novo,
   **confere o SHA-256** e coloca em `BepInEx/plugins`. Rode de novo quando quiser atualizar.
   Para remover o mod: `Install-ADOFAI-Online.bat --uninstall`.
3. Abra o jogo, entre no seletor de fases personalizadas e clique em **Online**.

O Windows pode mostrar um aviso do SmartScreen nos arquivos baixados ("Mais informações" → "Executar assim mesmo").
Os scripts são texto puro e podem ser lidos antes: [`mod/installer`](installer).

#### Instalação manual

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

#### Os botões do Online não aparecem

1. Com o jogo **fechado**, rode o `Install-ADOFAI-Online.bat` de novo (ele também atualiza um BepInEx antigo e desbloqueia os arquivos).
2. Abra o jogo, entre no **seletor de fases personalizadas** (não no menu principal), feche o jogo e dê dois cliques em
   **`Diagnose-ADOFAI-Online.bat`**. Ele confere a instalação e grava `ADOFAI-Online-diagnostic.txt` na Área de Trabalho
   (sem nomes pessoais). Envie esse arquivo numa issue ou para quem está te ajudando.
3. Causas comuns: o antivírus removeu o `winhttp.dll` (adicione a pasta do jogo como exceção e instale de novo); BepInEx
   mais antigo que a 5.4.23 (o instalador atualiza); o instalador achou outra cópia do jogo (outra biblioteca do Steam);
   arquivo bloqueado pelo Windows (botão direito na DLL → Propriedades → Desbloquear); atualização do jogo quebrou um patch
   (o seletor mostra um aviso); no Linux/Steam Deck use a opção de inicialização `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
   Se o `BepInEx/LogOutput.log` nem existir, o BepInEx nunca iniciou.

### Adicione seu idioma

O mod traz inglês, português e coreano, e dá para acrescentar outros **sem mexer no código**. Abra o jogo uma vez: o mod cria
`BepInEx/config/AdofaiOnline-lang/template.txt` (todos os textos, original em português = inglês). Copie para `<código>.txt`
(por exemplo `es.txt`), acrescente a linha `# name: Español`, traduza o que vem depois de ` = ` (o que ficar sem tradução
aparece em inglês) e escolha o idioma em **Online → Opções** ou com `Language = es` no `adofai.port.online.cfg`.
Traduções são bem-vindas por pull request (arquivo em `mod/lang/`).

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

### 설치 (Windows, Steam)

#### 간편 설치 (권장)

1. [최신 릴리스](https://github.com/CopperFrame/adofai-online/releases/latest)에서 `Install-ADOFAI-Online.bat`과
   `Install-ADOFAI-Online.ps1` **두 파일 모두**를 같은 폴더에 받습니다.
2. 게임을 종료하고 `Install-ADOFAI-Online.bat`을 더블클릭합니다. 게임 폴더를 찾아 BepInEx를 설치하고(필요한 경우),
   최신 모드를 받아 **SHA-256을 확인**한 뒤 `BepInEx/plugins`에 넣습니다. 업데이트할 때도 다시 실행하면 됩니다.
   제거하려면 `Install-ADOFAI-Online.bat --uninstall`.
3. 게임을 실행하고 커스텀 레벨 선택 화면에서 **온라인**을 누릅니다.

Windows SmartScreen 경고가 나올 수 있습니다("추가 정보" → "실행"). 스크립트는 텍스트 파일이므로 먼저 읽어 볼 수 있습니다:
[`mod/installer`](installer).

#### 수동 설치

1. **BepInEx 5 (x64)** 를 게임 폴더(`A Dance of Fire and Ice.exe`가 있는 폴더)에 압축 해제합니다: <https://github.com/BepInEx/BepInEx/releases>. 게임을 한 번 실행하면 BepInEx가 폴더를 만듭니다.
2. `AdofaiOnlineMod.dll`을 `BepInEx/plugins/` 폴더에 넣습니다.
3. (선택) 다른 서버를 쓰려면 게임을 한 번 실행했다가 종료한 뒤 `BepInEx/config/adofai.port.online.cfg`를 수정합니다. 같은 방의 모든 플레이어는 같은 서버를 사용해야 합니다.
4. 게임을 실행하고 커스텀 레벨 선택 화면을 엽니다. 오른쪽 위에 **온라인**과 **커뮤니티 레벨**(ADOFAI.gg / TUF) 버튼이 나타납니다.

제거하려면 `AdofaiOnlineMod.dll`을 지우면 됩니다(또는 `BepInEx` 폴더, `winhttp.dll`, `doorstop_config.ini`).

게임 업데이트로 모드가 동작하지 않을 수 있습니다. 문제를 제보할 때는 게임 버전과 `BepInEx/LogOutput.log`를 함께 알려 주세요.

#### 온라인 버튼이 보이지 않을 때

1. 게임을 **종료**한 뒤 `Install-ADOFAI-Online.bat`를 다시 실행하세요(오래된 BepInEx도 업데이트하고 파일 차단도 해제합니다).
2. 게임을 실행해 **커스텀 레벨 선택 화면**(메인 메뉴 아님)을 연 뒤 게임을 종료하고 **`Diagnose-ADOFAI-Online.bat`**를 더블클릭하세요.
   설치 상태를 확인하고 바탕화면에 `ADOFAI-Online-diagnostic.txt`를 만듭니다(개인 이름 없음). 이 파일을 이슈에 올리거나 도와주는 사람에게 보내세요.
3. 흔한 원인: 백신이 `winhttp.dll`을 삭제함(게임 폴더를 예외로 추가 후 재설치), BepInEx 5.4.23 미만(설치 프로그램이 업데이트), 다른 게임 폴더에 설치됨,
   Windows의 파일 차단(DLL 우클릭 → 속성 → 차단 해제), 게임 업데이트로 패치 실패(레벨 선택 화면에 안내가 표시됨),
   Linux/Steam Deck은 실행 옵션 `WINEDLLOVERRIDES="winhttp=n,b" %command%` 필요.
