<#
  把 Release 构建部署到现有 r2modman TEST 档。游戏运行时不覆盖文件。
  只替换本 MOD 的 DLL、调试符号和运行素材。备份放在 TEST 档根，不会被 BepInEx 加载。
#>
[CmdletBinding()]
param([ValidateRange(0, 7200)][int]$WaitSeconds = 120)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$profile = Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles\TEST'
$target = Join-Path $profile 'BepInEx\plugins\TTenYX-SmartWarningIcons'
$files = @{
    'SmartWarningIcons.dll' = (Join-Path $project 'bin\Release\netstandard2.1\SmartWarningIcons.dll')
    'assets\InsufficientInput.png' = (Join-Path $project 'assets\InsufficientInput.png')
    'assets\InsufficientInputSmall.png' = (Join-Path $project 'assets\InsufficientInputSmall.png')
    'assets\OutputPile.png' = (Join-Path $project 'assets\OutputPile.png')
    'assets\OutputPileSmall.png' = (Join-Path $project 'assets\OutputPileSmall.png')
}
$pdb = Join-Path $project 'bin\Release\netstandard2.1\SmartWarningIcons.pdb'
if (Test-Path -LiteralPath $pdb -PathType Leaf) { $files['SmartWarningIcons.pdb'] = $pdb }

try {
    if (-not (Test-Path -LiteralPath (Join-Path $profile 'BepInEx\core\BepInEx.Preloader.dll') -PathType Leaf)) {
        throw '没有找到现有 TEST 档及其 BepInEx Preloader。'
    }
    $expected = @{}
    foreach ($name in $files.Keys) { $expected[$name] = (Get-FileHash -LiteralPath $files[$name]).Hash }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
        if ($watch.Elapsed.TotalSeconds -ge $WaitSeconds) { throw 'DSPGAME 尚未退出，未部署。' }
        Start-Sleep -Seconds 1
    }

    $changed = @($files.Keys | Sort-Object | Where-Object {
        $path = Join-Path $target $_
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ne $expected[$_]
    })
    $existing = @($changed | Where-Object { Test-Path -LiteralPath (Join-Path $target $_) -PathType Leaf })
    if ($existing.Count -gt 0) {
        $backup = Join-Path $profile ('_backups-SmartWarningIcons\bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        foreach ($name in $existing) {
            $to = Join-Path $backup $name
            New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $target $name) -Destination $to
        }
        Write-Host "已备份：$backup"
    }
    if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { throw 'DSPGAME 在备份期间启动，未覆盖插件。' }
    foreach ($name in $changed) {
        $to = Join-Path $target $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath $files[$name] -Destination $to -Force
    }
    foreach ($name in $files.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -ne $expected[$name]) {
            throw "部署文件与构建不一致：$name"
        }
    }
    Write-Host "TEST 部署校验通过：$target"
    Write-Host "SmartWarningIcons.dll SHA256=$($expected['SmartWarningIcons.dll'])"
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
