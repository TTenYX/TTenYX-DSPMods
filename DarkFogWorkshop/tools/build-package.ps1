<#
  编译并打出 Thunderstore 发布包 DarkFogWorkshop.zip。

  用法：
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-package.ps1

  做四件事：
    1. dotnet build -c Release
    2. 组装 zip 内容：DLL + manifest.json + README.md + CHANGELOG.md + icon.png
    3. **文本文件统一按 LF 写入**，保证打包字节与仓库存储一致
       （否则 git 检出时 LF→CRLF，包内 README/manifest 大小与校验值会对不上）
    4. 打印每个文件的 SHA256

  Thunderstore 要求 zip 根目录必须有 manifest.json / icon.png / README.md，
  本脚本会自检这一点。
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$csproj      = Join-Path $projectRoot 'DarkFogWorkshop.csproj'
$dll         = Join-Path $projectRoot "bin\$Configuration\netstandard2.1\DarkFogWorkshop.dll"
$zipPath     = Join-Path $projectRoot 'DarkFogWorkshop.zip'
$staging     = Join-Path $projectRoot 'obj\package-staging'

$utf8NoBom = New-Object System.Text.UTF8Encoding $false

Write-Host "=== 1/4 编译 ===" -ForegroundColor Cyan
dotnet build $csproj -c $Configuration | Select-String -Pattern 'error|warning CS|Build succeeded|Build FAILED'
if (-not (Test-Path $dll)) { Write-Host "编译产物不存在：$dll" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "=== 2/4 组装包内容 ===" -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

# 二进制文件原样复制
Copy-Item $dll $staging -Force
Copy-Item (Join-Path $projectRoot 'icon.png') $staging -Force

# 文本文件按 LF 写入（关键：避免 CRLF 导致校验值漂移）
$textFiles = @('manifest.json', 'README.md', 'CHANGELOG.md')
foreach ($f in $textFiles) {
    $src = Join-Path $projectRoot $f
    if (-not (Test-Path $src)) { continue }
    $text = [System.IO.File]::ReadAllText($src, $utf8NoBom)
    $text = $text.TrimStart([char]0xFEFF)          # 去掉可能的 BOM
    $text = $text -replace "`r`n", "`n"            # 统一 LF
    [System.IO.File]::WriteAllText((Join-Path $staging $f), $text, $utf8NoBom)
}
Get-ChildItem $staging -File | ForEach-Object { Write-Host "  $($_.Name)  $($_.Length) 字节" }

Write-Host ""
Write-Host "=== 3/4 自检：根目录必需文件 ===" -ForegroundColor Cyan
$required = @('manifest.json', 'icon.png', 'README.md')
$present  = (Get-ChildItem $staging -File).Name
$missing  = $required | Where-Object { $present -notcontains $_ }
if ($missing) { Write-Host "缺少：$($missing -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "  manifest.json / icon.png / README.md  均在根目录 OK" -ForegroundColor Green

$manifest = [System.IO.File]::ReadAllText((Join-Path $staging 'manifest.json'), $utf8NoBom) | ConvertFrom-Json
Write-Host "  包名 $($manifest.name)   版本 $($manifest.version_number)"

Write-Host ""
Write-Host "=== 4/4 打包 ===" -ForegroundColor Cyan
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -Force

Write-Host "  $zipPath" -ForegroundColor Green
Write-Host "  $((Get-Item $zipPath).Length) 字节"
Write-Host "  SHA256 $((Get-FileHash $zipPath -Algorithm SHA256).Hash)"
Write-Host ""
Write-Host "包内各文件 SHA256：" -ForegroundColor Cyan
Get-ChildItem $staging -File | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-22} {1,8}  {2}" -f $_.Name, $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash)
}
