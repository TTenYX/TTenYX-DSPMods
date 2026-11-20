param([int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$root = 'X:\戴森球计划\【MOD】修改仓库\1.我的MOD\BlueprintBeltSaver'
$test = Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles\TEST\BepInEx'
$plugins = Join-Path $test 'plugins'
$game = 'D:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$started = Get-Date
$reports = Join-Path $root ('tools\restorecheck\reports\ingame-' + $stamp)
$disabled = Join-Path $test ('_restoretest_disabled_' + $stamp)
$selftest = Join-Path $plugins 'BBSaver.SelfTest'
$dst = Join-Path $plugins 'BlueprintBeltSaver\BlueprintBeltSaver.dll'
$src = Join-Path $root 'bin\Release\netstandard2.1\BlueprintBeltSaver.dll'
$tester = Join-Path $root 'tools\selftest\bin\Release\netstandard2.1\BBSaver.SelfTest.dll'
if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { throw 'DSPGAME is already running; do not close a user instance.' }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running.' }
if (Test-Path $selftest) { throw 'Existing selftest directory is not owned by this run.' }
New-Item -ItemType Directory -Path $reports, $disabled | Out-Null
$manifest = @(Get-ChildItem -LiteralPath $plugins | Where-Object { $_.Name -ne 'BlueprintBeltSaver' })
$manifest.Name | Out-File -LiteralPath (Join-Path $reports 'moved-plugins.txt') -Encoding utf8
$moved = @()
$proc = $null
$success = $false
try {
    Copy-Item -LiteralPath $dst -Destination ($dst + '.bak-' + $stamp)
    Copy-Item -LiteralPath $src -Destination $dst -Force
    $h = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash -ne $h) { throw 'Deployment hash mismatch.' }
    if (-not ([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dst)).Contains('BlueprintGridRestorer'))) { throw 'Restoration symbol not found in deployed DLL.' }
    [IO.File]::WriteAllText((Join-Path $reports 'deploy.txt'), "SHA256=$h`nbackup=$dst.bak-$stamp`n", (New-Object Text.UTF8Encoding($false)))
    foreach ($entry in $manifest) { Move-Item -LiteralPath $entry.FullName -Destination $disabled; $moved += $entry.Name }
    New-Item -ItemType Directory -Path $selftest | Out-Null
    Copy-Item -LiteralPath $tester -Destination (Join-Path $selftest 'BBSaver.SelfTest.dll')
    [IO.File]::WriteAllText((Join-Path $selftest 'RUN_TESTS'), 'restore-test', (New-Object Text.UTF8Encoding($false)))
    $loader = Join-Path $test 'core\BepInEx.Preloader.dll'
    $proc = Start-Process -FilePath (Join-Path $game 'DSPGAME.exe') -WorkingDirectory $game -ArgumentList ('--doorstop-enable true --doorstop-target "' + $loader + '"') -PassThru
    [IO.File]::WriteAllText((Join-Path $reports 'game-pid.txt'), $proc.Id.ToString())
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $log = Join-Path $selftest 'selftest.log'
    $summary = $null
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $log) {
            $lines = [IO.File]::ReadAllLines($log)
            $summary = @($lines | Where-Object { $_ -match '^SUMMARY PASS=\d+ FAIL=\d+' } | Select-Object -Last 1)
            if ($summary.Count -gt 0) { break }
        }
        $proc.Refresh()
        if ($proc.HasExited) { throw 'Game exited before a complete selftest report.' }
        Start-Sleep -Seconds 2
    }
    if (-not $summary -or $summary.Count -eq 0) { throw 'Selftest timed out.' }
    Write-Output $summary
    if ($summary[0] -notmatch 'FAIL=0$') { throw 'Selftest failed; see archived report.' }
    $success = $true
    $proc.WaitForExit(10000) | Out-Null
} finally {
    # Stop ONLY the process this script started. No name-wide taskkill.
    if ($proc) { $proc.Refresh(); if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit() } }
    if (Test-Path $selftest) {
        Remove-Item -LiteralPath (Join-Path $selftest 'RUN_TESTS') -ErrorAction SilentlyContinue
        foreach ($name in @('selftest.log','addon-dump.txt','restored-B')) {
            $path = Join-Path $selftest $name
            if (Test-Path $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $reports $name) -Recurse }
        }
        # Exact owned test directory; final DLL remains deployed (unless test failed below).
        Remove-Item -LiteralPath $selftest -Recurse
    }
    $auditDir = Join-Path $test 'config\BlueprintBeltSaver-restore-audit'
    if (Test-Path $auditDir) {
        $audits = @(Get-ChildItem -LiteralPath $auditDir -Filter '*.csv' | Where-Object { $_.LastWriteTime -ge $started })
        if ($audits.Count -gt 0) {
            $auditOut = Join-Path $reports 'protected-points'
            New-Item -ItemType Directory -Path $auditOut | Out-Null
            foreach ($audit in $audits) { Copy-Item -LiteralPath $audit.FullName -Destination $auditOut }
        }
    }
    if (Test-Path (Join-Path $test 'LogOutput.log')) { Copy-Item -LiteralPath (Join-Path $test 'LogOutput.log') -Destination (Join-Path $reports 'LogOutput.log') }
    foreach ($name in $moved) { Move-Item -LiteralPath (Join-Path $disabled $name) -Destination $plugins }
    if (@(Get-ChildItem -LiteralPath $disabled).Count -eq 0) { Remove-Item -LiteralPath $disabled }
    if (-not $success -and (Test-Path ($dst + '.bak-' + $stamp))) { Copy-Item -LiteralPath ($dst + '.bak-' + $stamp) -Destination $dst -Force }
    Write-Output ('REPORTS=' + $reports)
    Write-Output ('PLUGINS=' + @(Get-ChildItem -LiteralPath $plugins).Count + ' RESTORED=' + $moved.Count)
}
if (-not $success) { exit 1 }
