<#
  关闭游戏后把编译好的 IconFix.dll 部署到 r2modman profile「TEST」。

  用法（游戏关闭后）：
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1

  也可以先运行，它会等 DSPGAME 进程退出后再复制。
#>

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $projectRoot 'bin\Release\netstandard2.1\IconFix.dll'
$target = Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles\TEST\BepInEx\plugins\TTenYX-IconFix'

if (-not (Test-Path $dll)) {
    Write-Host "找不到编译产物：$dll" -ForegroundColor Red
    Write-Host "先执行：dotnet build '$projectRoot\IconFix.csproj' -c Release"
    exit 1
}

$proc = Get-Process DSPGAME -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "检测到游戏正在运行（PID $($proc.Id)）。请先关闭游戏，本脚本会等待……" -ForegroundColor Yellow
    while (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
        Start-Sleep -Seconds 2
    }
    Write-Host "游戏已退出，开始部署。" -ForegroundColor Green
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

$files = @($dll)
foreach ($extra in @('manifest.json', 'README.md', 'icon.png')) {
    $p = Join-Path $projectRoot $extra
    if (Test-Path $p) { $files += $p }
}
Copy-Item -Path $files -Destination $target -Force

$srcHash = (Get-FileHash $dll -Algorithm SHA256).Hash
$dstHash = (Get-FileHash (Join-Path $target 'IconFix.dll') -Algorithm SHA256).Hash

Write-Host "已部署到：$target"
Write-Host "源    SHA256：$srcHash"
Write-Host "目标  SHA256：$dstHash"
if ($srcHash -eq $dstHash) {
    Write-Host "校验通过，重启游戏即可生效。" -ForegroundColor Green
} else {
    Write-Host "校验失败：两边哈希不一致，请重新部署。" -ForegroundColor Red
    exit 1
}
