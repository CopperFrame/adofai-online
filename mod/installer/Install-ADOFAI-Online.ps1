<#
.SYNOPSIS
  One-click installer for the ADOFAI Online mod (PC, Steam). / Instalador de um clique do mod ADOFAI Online. / 한 번에 설치.

.DESCRIPTION
  1. Finds your copy of "A Dance of Fire and Ice" (Steam).
  2. Installs BepInEx 5 (x64) into the game folder if it is not there yet (or updates it when it is too old for this game).
  3. Downloads the latest AdofaiOnlineMod.dll from GitHub, checks its SHA-256 and puts it in BepInEx\plugins.
  4. Checks the installation (files, versions, Windows blocking, antivirus detections).
  Running it again updates the mod. Nothing outside the game folder is changed.
  With -Diagnose it changes nothing: it checks the installation and BepInEx\LogOutput.log and writes a report file
  (ADOFAI-Online-diagnostic.txt on the Desktop) that you can send to whoever is helping you.

.PARAMETER GamePath   Game folder (the one with "A Dance of Fire and Ice.exe"). Detected automatically when omitted.
.PARAMETER Uninstall  Removes the mod (and, with -RemoveBepInEx, BepInEx too).
.PARAMETER Diagnose   Only checks the installation and writes a report. Run the game once before (and open the level selector).
.PARAMETER DllSource  Use this DLL (local path or URL) instead of the latest release. Mostly for testing.
#>
param(
    [string]$GamePath,
    [switch]$Uninstall,
    [switch]$RemoveBepInEx,
    [switch]$Diagnose,
    [string]$DllSource,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }
$ProgressPreference = 'SilentlyContinue'

$Repo        = 'CopperFrame/adofai-online'
$ReleaseBase = if ($env:ADOFAI_ONLINE_RELEASE_BASE) { $env:ADOFAI_ONLINE_RELEASE_BASE } else { "https://github.com/$Repo/releases/latest/download" }  # the variable is for testing
$BepInExUrl  = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip'
$BepInExSha  = '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'
$BepInExMin  = [version]'5.4.23.0'   # older BepInEx 5 does not start on Unity 6
$GameExe     = 'A Dance of Fire and Ice.exe'

# ---- messages (English / Portugues / Korean) ----
$lang = 'en'
$ui = (Get-Culture).TwoLetterISOLanguageName
if ($ui -eq 'pt') { $lang = 'pt' } elseif ($ui -eq 'ko') { $lang = 'ko' }
$M = @{
    en = @{
        title = 'ADOFAI Online - installer'; running = 'Close A Dance of Fire and Ice first, then run this again.'
        finding = 'Looking for the game...'; found = 'Game found: '; notfound = 'Could not find the game automatically.'
        ask = 'Paste the game folder (the one with A Dance of Fire and Ice.exe): '; bad = 'That folder does not have the game.'
        bep_have = 'BepInEx is already installed.'; bep_get = 'Installing BepInEx 5 (downloading ~0.6 MB)...'
        bep_old = 'The installed BepInEx is too old for this game. Updating it...'
        bep_bad = 'The BepInEx download did not match the expected checksum. Nothing was changed.'
        dll_get = 'Downloading the mod...'; dll_bad = 'The mod download did not match its checksum. Nothing was installed.'
        done = 'Installed! Start the game, open the custom level selector and click Online.'
        first = 'Tip: start the game once so BepInEx creates its folders (the mod works right away).'
        help = 'If the Online buttons do not show up: start the game, open the custom level selector, close the game and run Diagnose-ADOFAI-Online.bat. It writes a report file to send to whoever is helping you.'
        removed = 'Mod removed.'; removed_bep = 'BepInEx removed.'
        fail = 'Something went wrong: '; press = 'Press Enter to close...'
        checking = 'Checking the installation...'; report = 'Report saved to: '; send = 'Send that file to whoever is helping you.'
        verdict = 'Result: '
    }
    pt = @{
        title = 'ADOFAI Online - instalador'; running = 'Feche o A Dance of Fire and Ice e rode de novo.'
        finding = 'Procurando o jogo...'; found = 'Jogo encontrado: '; notfound = 'Nao consegui achar o jogo sozinho.'
        ask = 'Cole a pasta do jogo (a que tem o A Dance of Fire and Ice.exe): '; bad = 'Essa pasta nao tem o jogo.'
        bep_have = 'O BepInEx ja esta instalado.'; bep_get = 'Instalando o BepInEx 5 (baixando ~0,6 MB)...'
        bep_old = 'O BepInEx instalado e antigo demais para este jogo. Atualizando...'
        bep_bad = 'O download do BepInEx nao bateu com a soma de verificacao esperada. Nada foi alterado.'
        dll_get = 'Baixando o mod...'; dll_bad = 'O download do mod nao bateu com a soma de verificacao. Nada foi instalado.'
        done = 'Instalado! Abra o jogo, entre no seletor de fases personalizadas e clique em Online.'
        first = 'Dica: abra o jogo uma vez para o BepInEx criar as pastas (o mod ja funciona).'
        help = 'Se os botoes do Online nao aparecerem: abra o jogo, entre no seletor de fases personalizadas, feche o jogo e rode o Diagnose-ADOFAI-Online.bat. Ele gera um arquivo de relatorio para enviar a quem esta te ajudando.'
        removed = 'Mod removido.'; removed_bep = 'BepInEx removido.'
        fail = 'Algo deu errado: '; press = 'Aperte Enter para fechar...'
        checking = 'Conferindo a instalacao...'; report = 'Relatorio salvo em: '; send = 'Envie esse arquivo para quem esta te ajudando.'
        verdict = 'Resultado: '
    }
    ko = @{
        title = 'ADOFAI Online - 설치 프로그램'; running = 'A Dance of Fire and Ice를 종료한 뒤 다시 실행하세요.'
        finding = '게임을 찾는 중...'; found = '게임 폴더: '; notfound = '게임을 자동으로 찾지 못했습니다.'
        ask = '게임 폴더(A Dance of Fire and Ice.exe가 있는 폴더)를 붙여 넣으세요: '; bad = '이 폴더에는 게임이 없습니다.'
        bep_have = 'BepInEx가 이미 설치되어 있습니다.'; bep_get = 'BepInEx 5 설치 중(약 0.6 MB 다운로드)...'
        bep_old = '설치된 BepInEx가 이 게임에는 너무 오래된 버전입니다. 업데이트합니다...'
        bep_bad = 'BepInEx 다운로드의 체크섬이 일치하지 않습니다. 아무것도 변경하지 않았습니다.'
        dll_get = '모드 다운로드 중...'; dll_bad = '모드 다운로드의 체크섬이 일치하지 않습니다. 아무것도 설치하지 않았습니다.'
        done = '설치 완료! 게임을 실행하고 커스텀 레벨 선택 화면에서 온라인을 누르세요.'
        first = '팁: 게임을 한 번 실행하면 BepInEx가 폴더를 만듭니다(모드는 바로 동작합니다).'
        help = '온라인 버튼이 보이지 않으면: 게임을 실행해 커스텀 레벨 선택 화면을 연 뒤 게임을 종료하고 Diagnose-ADOFAI-Online.bat를 실행하세요. 도움을 주는 사람에게 보낼 보고서 파일이 만들어집니다.'
        removed = '모드를 제거했습니다.'; removed_bep = 'BepInEx를 제거했습니다.'
        fail = '문제가 발생했습니다: '; press = 'Enter를 눌러 닫기...'
        checking = '설치 상태를 확인하는 중...'; report = '보고서 저장 위치: '; send = '이 파일을 도움을 주는 사람에게 보내세요.'
        verdict = '결과: '
    }
}
$T = $M[$lang]

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

# text in the user's language, from three variants
function L($en, $pt, $ko) { if ($lang -eq 'pt') { return $pt } elseif ($lang -eq 'ko') { return $ko } else { return $en } }

function Test-GameFolder($path) {
    return ($path -and (Test-Path -LiteralPath (Join-Path $path $GameExe)))
}

function Find-Game {
    $roots = New-Object System.Collections.Generic.List[string]
    $steam = $null
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($p.SteamPath) { $steam = $p.SteamPath }
            elseif ($p.InstallPath) { $steam = $p.InstallPath }
            if ($steam) { break }
        } catch { }
    }
    if ($steam) {
        $roots.Add($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($line in Get-Content -LiteralPath $vdf) {
                if ($line -match '"path"\s+"([^"]+)"') { $roots.Add(($Matches[1] -replace '\\\\', '\')) }
            }
        }
    }
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem)) {
        $roots.Add((Join-Path $drive.Root 'SteamLibrary'))
        $roots.Add((Join-Path $drive.Root 'Program Files (x86)\Steam'))
    }
    foreach ($root in ($roots | Select-Object -Unique)) {
        $candidate = Join-Path $root 'steamapps\common\A Dance of Fire and Ice'
        if (Test-GameFolder $candidate) { return $candidate }
    }
    return $null
}

function Get-Sha256($path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-File($source, $destination) {
    if ($source -match '^https?://') {
        Invoke-WebRequest -Uri $source -OutFile $destination -UseBasicParsing
    } else {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

# ---- installation checks ----

$script:checks = New-Object System.Collections.Generic.List[object]   # @{ level = OK|WARN|FAIL; en = '...' }

function Add-Check($level, $en, $pt, $ko) {
    $script:checks.Add(@{ level = $level; en = $en })
    $color = 'Green'; $tag = ' OK '
    if ($level -eq 'WARN') { $color = 'Yellow'; $tag = 'WARN' } elseif ($level -eq 'FAIL') { $color = 'Red'; $tag = 'FAIL' }
    Say ("  [$tag] " + (L $en $pt $ko)) $color
}

function Get-FileVersionSafe($path) {
    try { return [version](Get-Item -LiteralPath $path).VersionInfo.FileVersion } catch { return $null }
}

# is the game executable 64-bit? (BepInEx x64 only works with a 64-bit game)
function Get-ExeMachine($exe) {
    try {
        $fs = [IO.File]::OpenRead($exe)
        try {
            $br = New-Object IO.BinaryReader($fs)
            $fs.Position = 0x3C
            $pe = $br.ReadInt32()
            $fs.Position = $pe + 4
            return $br.ReadUInt16()
        } finally { $fs.Close() }
    } catch { return 0 }
}

# lines of a file that another program may have open (BepInEx keeps LogOutput.log open while the game runs)
function Read-SharedLines($path) {
    $fs = New-Object IO.FileStream($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $sr = New-Object IO.StreamReader($fs)
        $text = $sr.ReadToEnd()
    } finally { $fs.Close() }
    return ($text -split "`r?`n")
}

function Test-Installation($gameDir, $withLog) {
    $exe = Join-Path $gameDir $GameExe

    # game
    $machine = Get-ExeMachine $exe
    if ($machine -eq 0x8664) { Add-Check 'OK' 'The game is the 64-bit version.' 'O jogo e a versao de 64 bits.' '게임이 64비트 버전입니다.' }
    elseif ($machine -eq 0x14c) { Add-Check 'FAIL' 'The game is 32-bit: this BepInEx (x64) cannot load.' 'O jogo e de 32 bits: este BepInEx (x64) nao consegue carregar.' '게임이 32비트라서 이 BepInEx(x64)는 로드되지 않습니다.' }
    $unity = ''
    try { $unity = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion } catch { }
    $manifest = Join-Path $gameDir '..\..\appmanifest_977950.acf'
    $build = ''
    if (Test-Path -LiteralPath $manifest) {
        foreach ($line in Get-Content -LiteralPath $manifest) { if ($line -match '"buildid"\s+"(\d+)"') { $build = $Matches[1] } }
    }
    if ($unity -or $build) {
        $tested = ($build -eq '26397875')
        if ($tested) { Add-Check 'OK' "Game version: Unity $unity, Steam build $build (the version this mod was tested on)." "Versao do jogo: Unity $unity, build do Steam $build (a versao em que o mod foi testado)." "게임 버전: Unity $unity, Steam 빌드 $build (모드를 테스트한 버전)." }
        else { Add-Check 'WARN' "Game version: Unity $unity, Steam build $build. The mod was tested on build 26397875; another build may break parts of the mod." "Versao do jogo: Unity $unity, build do Steam $build. O mod foi testado na build 26397875; outra build pode quebrar partes do mod." "게임 버전: Unity $unity, Steam 빌드 $build. 이 모드는 빌드 26397875에서 테스트되었으며, 다른 빌드에서는 일부가 동작하지 않을 수 있습니다." }
    }

    # BepInEx
    $winhttp = Join-Path $gameDir 'winhttp.dll'
    if (Test-Path -LiteralPath $winhttp) { Add-Check 'OK' 'winhttp.dll (BepInEx loader) is in the game folder.' 'winhttp.dll (carregador do BepInEx) esta na pasta do jogo.' 'winhttp.dll(BepInEx 로더)이 게임 폴더에 있습니다.' }
    else { Add-Check 'FAIL' 'winhttp.dll is missing: BepInEx cannot start. An antivirus may have removed it; add the game folder as an exclusion and install again.' 'Falta o winhttp.dll: o BepInEx nao inicia. Um antivirus pode ter removido; adicione a pasta do jogo como excecao e instale de novo.' 'winhttp.dll이 없어 BepInEx가 시작되지 않습니다. 백신이 삭제했을 수 있으니 게임 폴더를 예외로 추가하고 다시 설치하세요.' }
    $doorstop = Join-Path $gameDir 'doorstop_config.ini'
    if (Test-Path -LiteralPath $doorstop) {
        $enabled = $false
        foreach ($line in Get-Content -LiteralPath $doorstop) { if ($line -match '^\s*enabled\s*=\s*true\s*$') { $enabled = $true } }
        if ($enabled) { Add-Check 'OK' 'doorstop_config.ini is enabled.' 'doorstop_config.ini esta ativado.' 'doorstop_config.ini가 활성화되어 있습니다.' }
        else { Add-Check 'FAIL' 'doorstop_config.ini has enabled = false: BepInEx is turned off.' 'doorstop_config.ini esta com enabled = false: o BepInEx esta desligado.' 'doorstop_config.ini의 enabled가 false라서 BepInEx가 꺼져 있습니다.' }
    } else {
        Add-Check 'FAIL' 'doorstop_config.ini is missing.' 'Falta o doorstop_config.ini.' 'doorstop_config.ini가 없습니다.'
    }
    $bepDll = Join-Path $gameDir 'BepInEx\core\BepInEx.dll'
    $bepVersion = Get-FileVersionSafe $bepDll
    if (-not $bepVersion) { Add-Check 'FAIL' 'BepInEx\core\BepInEx.dll is missing.' 'Falta o BepInEx\core\BepInEx.dll.' 'BepInEx\core\BepInEx.dll이 없습니다.' }
    elseif ($bepVersion -lt $BepInExMin) { Add-Check 'FAIL' "BepInEx $bepVersion is too old for this game (needs 5.4.23 or newer). Run the installer again to update it." "O BepInEx $bepVersion e antigo demais para este jogo (precisa da 5.4.23 ou mais nova). Rode o instalador de novo para atualizar." "BepInEx $bepVersion은(는) 이 게임에는 너무 오래되었습니다(5.4.23 이상 필요). 설치 프로그램을 다시 실행해 업데이트하세요." }
    else { Add-Check 'OK' "BepInEx $bepVersion is installed." "BepInEx $bepVersion esta instalado." "BepInEx $bepVersion 설치됨." }

    # the mod
    $dll = Join-Path $gameDir 'BepInEx\plugins\AdofaiOnlineMod.dll'
    if (-not (Test-Path -LiteralPath $dll)) {
        Add-Check 'FAIL' 'AdofaiOnlineMod.dll is not in BepInEx\plugins.' 'O AdofaiOnlineMod.dll nao esta em BepInEx\plugins.' 'AdofaiOnlineMod.dll이 BepInEx\plugins에 없습니다.'
    } else {
        Add-Check 'OK' ("AdofaiOnlineMod.dll is in BepInEx\plugins (" + (Get-Item -LiteralPath $dll).Length + " bytes).") ("AdofaiOnlineMod.dll esta em BepInEx\plugins (" + (Get-Item -LiteralPath $dll).Length + " bytes).") ("AdofaiOnlineMod.dll이 BepInEx\plugins에 있습니다(" + (Get-Item -LiteralPath $dll).Length + " bytes).")
        $blocked = $null
        try { $blocked = Get-Item -LiteralPath $dll -Stream Zone.Identifier -ErrorAction SilentlyContinue } catch { }
        if ($blocked) { Add-Check 'WARN' 'Windows marked the mod file as downloaded from the internet. Right-click it > Properties > Unblock (the installer does this for you).' 'O Windows marcou o arquivo do mod como baixado da internet. Clique com o botao direito > Propriedades > Desbloquear (o instalador faz isso por voce).' 'Windows가 모드 파일을 인터넷에서 받은 파일로 표시했습니다. 우클릭 > 속성 > 차단 해제 (설치 프로그램이 대신 해 줍니다).' }
    }
    $copies = @(Get-ChildItem -LiteralPath (Join-Path $gameDir 'BepInEx') -Recurse -Filter 'AdofaiOnline*.dll' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne $dll })
    if ($copies.Count -gt 0) { Add-Check 'WARN' ('Other copies of the mod found (delete them): ' + (($copies | ForEach-Object { $_.FullName.Substring($gameDir.Length) }) -join ', ')) ('Outras copias do mod encontradas (apague): ' + (($copies | ForEach-Object { $_.FullName.Substring($gameDir.Length) }) -join ', ')) ('다른 모드 사본이 있습니다(삭제하세요): ' + (($copies | ForEach-Object { $_.FullName.Substring($gameDir.Length) }) -join ', ')) }

    # antivirus (Windows Defender): detections involving the game folder or the mod
    try {
        $hits = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object { (($_.Resources -join ' ') -match [regex]::Escape($gameDir)) -or (($_.Resources -join ' ') -match 'winhttp|AdofaiOnline|BepInEx') })
        if ($hits.Count -gt 0) { Add-Check 'WARN' 'Windows Security reported detections involving the game folder or BepInEx. Restore them and add the game folder as an exclusion.' 'A Seguranca do Windows registrou deteccoes envolvendo a pasta do jogo ou o BepInEx. Restaure os arquivos e adicione a pasta do jogo como excecao.' 'Windows 보안이 게임 폴더 또는 BepInEx와 관련된 항목을 감지했습니다. 복원하고 게임 폴더를 예외로 추가하세요.' }
    } catch { }

    if (-not $withLog) { return }

    # what BepInEx did the last time the game ran
    $logPath = Join-Path $gameDir 'BepInEx\LogOutput.log'
    if (-not (Test-Path -LiteralPath $logPath)) {
        Add-Check 'FAIL' 'BepInEx\LogOutput.log does not exist: BepInEx has never run with this game. Start the game once. If the file still does not appear: the antivirus removed winhttp.dll, the game is not this folder (another Steam library?), or you play on Linux/Steam Deck (launch option: WINEDLLOVERRIDES="winhttp=n,b" %command%).' 'BepInEx\LogOutput.log nao existe: o BepInEx nunca rodou com este jogo. Abra o jogo uma vez. Se o arquivo continuar sem aparecer: o antivirus removeu o winhttp.dll, a pasta nao e a do jogo que voce abre (outra biblioteca do Steam?) ou voce joga no Linux/Steam Deck (opcao de inicializacao: WINEDLLOVERRIDES="winhttp=n,b" %command%).' 'BepInEx\LogOutput.log가 없습니다. BepInEx가 이 게임에서 한 번도 실행되지 않았습니다. 게임을 한 번 실행하세요. 그래도 없으면 백신이 winhttp.dll을 삭제했거나, 실행하는 게임 폴더가 다르거나(다른 Steam 라이브러리?), Linux/Steam Deck에서 플레이 중입니다(실행 옵션: WINEDLLOVERRIDES="winhttp=n,b" %command%).'
        return
    }
    $lines = Read-SharedLines $logPath
    $plugins = @($lines | ForEach-Object { if ($_ -match 'Loading \[(.+?)\]') { $Matches[1] } })
    $mod = @($plugins | Where-Object { $_ -like 'ADOFAI Online*' })
    if ($mod.Count -eq 0) {
        $others = if ($plugins.Count -gt 0) { ($plugins -join ', ') } else { 'none' }
        Add-Check 'FAIL' "BepInEx ran, but it did not load the mod. Plugins it loaded: $others. Check that AdofaiOnlineMod.dll is directly in BepInEx\plugins and is not blocked." "O BepInEx rodou, mas nao carregou o mod. Plugins carregados: $others. Confira se o AdofaiOnlineMod.dll esta direto em BepInEx\plugins e nao esta bloqueado." "BepInEx는 실행되었지만 모드를 로드하지 않았습니다. 로드된 플러그인: $others. AdofaiOnlineMod.dll이 BepInEx\plugins 바로 안에 있고 차단되지 않았는지 확인하세요."
        $errs = @($lines | Where-Object { $_ -match 'Error|Exception' } | Select-Object -First 3)
        foreach ($e in $errs) { Add-Check 'WARN' ('Log: ' + $e) ('Log: ' + $e) ('Log: ' + $e) }
        return
    }
    Add-Check 'OK' ("The game loaded the mod: " + $mod[0]) ("O jogo carregou o mod: " + $mod[0]) ("게임이 모드를 로드했습니다: " + $mod[0])
    $patchLines = @($lines | Where-Object { $_ -match 'patch .* falhou|Partes do mod que n' })
    if ($patchLines.Count -gt 0) {
        Add-Check 'FAIL' ('Some parts of the mod do not work with this game version: ' + ($patchLines -join ' | ')) ('Algumas partes do mod nao funcionam nesta versao do jogo: ' + ($patchLines -join ' | ')) ('이 게임 버전에서 모드의 일부가 동작하지 않습니다: ' + ($patchLines -join ' | '))
    } else {
        Add-Check 'OK' 'No patch failed.' 'Nenhum patch falhou.' '실패한 패치가 없습니다.'
    }
    $screens = @($lines | Where-Object { $_ -match 'ligando as telas do online' })
    if ($screens.Count -gt 0) { Add-Check 'OK' 'The Online screens were attached to the level selector.' 'As telas do Online foram ligadas ao seletor de fases.' '온라인 화면이 레벨 선택 화면에 연결되었습니다.' }
    else { Add-Check 'WARN' 'The log does not show the level selector being opened. Open the custom level selector (not the main menu) and run the check again.' 'O log nao mostra o seletor de fases sendo aberto. Abra o seletor de fases personalizadas (nao o menu principal) e rode a verificacao de novo.' '로그에 레벨 선택 화면이 열린 기록이 없습니다. 커스텀 레벨 선택 화면을 연 뒤 다시 확인하세요.' }
}

# plain-text report (always in English) for whoever is helping
function Write-Report($gameDir) {
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('ADOFAI Online - diagnostic report')
    $out.Add('Generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    $out.Add('Windows: ' + [Environment]::OSVersion.VersionString + ' | PowerShell ' + $PSVersionTable.PSVersion + ' | culture ' + (Get-Culture).Name)
    $out.Add('Game folder: ' + $gameDir)
    $out.Add('')
    $out.Add('== Checks')
    foreach ($c in $script:checks) { $out.Add('[' + $c.level + '] ' + $c.en) }
    $out.Add('')
    $out.Add('== Files in BepInEx\plugins')
    $pl = Join-Path $gameDir 'BepInEx\plugins'
    if (Test-Path -LiteralPath $pl) { foreach ($f in Get-ChildItem -LiteralPath $pl -Recurse -File -ErrorAction SilentlyContinue) { $out.Add($f.FullName.Substring($gameDir.Length) + '  ' + $f.Length + ' bytes  ' + $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm')) } } else { $out.Add('(folder does not exist)') }
    $out.Add('')
    $out.Add('== Game folder (top level)')
    foreach ($f in Get-ChildItem -LiteralPath $gameDir -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'winhttp|doorstop|BepInEx|changelog|\.exe$' }) { $out.Add($f.Name) }
    $out.Add('')
    $out.Add('== Last 80 lines of BepInEx\LogOutput.log')
    $logPath = Join-Path $gameDir 'BepInEx\LogOutput.log'
    if (Test-Path -LiteralPath $logPath) { foreach ($l in (Read-SharedLines $logPath | Select-Object -Last 80)) { $out.Add($l) } } else { $out.Add('(no log file)') }
    $text = ($out -join "`r`n")
    # no personal names in the report
    foreach ($name in @($env:USERNAME, $env:COMPUTERNAME)) { if ($name) { $text = $text -ireplace [regex]::Escape($name), '<name>' } }
    $dir = $null
    try { $dir = [Environment]::GetFolderPath('Desktop') } catch { }
    if (-not $dir -or -not (Test-Path -LiteralPath $dir)) { $dir = [IO.Path]::GetTempPath() }
    $file = Join-Path $dir 'ADOFAI-Online-diagnostic.txt'
    [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($true)))
    return $file
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('adofai-online-' + [Guid]::NewGuid().ToString('N'))
$exitCode = 0
try {
    Say ''
    Say "== $($T.title) ==" 'Cyan'

    if (-not $Diagnose -and (Get-Process -Name 'A Dance of Fire and Ice' -ErrorAction SilentlyContinue)) { throw $T.running }

    if (-not $GamePath) {
        Say $T.finding
        $GamePath = Find-Game
    }
    while (-not (Test-GameFolder $GamePath)) {
        if ($GamePath) { Say $T.bad 'Yellow' } else { Say $T.notfound 'Yellow' }
        if ($NoPause) { throw $T.notfound }
        $GamePath = (Read-Host $T.ask).Trim('"', ' ')
    }
    Say ($T.found + $GamePath) 'Green'

    $plugins = Join-Path $GamePath 'BepInEx\plugins'
    $dllTarget = Join-Path $plugins 'AdofaiOnlineMod.dll'

    if ($Diagnose) {
        Say $T.checking
        Test-Installation $GamePath $true
        $bad = @($script:checks | Where-Object { $_.level -eq 'FAIL' }).Count
        $warn = @($script:checks | Where-Object { $_.level -eq 'WARN' }).Count
        Say ''
        if ($bad -gt 0) { Say ($T.verdict + (L "$bad problem(s) found (see the FAIL lines above)." "$bad problema(s) encontrado(s) (veja as linhas FAIL acima)." "문제 $bad개 발견(위의 FAIL 줄 참조).")) 'Red' }
        elseif ($warn -gt 0) { Say ($T.verdict + (L "no failure, $warn warning(s)." "nenhuma falha, $warn aviso(s)." "실패 없음, 경고 $warn개.")) 'Yellow' }
        else { Say ($T.verdict + (L 'everything looks fine.' 'tudo certo.' '모두 정상입니다.')) 'Green' }
        $file = Write-Report $GamePath
        Say ($T.report + $file) 'Cyan'
        Say $T.send
        return
    }

    if ($Uninstall) {
        if (Test-Path -LiteralPath $dllTarget) { Remove-Item -LiteralPath $dllTarget -Force; Say $T.removed 'Green' }
        if ($RemoveBepInEx) {
            foreach ($item in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini', 'changelog.txt', '.doorstop_version')) {
                $p = Join-Path $GamePath $item
                if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
            }
            Say $T.removed_bep 'Green'
        }
        return
    }

    New-Item -ItemType Directory -Path $tmp -Force | Out-Null

    # --- BepInEx
    $haveBepInEx = (Test-Path -LiteralPath (Join-Path $GamePath 'winhttp.dll')) -and
                   (Test-Path -LiteralPath (Join-Path $GamePath 'BepInEx\core\BepInEx.dll'))
    $installedBep = Get-FileVersionSafe (Join-Path $GamePath 'BepInEx\core\BepInEx.dll')
    $needBepInEx = (-not $haveBepInEx)
    if ($haveBepInEx -and $installedBep -and $installedBep -lt $BepInExMin) {
        Say $T.bep_old 'Yellow'
        $needBepInEx = $true
    }
    if (-not $needBepInEx) {
        Say $T.bep_have
    } else {
        if (-not $haveBepInEx) { Say $T.bep_get }
        $zip = Join-Path $tmp 'bepinex.zip'
        Get-File $BepInExUrl $zip
        if ((Get-Sha256 $zip) -ne $BepInExSha) { throw $T.bep_bad }
        Expand-Archive -LiteralPath $zip -DestinationPath $GamePath -Force
    }

    # --- the mod
    Say $T.dll_get
    $dll = Join-Path $tmp 'AdofaiOnlineMod.dll'
    if ($DllSource) {
        Get-File $DllSource $dll
    } else {
        Get-File "$ReleaseBase/AdofaiOnlineMod.dll" $dll
        $sums = Join-Path $tmp 'SHA256SUMS.txt'
        Get-File "$ReleaseBase/SHA256SUMS.txt" $sums
        $expected = $null
        foreach ($line in Get-Content -LiteralPath $sums) {
            if ($line -match '^([0-9a-fA-F]{64})\s+\*?AdofaiOnlineMod\.dll\s*$') { $expected = $Matches[1].ToLowerInvariant() }
        }
        if (-not $expected -or (Get-Sha256 $dll) -ne $expected) { throw $T.dll_bad }
    }
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination $dllTarget -Force
    # files taken from the internet may carry a "blocked" mark: clear it on what we installed
    foreach ($f in @($dllTarget, (Join-Path $GamePath 'winhttp.dll'), (Join-Path $GamePath 'BepInEx\core\BepInEx.dll'))) {
        try { if (Test-Path -LiteralPath $f) { Unblock-File -LiteralPath $f } } catch { }
    }

    Say ''
    Say $T.checking
    Test-Installation $GamePath $false
    Say ''
    $failed = @($script:checks | Where-Object { $_.level -eq 'FAIL' }).Count
    if ($failed -eq 0) {
        Say $T.done 'Green'
        if (-not $haveBepInEx) { Say $T.first }
    }
    Say $T.help
} catch {
    Say ($T.fail + $_.Exception.Message) 'Red'
    $exitCode = 1
} finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not $NoPause) { [void](Read-Host $T.press) }
}
exit $exitCode
