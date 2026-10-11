<#
  把本机 NuGet 缓存里的 .nupkg 导出到项目下的 offline-feed\，用于**无外网时离线编译**。

  为什么需要它：
    `DysonSphereProgram.GameLibs` 是从游戏本体派生的包，不在 nuget.org 上，
    也不能随仓库分发（版权 + 体积）。所以公开仓库无法做到「clone 即可编译」，
    需要你自己准备这个包 —— 只要本机装过 DSP mod 开发环境，它就已在 NuGet 缓存里。

  用法（在项目根目录执行）：
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\export-offline-feed.ps1
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\export-offline-feed.ps1 -GameLibsVersion 0.10.35.29088-r.0

  导出后 offline-feed\ 会被 NuGet.config 当作包源使用，即可脱离外网编译。
#>
[CmdletBinding()]
param(
    [string]$GameLibsVersion = '0.10.35.29088-r.0',
    [string]$Destination     = (Join-Path (Split-Path -Parent $PSScriptRoot) 'offline-feed')
)

$ErrorActionPreference = 'Stop'
$cache = Join-Path $env:USERPROFILE '.nuget\packages'

if (-not (Test-Path $cache)) {
    Write-Host "找不到 NuGet 缓存：$cache" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

# 需要的包（key = NuGet 包 id 小写，value = 版本）
$needed = [ordered]@{
    'dysonsphereprogram.gamelibs' = $GameLibsVersion
    'bepinex.core'                = '5.4.17'
    'bepinex.plugininfoprops'     = '1.0.0'
    'unityengine.modules'         = '2022.3.53'
}

$copied = 0
$missing = @()

foreach ($id in $needed.Keys) {
    $ver = $needed[$id]
    $dir = Join-Path (Join-Path $cache $id) $ver
    if (-not (Test-Path $dir)) {
        $missing += "$id $ver"
        continue
    }
    Get-ChildItem $dir -File -Filter *.nupkg | ForEach-Object {
        Copy-Item $_.FullName -Destination $Destination -Force
        Write-Host "  已导出 $($_.Name)" -ForegroundColor Green
        $script:copied++
    }
}

Write-Host ""
Write-Host "导出目录：$Destination" -ForegroundColor Cyan
Write-Host "已导出 $copied 个包。"

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "以下包在本机缓存中不存在，离线编译会失败：" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    Write-Host "先联网编译一次让 NuGet 把它们下载到缓存，再重跑本脚本。" -ForegroundColor Yellow
}
