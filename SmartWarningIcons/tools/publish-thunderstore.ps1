<#
  用 tcli 构建 SmartWarningIcons 发布包并发布到 Thunderstore。

  前置：需要 Thunderstore 团队 Service Account token（获取方式见文末）。

  Token 的提供方式（二选一，脚本不会回显 token）：
    1. 环境变量（当前会话有效）：
           $env:THUNDERSTORE_TOKEN = '<你的 token>'
    2. 本地文件（长期有效，已在 .gitignore 中，不会被提交）：
           SmartWarningIcons\.thunderstore-token
       （DarkFogWorkshop\.thunderstore-token 存在时也会被复用——同一个团队。）

  用法：
      # 只构建 + 校验包内容，不上传
      powershell -NoProfile -ExecutionPolicy Bypass -File SmartWarningIcons\tools\publish-thunderstore.ps1 -DryRun

      # 真正上传
      powershell -NoProfile -ExecutionPolicy Bypass -File SmartWarningIcons\tools\publish-thunderstore.ps1

  注意：Thunderstore 上的包是**不可删除**的，只能弃用（Deprecate）。
        版本号一经发布即被占用，重复发布同一版本会被拒绝。所以先跑 -DryRun。
#>
[CmdletBinding()]
param(
    [string]$Namespace = 'TTenYX',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$tomlPath = Join-Path $projectRoot 'thunderstore.toml'
$distDir = Join-Path $projectRoot 'dist'

function Fail($msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

# —— 1. 编译并打包 ——
Write-Host "=== 编译 ===" -ForegroundColor Cyan
dotnet build (Join-Path $projectRoot 'SmartWarningIcons.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Fail "编译失败。" }

$tcli = Join-Path $env:USERPROFILE '.dotnet\tools\tcli.exe'
if (-not (Test-Path $tcli)) {
    $cmd = Get-Command tcli -ErrorAction SilentlyContinue
    if ($cmd) { $tcli = $cmd.Source }
    else { Fail "找不到 tcli。先安装：dotnet tool install -g tcli" }
}

Write-Host ""
Write-Host "=== tcli build ===" -ForegroundColor Cyan
& $tcli build --config-path $tomlPath
if ($LASTEXITCODE -ne 0) { Fail "tcli build 失败。" }

$zipPath = Get-ChildItem $distDir -Filter *.zip |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $zipPath) { Fail "dist 里没有生成 zip。" }
$zipPath = $zipPath.FullName

# —— 2. 校验 zip 根目录必须有 Thunderstore 要求的文件 + assets 目录 ——
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = $archive.Entries | ForEach-Object { $_.FullName }
    $roots = $entries | Where-Object { $_ -notmatch '/' }
    $required = @('manifest.json', 'icon.png', 'README.md')
    $missing = $required | Where-Object { $roots -notcontains $_ }
    if ($missing) {
        Fail "发布包根目录缺少必需文件：$($missing -join ', ')`n当前根目录：$($roots -join ', ')"
    }
    if (-not ($entries | Where-Object { $_ -like 'assets/*' })) {
        Fail "发布包缺少 assets 目录（插件运行时要加载 assets\InsufficientInput*.png）。"
    }

    Write-Host ""
    Write-Host "=== 将要发布 ===" -ForegroundColor Cyan
    Write-Host "  包    : $zipPath"
    Write-Host "  大小  : $((Get-Item $zipPath).Length) 字节"
    Write-Host "  SHA256: $((Get-FileHash $zipPath -Algorithm SHA256).Hash)"
    Write-Host ""
    Write-Host "=== zip 内容 ===" -ForegroundColor Cyan
    $entries | Sort-Object | ForEach-Object { Write-Host "  $_" }
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
if (-not $token) {
    $ownTokenFile = Join-Path $projectRoot '.thunderstore-token'
    if (Test-Path $ownTokenFile) { $TokenFile = $ownTokenFile }
    else {
        $sibling = Join-Path (Split-Path -Parent $projectRoot) 'DarkFogWorkshop\.thunderstore-token'
        if (Test-Path $sibling) { $TokenFile = $sibling }
    }
    if ($TokenFile) {
        # 记事本保存的文件可能带 UTF-8 BOM（U+FEFF），Trim() 不会去掉它，需显式剔除
        $token = ([System.IO.File]::ReadAllText($TokenFile)).Trim([char]0xFEFF).Trim()
        $tokenSource = $TokenFile
    }
}
if (-not $token) {
    Fail @"
找不到 Thunderstore token。请任选一种方式提供：
  1. `$env:THUNDERSTORE_TOKEN = '<你的 token>'
  2. 把 token 写入文件：$(Join-Path $projectRoot '.thunderstore-token')

token 获取位置：https://thunderstore.io/ → Settings → Teams → 你的团队 → Service Accounts
"@
}
Write-Host ""
Write-Host "  token 来源：$tokenSource（长度 $($token.Length)，已隐藏）" -ForegroundColor DarkGray

# —— 5. 发布 ——
Write-Host ""
Write-Host "=== 上传中 ===" -ForegroundColor Cyan
& $tcli publish --file $zipPath --token $token --config-path $tomlPath 2>&1 | ForEach-Object {
    # 万一输出里带 token，先屏蔽再打印
    $line = "$_"
    if ($token -and $line.Contains($token)) { $line = $line.Replace($token, '***') }
    Write-Host $line
}

$code = $LASTEXITCODE
Write-Host ""
if ($code -eq 0) {
    Write-Host "发布成功。" -ForegroundColor Green
    Write-Host "页面：https://thunderstore.io/c/dyson-sphere-program/p/$Namespace/SmartWarningIcons/"
} else {
    Write-Host "发布失败（退出码 $code）。常见原因：" -ForegroundColor Red
    Write-Host "  · 团队名不对 —— 用 -Namespace <团队名> 指定"
    Write-Host "  · 该版本号已存在 —— Thunderstore 版本号不可复用，需提升 thunderstore.toml 的 versionNumber"
    Write-Host "  · token 无效或已撤销 —— 到 Team → Service Accounts 重新生成"
    exit $code
}
