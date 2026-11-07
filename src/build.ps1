# Тихий ПК — збирання програми й інсталятора.
# Запускай подвійним кліком по build.bat. Потрібен лише Windows 10/11 та інтернет під час першого збирання.
# Те саме робить GitHub Actions (.github/workflows/build.yml): Actions → «Збирання» → «Run workflow».
#   1) один раз завантажує бібліотеку датчиків LibreHardwareMonitor у папку lib;
#   2) збирає TyhyiPK.exe вбудованим компілятором Windows і додає бібліотеку датчиків як ресурси
#      (при встановленні вони розкладаються звичайними файлами в папку lib);
#   3) інсталятор Tykhyi-PK-setup.exe — та сама програма під іншою назвою, без обгорток IExpress:
#      файл з «setup» у назві ставить себе в папку користувача. Драйвер PawnIO ставиться з pawnio.eu.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
$log = Join-Path $root 'build_log.txt'
Set-Content -Path $log -Value ('Збирання ' + (Get-Date)) -Encoding UTF8
function Say([string]$t) { Write-Host $t; Add-Content -Path $log -Value $t -Encoding UTF8 }

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    $ProgressPreference = 'SilentlyContinue'

    # ── 1. Бібліотека датчиків (папка lib, завантажується один раз) ──
    $lib = Join-Path $root 'lib'
    if (!(Test-Path (Join-Path $lib 'LibreHardwareMonitorLib.dll'))) {
        Say 'Завантажую бібліотеку датчиків LibreHardwareMonitor 0.9.6 (один раз)...'
        $zip = Join-Path $env:TEMP 'TyhyiPK_LHM.zip'
        $tmp = Join-Path $env:TEMP 'TyhyiPK_LHM'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' -OutFile $zip
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $tmp -Force
        New-Item -ItemType Directory -Force -Path $lib | Out-Null
        # Лише бібліотеки (без графічної частини самого LibreHardwareMonitor) і текст ліцензії.
        Get-ChildItem $tmp -Recurse -Filter *.dll | Where-Object { $_.Name -notmatch '^(Aga\.Controls|OxyPlot)' } | Copy-Item -Destination $lib -Force
        Get-ChildItem $tmp -Recurse -File | Where-Object { $_.Name -match 'license' } | Copy-Item -Destination $lib -Force
        Remove-Item $zip, $tmp -Recurse -Force -ErrorAction SilentlyContinue
        if (!(Test-Path (Join-Path $lib 'LibreHardwareMonitorLib.dll'))) { throw 'В архіві LibreHardwareMonitor не знайдено LibreHardwareMonitorLib.dll' }
    }
    # Ліцензія бібліотеки (MPL 2.0) має йти разом із програмою.
    $licFile = Join-Path $lib 'LibreHardwareMonitor_LICENSE.txt'
    if (!(Test-Path $licFile)) {
        $found = Get-ChildItem $lib -File | Where-Object { $_.Name -match 'license' -and $_.Extension -ne '.dll' } | Select-Object -First 1
        if ($found) { Copy-Item $found.FullName $licFile -Force }
        else {
            try { Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/master/LICENSE' -OutFile $licFile } catch { }
        }
    }

    # ── 2. Програма ──
    $exePath = Join-Path $root 'TyhyiPK.exe'
    if (Test-Path $exePath) {
        try { [IO.File]::Open($exePath, 'Open', 'ReadWrite', 'None').Close() }
        catch { throw 'Тихий ПК зараз запущений з цієї папки. Закрий його (правою кнопкою на значку біля годинника → Вийти) і запусти збирання ще раз.' }
    }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (!(Test-Path $csc)) { throw 'Не знайдено компілятор .NET Framework 4 (csc.exe).' }
    $cargs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/utf8output', '/win32icon:app.ico', '/out:TyhyiPK.exe',
        '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Management.dll')
    $dlls = @(Get-ChildItem $lib -Filter *.dll -ErrorAction SilentlyContinue)
    foreach ($d in $dlls) { $cargs += ('/resource:' + $d.FullName + ',lhm.' + $d.Name) }
    if (Test-Path $licFile) { $cargs += ('/resource:' + $licFile + ',lhm.LICENSE.txt') }
    $cargs += @(Get-ChildItem $root -Filter *.cs | ForEach-Object { $_.Name })
    Say ('Збираю TyhyiPK.exe (вшиваю бібліотек датчиків: ' + $dlls.Count + ')...')
    $ErrorActionPreference = 'Continue'
    $out = & $csc @cargs 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $out | ForEach-Object { Say ([string]$_) }
    if ($code -ne 0) { throw 'Помилка компіляції — подробиці вище і в build_log.txt' }
    Say 'Готово: TyhyiPK.exe'

    # ── 3. Інсталятор — та сама програма під назвою з «setup» ──
    Remove-Item (Join-Path $root 'Tykhyi-PK-setup.SED') -Force -ErrorAction SilentlyContinue
    Copy-Item $exePath (Join-Path $root 'Tykhyi-PK-setup.exe') -Force
    Say 'Готово: Tykhyi-PK-setup.exe — його й надсилай другові.'
}
catch {
    Say ('ПОМИЛКА: ' + $_.Exception.Message)
    exit 1
}
