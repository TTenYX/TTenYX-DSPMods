# 自动部署等待器：等游戏（DSPGAME）退出后，把最新构建产物复制进 r2modman profile。
# 用法：powershell -File auto-deploy-wait.ps1 [-TimeoutMinutes 240]
# 由 DarkFogWorkshop.csproj 在“游戏运行导致部署失败”时自动拉起，也可手动运行。
param(
    [int]$TimeoutMinutes = 240
)
$ErrorActionPreference = 'Continue'
$src  = Join-Path $PSScriptRoot '..\bin\Release\netstandard2.1'
$prof = Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles\0.10.35 new\BepInEx\plugins\TTenYX-DarkFogWorkshop'
$log  = Join-Path $PSScriptRoot 'auto-deploy.log'

function Write-Log([string]$m) {
    Add-Content -Path $log -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) -Encoding UTF8
}

Write-Log ("watcher start (timeout {0} min)" -f $TimeoutMinutes)

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ($true) {
    $game = Get-Process DSPGAME -ErrorAction SilentlyContinue
    if (-not $game) { break }
    if ((Get-Date) -gt $deadline) {
        Write-Log "timeout, exit without deploy"
        exit 1
    }
    Start-Sleep -Seconds 5
}

Write-Log "game closed, deploying..."
try {
    if (-not (Test-Path $prof)) { throw "profile dir not found: $prof" }
    foreach ($f in @('DarkFogWorkshop.dll', 'manifest.json', 'README.md', 'CHANGELOG.md')) {
        $s = Join-Path $src $f
        if (Test-Path $s) { Copy-Item $s $prof -Force }
    }
    $a = (Get-FileHash (Join-Path $src 'DarkFogWorkshop.dll') -Algorithm SHA256).Hash
    $b = (Get-FileHash (Join-Path $prof 'DarkFogWorkshop.dll') -Algorithm SHA256).Hash
    if ($a -eq $b) { Write-Log "deploy OK (hash match)" }
    else { Write-Log "deploy finished but hash MISMATCH" }
}
catch {
    Write-Log ("deploy FAILED: " + $_.Exception.Message)
}
