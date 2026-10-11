<#
  发布 DarkFogWorkshop 到 Thunderstore。

  前置：需要 Thunderstore 的团队 Service Account token。
    获取位置：https://thunderstore.io/ → 右上角 Settings → Teams →
              选中/创建你的团队 → 左侧 Service Accounts → 新建一个
    （不是账号密码，是团队的服务账号 token。）

  Token 的提供方式（二选一，脚本不会回显 token）：
    1. 环境变量（当前会话有效）：
           $env:THUNDERSTORE_TOKEN = '<你的 token>'
    2. 本地文件（长期有效，已在 .gitignore 中，不会被提交）：
           DarkFogWorkshop\.thunderstore-token

  用法：
      # 只校验，不真的上传
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-thunderstore.ps1 -DryRun

      # 真正上传
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\publish-thunderstore.ps1

      # 团队名不是 TTenYX 时指定
      ... -Namespace <你的团队名>

  注意：Thunderstore 上的包是**不可删除**的，只能弃用（Deprecate）。
        版本号 `manifest.json` 里的 `version_number` 一经发布即被占用，
        重复发布同一版本会被拒绝。所以先跑 -DryRun 看清要发什么。
#>
[CmdletBinding()]
param(
    [string]$Namespace = 'TTenYX',
    [string]$TokenFile = '',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
# $PSScriptRoot 在 param 默认值里取不到，所以放到这里算
if (-not $TokenFile) { $TokenFile = Join-Path $projectRoot '.thunderstore-token' }
$zipPath     = Join-Path $projectRoot 'DarkFogWorkshop.zip'
$manifestPath = Join-Path $projectRoot 'manifest.json'

function Fail($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

# —— 1. 检查产物与清单 ——
if (-not (Test-Path $zipPath))     { Fail "找不到发布包：$zipPath`n先运行 tools\build-package.ps1 或手动打包。" }
if (-not (Test-Path $manifestPath)) { Fail "找不到 manifest.json：$manifestPath" }

$manifest = [System.IO.File]::ReadAllText($manifestPath, (New-Object System.Text.UTF8Encoding $false)) | ConvertFrom-Json
$pkgName  = $manifest.name
$pkgVer   = $manifest.version_number

Write-Host "=== 将要发布 ===" -ForegroundColor Cyan
Write-Host "  团队 / 命名空间 : $Namespace"
Write-Host "  包名            : $pkgName"
Write-Host "  版本            : $pkgVer"
Write-Host "  文件            : $zipPath"
Write-Host "  大小            : $((Get-Item $zipPath).Length) 字节"
Write-Host "  SHA256          : $((Get-FileHash $zipPath -Algorithm SHA256).Hash)"

# —— 2. 校验 zip 根目录必须有 Thunderstore 要求的文件 ——
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $roots = $archive.Entries |
        Where-Object { $_.FullName -notmatch '/' } |
        ForEach-Object { $_.Name }
    $required = @('manifest.json', 'icon.png', 'README.md')
    $missing  = $required | Where-Object { $roots -notcontains $_ }
    if ($missing) {
        Fail "发布包根目录缺少必需文件：$($missing -join ', ')`n当前根目录：$($roots -join ', ')"
    }
    Write-Host ""
    Write-Host "=== zip 根目录文件（Thunderstore 要求）===" -ForegroundColor Cyan
    $roots | Sort-Object | ForEach-Object { Write-Host "  $_" }
}
finally { $archive.Dispose() }

# —— 3. -DryRun：只校验，不需要 token ——
if ($DryRun) {
    Write-Host ""
    Write-Host "=== -DryRun：以上校验全部通过，未上传 ===" -ForegroundColor Yellow
    Write-Host "去掉 -DryRun 即可真正发布（届时才需要 token）。"
    exit 0
}

# —— 4. 取 token（绝不回显）——
$token = $env:THUNDERSTORE_TOKEN
$tokenSource = 'THUNDERSTORE_TOKEN 环境变量'
if (-not $token -and (Test-Path $TokenFile)) {
    $token = ([System.IO.File]::ReadAllText($TokenFile)).Trim()
    $tokenSource = $TokenFile
}
if (-not $token) {
    Fail @"
找不到 Thunderstore token。请任选一种方式提供：
  1. `$env:THUNDERSTORE_TOKEN = '<你的 token>'
  2. 把 token 写入文件：$TokenFile

token 获取位置：https://thunderstore.io/ → Settings → Teams → 你的团队 → Service Accounts
"@
}
Write-Host ""
Write-Host "  token 来源：$tokenSource（长度 $($token.Length)，已隐藏）" -ForegroundColor DarkGray

# —— 5. 发布 ——
$tcli = Join-Path $env:USERPROFILE '.dotnet\tools\tcli.exe'
if (-not (Test-Path $tcli)) {
    $cmd = Get-Command tcli -ErrorAction SilentlyContinue
    if ($cmd) { $tcli = $cmd.Source }
    else { Fail "找不到 tcli。先安装：dotnet tool install -g tcli" }
}

Write-Host ""
Write-Host "=== 上传中 ===" -ForegroundColor Cyan
& $tcli publish `
    --file $zipPath `
    --token $token `
    --package-namespace $Namespace `
    --package-name $pkgName `
    --package-version $pkgVer

$code = $LASTEXITCODE
Write-Host ""
if ($code -eq 0) {
    Write-Host "发布成功。" -ForegroundColor Green
    Write-Host "页面：https://thunderstore.io/c/dyson-sphere-program/p/$Namespace/$pkgName/"
} else {
    Write-Host "发布失败（退出码 $code）。常见原因：" -ForegroundColor Red
    Write-Host "  · 团队名不对 —— 用 -Namespace <团队名> 指定"
    Write-Host "  · 该版本号已存在 —— Thunderstore 版本号不可复用，需提升 manifest.json 的 version_number"
    Write-Host "  · token 无效或已撤销 —— 到 Team → Service Accounts 重新生成"
    exit $code
}
