<#
.SYNOPSIS
  One-click installer for the ADOFAI Online mod (PC, Steam). / Instalador de um clique do mod ADOFAI Online. / 한 번에 설치.

.DESCRIPTION
  1. Finds your copy of "A Dance of Fire and Ice" (Steam).
  2. Installs BepInEx 5 (x64) into the game folder if it is not there yet (pinned, checksum-verified version).
  3. Downloads the latest AdofaiOnlineMod.dll from GitHub, checks its SHA-256 and puts it in BepInEx\plugins.
  Running it again updates the mod. Nothing outside the game folder is changed.

.PARAMETER GamePath   Game folder (the one with "A Dance of Fire and Ice.exe"). Detected automatically when omitted.
.PARAMETER Uninstall  Removes the mod (and, with -RemoveBepInEx, BepInEx too).
.PARAMETER DllSource  Use this DLL (local path or URL) instead of the latest release. Mostly for testing.
#>
param(
    [string]$GamePath,
    [switch]$Uninstall,
    [switch]$RemoveBepInEx,
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
        bep_bad = 'The BepInEx download did not match the expected checksum. Nothing was changed.'
        dll_get = 'Downloading the mod...'; dll_bad = 'The mod download did not match its checksum. Nothing was installed.'
        done = 'Done! Start the game, open the custom level selector and click Online.'
        first = 'Tip: start the game once so BepInEx creates its folders (the mod works right away).'
        removed = 'Mod removed.'; removed_bep = 'BepInEx removed.'
        fail = 'Something went wrong: '; press = 'Press Enter to close...'
    }
    pt = @{
        title = 'ADOFAI Online - instalador'; running = 'Feche o A Dance of Fire and Ice e rode de novo.'
        finding = 'Procurando o jogo...'; found = 'Jogo encontrado: '; notfound = 'Nao consegui achar o jogo sozinho.'
        ask = 'Cole a pasta do jogo (a que tem o A Dance of Fire and Ice.exe): '; bad = 'Essa pasta nao tem o jogo.'
        bep_have = 'O BepInEx ja esta instalado.'; bep_get = 'Instalando o BepInEx 5 (baixando ~0,6 MB)...'
        bep_bad = 'O download do BepInEx nao bateu com a soma de verificacao esperada. Nada foi alterado.'
        dll_get = 'Baixando o mod...'; dll_bad = 'O download do mod nao bateu com a soma de verificacao. Nada foi instalado.'
        done = 'Pronto! Abra o jogo, entre no seletor de fases personalizadas e clique em Online.'
        first = 'Dica: abra o jogo uma vez para o BepInEx criar as pastas (o mod ja funciona).'
        removed = 'Mod removido.'; removed_bep = 'BepInEx removido.'
        fail = 'Algo deu errado: '; press = 'Aperte Enter para fechar...'
    }
    ko = @{
        title = 'ADOFAI Online - 설치 프로그램'; running = 'A Dance of Fire and Ice를 종료한 뒤 다시 실행하세요.'
        finding = '게임을 찾는 중...'; found = '게임 폴더: '; notfound = '게임을 자동으로 찾지 못했습니다.'
        ask = '게임 폴더(A Dance of Fire and Ice.exe가 있는 폴더)를 붙여 넣으세요: '; bad = '이 폴더에는 게임이 없습니다.'
        bep_have = 'BepInEx가 이미 설치되어 있습니다.'; bep_get = 'BepInEx 5 설치 중(약 0.6 MB 다운로드)...'
        bep_bad = 'BepInEx 다운로드의 체크섬이 일치하지 않습니다. 아무것도 변경하지 않았습니다.'
        dll_get = '모드 다운로드 중...'; dll_bad = '모드 다운로드의 체크섬이 일치하지 않습니다. 아무것도 설치하지 않았습니다.'
        done = '완료! 게임을 실행하고 커스텀 레벨 선택 화면에서 온라인을 누르세요.'
        first = '팁: 게임을 한 번 실행하면 BepInEx가 폴더를 만듭니다(모드는 바로 동작합니다).'
        removed = '모드를 제거했습니다.'; removed_bep = 'BepInEx를 제거했습니다.'
        fail = '문제가 발생했습니다: '; press = 'Enter를 눌러 닫기...'
    }
}
$T = $M[$lang]

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

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

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('adofai-online-' + [Guid]::NewGuid().ToString('N'))
$exitCode = 0
try {
    Say ''
    Say "== $($T.title) ==" 'Cyan'

    if (Get-Process -Name 'A Dance of Fire and Ice' -ErrorAction SilentlyContinue) { throw $T.running }

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
    if ($haveBepInEx) {
        Say $T.bep_have
    } else {
        Say $T.bep_get
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

    Say ''
    Say $T.done 'Green'
    if (-not $haveBepInEx) { Say $T.first }
} catch {
    Say ($T.fail + $_.Exception.Message) 'Red'
    $exitCode = 1
} finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not $NoPause) { [void](Read-Host $T.press) }
}
exit $exitCode
