[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Label,
    [string]$GameDir = 'D:\Program Files (x86)\Steam\steamapps\common\Dyson Sphere Program',
    [string]$BepInExCore = (Join-Path $env:APPDATA 'r2modmanPlus-local\DysonSphereProgram\profiles\TEST\BepInEx\core'),
    [string]$ModDll,
    [switch]$UseTestProfile,
    [ValidateRange(30, 540)][int]$TimeoutSeconds = 160
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $ModDll) { $ModDll = Join-Path $project 'bin\Release\netstandard2.1\SmartWarningIcons.dll' }
$selfTestDll = Join-Path $project 'tests\bin\Release\netstandard2.1\SmartWarningIcons.SelfTest.dll'
$game = (Resolve-Path -LiteralPath $GameDir).ProviderPath
$core = (Resolve-Path -LiteralPath $BepInExCore).ProviderPath
$evidence = Join-Path $project "docs\logs\$Label"
$token = [Guid]::NewGuid().ToString('N')
$run = Join-Path ([IO.Path]::GetTempPath()) "SmartWarningIcons-$token"
$install = Join-Path $run 'game'
$documents = Join-Path $run 'documents'
$bep = Join-Path $run 'BepInEx'
if ($UseTestProfile) {
    $bep = Split-Path -Parent $core
    if ((Split-Path -Leaf (Split-Path -Parent $bep)) -ne 'TEST') { throw 'Only the existing TEST profile is allowed in this mode.' }
    $mod = Join-Path $bep 'plugins\TTenYX-SmartWarningIcons'
    $test = Join-Path $bep "plugins\_SmartWarningIcons.SelfTest-$token"
}
else {
    $mod = Join-Path $bep 'plugins\SmartWarningIcons'
    $test = Join-Path $bep 'plugins\SelfTest'
}
$playerLog = Join-Path $run 'Player.log'
$log = Join-Path $bep 'LogOutput.log'
$report = Join-Path $test 'selftest.log'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$junctions = New-Object 'System.Collections.Generic.List[string]'
$notes = New-Object 'System.Collections.Generic.List[string]'
$process = $null
$ownedRun = $false
$ownedEvidence = $false
$ownedTestInProfile = $false
$passed = $false
$baselineProfileErrors = @()

try {
    if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { throw 'DSPGAME is already running; no process was stopped.' }
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Start the Steam client before running this test.' }
    foreach ($path in @($ModDll, $selfTestDll, (Join-Path $core 'BepInEx.Preloader.dll'))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required input: $path" }
    }
    if (Test-Path -LiteralPath $evidence) { throw "Evidence for '$Label' already exists; choose a fresh label." }
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $ownedEvidence = $true
    New-Item -ItemType Directory -Path $run | Out-Null
    $ownedRun = $true
    New-Item -ItemType Directory -Path $install, $documents, (Join-Path $install 'Configs') -Force | Out-Null
    if ($UseTestProfile) {
        New-Item -ItemType Directory -Path $test | Out-Null
        $ownedTestInProfile = $true
        if (Test-Path -LiteralPath $log -PathType Leaf) {
            Copy-Item -LiteralPath $log -Destination (Join-Path $evidence 'PriorLogOutput.log')
            $baselineProfileErrors = @(Select-String -LiteralPath $log -Pattern '^\s*\[(Error|Fatal)\s*:' | ForEach-Object { $_.Line })
        }
        $notes.Add('Profile=existing r2modman TEST; test DLL is temporary; game documents remain isolated')
    }
    else {
        New-Item -ItemType Directory -Path (Join-Path $mod 'assets'), $test -Force | Out-Null
        $notes.Add('Profile=isolated BepInEx core and SmartWarningIcons only')
    }

    # Only immutable game content is junctioned. Options, saves and logs are private to this run.
    foreach ($name in @('DSPGAME.exe', 'UnityPlayer.dll', 'steam_appid.txt', 'winhttp.dll')) {
        Copy-Item -LiteralPath (Join-Path $game $name) -Destination $install
    }
    foreach ($name in @('DSPGAME_Data', 'MonoBleedingEdge', 'Locale', 'Updates', 'Icarus', 'Verta', 'Wiki')) {
        $source = Join-Path $game $name
        if (-not (Test-Path -LiteralPath $source -PathType Container)) { continue }
        $link = Join-Path $install $name
        New-Item -ItemType Junction -Path $link -Target $source | Out-Null
        $junctions.Add($link)
    }
    [IO.File]::WriteAllText((Join-Path $install 'Configs\path.txt'), $documents.Replace('\', '/') + '/', $utf8)
    $gameDocuments = Join-Path $documents 'Dyson Sphere Program'
    New-Item -ItemType Directory -Path $gameDocuments | Out-Null
    $seedOptions = @'
<Options>
  <CurrentVersion><VersionNumber>6</VersionNumber></CurrentVersion>
  <Video><ResolutionWidth>1280</ResolutionWidth><ResolutionHeight>720</ResolutionHeight><DisplayMode>3</DisplayMode><MaxFPS>60</MaxFPS></Video>
  <Audio><GameMainVolume>0</GameMainVolume></Audio>
  <Gameplay><BackgroundPause>0</BackgroundPause><AutosavingInterval>0</AutosavingInterval><MilkyWayActivated>0</MilkyWayActivated><DataUploadToTheMilkyWay>2</DataUploadToTheMilkyWay></Gameplay>
  <Miscellaneous><Language>2052</Language><OpeningCinematic>0</OpeningCinematic></Miscellaneous>
</Options>
'@
    [IO.File]::WriteAllText((Join-Path $gameDocuments 'options.xml'), $seedOptions, $utf8)
    if (-not $UseTestProfile) {
        Copy-Item -LiteralPath $core -Destination (Join-Path $bep 'core') -Recurse
        Copy-Item -LiteralPath $ModDll -Destination (Join-Path $mod 'SmartWarningIcons.dll')
    }
    Copy-Item -LiteralPath $selfTestDll -Destination $test
    foreach ($name in @('InsufficientInput.png', 'InsufficientInputSmall.png', 'OutputPile.png', 'OutputPileSmall.png')) {
        $source = Join-Path $project "assets\$name"
        $destination = Join-Path $mod "assets\$name"
        if (-not $UseTestProfile) { Copy-Item -LiteralPath $source -Destination $destination }
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Deployed asset differs: $name" }
    }
    $originalHash = (Get-FileHash -LiteralPath $ModDll).Hash
    if ($originalHash -ne (Get-FileHash -LiteralPath (Join-Path $mod 'SmartWarningIcons.dll')).Hash) { throw 'DLL copy verification failed.' }
    $notes.Add("MOD SHA256=$originalHash; deployed copy identical; four assets identical")
    $notes.Add("Game Assembly-CSharp SHA256=$((Get-FileHash -LiteralPath (Join-Path $game 'DSPGAME_Data\Managed\Assembly-CSharp.dll')).Hash)")
    $notes.Add("Sandbox=$run")

    if (Get-Process DSPGAME -ErrorAction SilentlyContinue) { throw 'DSPGAME started during staging; this run did not launch or stop it.' }
    $preloader = Join-Path $bep 'core\BepInEx.Preloader.dll'
    $launch = Join-Path $install 'DSPGAME.exe'
    $old = $env:SWI_SELFTEST_DOCUMENTS
    $oldMode = $env:SWI_SELFTEST_SMOKE_ONLY
    try {
        $env:SWI_SELFTEST_DOCUMENTS = $documents
        $env:SWI_SELFTEST_SMOKE_ONLY = if ($UseTestProfile) { '1' } else { '0' }
        $arguments = "--doorstop-enable true --doorstop-target `"$preloader`" --doorstop-dll-search-override `"$(Split-Path -Parent $preloader)`" -logFile `"$playerLog`" -screen-fullscreen 0 -screen-width 1280 -screen-height 720"
        $process = Start-Process -FilePath $launch -WorkingDirectory $install -ArgumentList $arguments -PassThru
    }
    finally {
        $env:SWI_SELFTEST_DOCUMENTS = $old
        $env:SWI_SELFTEST_SMOKE_ONLY = $oldMode
    }
    $notes.Add("START pid=$($process.Id) time=$([DateTimeOffset]::Now.ToString('o'))")
    Write-Host $notes[$notes.Count - 1]
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Timeout after $TimeoutSeconds seconds." }
    $notes.Add("EXIT pid=$($process.Id) code=$($process.ExitCode) time=$([DateTimeOffset]::Now.ToString('o'))")
    if (-not (Test-Path -LiteralPath $report)) { throw 'The game exited without a self-test report.' }
    if (-not (Test-Path -LiteralPath $log)) { throw 'The isolated BepInEx log was not created.' }
    $result = [IO.File]::ReadAllText($report)
    $result.TrimEnd() | Write-Host
    if ($process.ExitCode -ne 0 -or $result -notmatch '(?m)^SUMMARY PASS=[1-9][0-9]* FAIL=0\r?$') { throw 'Game self-test failed.' }
    $runtimeErrors = @(Select-String -LiteralPath $log -Pattern '^\s*\[(Error|Fatal)\s*:' | ForEach-Object { $_.Line })
    foreach ($line in $runtimeErrors) {
        if ($UseTestProfile -and $baselineProfileErrors -contains $line -and $line -match 'Could not load \[BuildToolOptCAPIcompat 1\.0\.0\].*org\.kremnev8\.plugin\.BlueprintTweaks') {
            $notes.Add("Known pre-existing third-party error: $line")
        }
        else { throw "New or unclassified BepInEx error: $line" }
    }
    $playerErrors = @(Select-String -LiteralPath $playerLog -Pattern '^\s*[\w.]+Exception:|^Releasing render texture that is set to be RenderTexture\.active!' | ForEach-Object { $_.Line })
    foreach ($line in $playerErrors) {
        if ($UseTestProfile -and $line -match '^HarmonyLib\.HarmonyException: .*FreeCamera\.FreeCamera\+PlayerAction_Build_PrepareBuild_Patch') {
            $notes.Add('Known pre-existing Free Camera Harmony patch error in Player.log.')
        }
        else { throw "New or unclassified Unity Player.log error: $line" }
    }
    $passed = $true
}
catch {
    $notes.Add("FAIL $($_.Exception.Message)")
    Write-Host $_.Exception.Message
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        try {
            if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(12000)) {
                $process.Kill()
                if (-not $process.WaitForExit(10000)) { throw 'Owned game PID is still running.' }
                $notes.Add("CLEANUP killed owned PID $($process.Id)")
            } else { $notes.Add("CLEANUP closed owned PID $($process.Id)") }
        }
        catch {
            $passed = $false
            $notes.Add("CLEANUP failure: $($_.Exception.Message)")
        }
    }
    if ($ownedEvidence) {
        try {
            foreach ($source in @($report, $log, $playerLog)) {
                if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination $evidence }
            }
            if (Test-Path -LiteralPath $test) {
                Get-ChildItem -LiteralPath $test -Filter '*.png' -File | Copy-Item -Destination $evidence
            }
        }
        catch {
            $passed = $false
            $notes.Add("Evidence copy failed: $($_.Exception.Message)")
        }
    }
    $closed = $null -eq $process -or $process.HasExited
    if ($ownedTestInProfile -and $closed) {
        try {
            $resolvedTest = (Resolve-Path -LiteralPath $test).ProviderPath
            if ($resolvedTest -ne $test -or (Split-Path -Leaf $resolvedTest) -ne "_SmartWarningIcons.SelfTest-$token") { throw 'Unexpected TEST cleanup path.' }
            Remove-Item -LiteralPath $resolvedTest -Recurse -Force
            $notes.Add('Temporary self-test plugin removed from TEST.')
        }
        catch {
            $passed = $false
            $notes.Add("TEST cleanup failed: $($_.Exception.Message)")
        }
    }
    if ($ownedRun -and $closed) {
        try {
            # Unlink only the recorded junctions, non-recursively, before deleting our private tree.
            foreach ($link in $junctions) { [IO.Directory]::Delete($link, $false) }
            $resolved = (Resolve-Path -LiteralPath $run).ProviderPath
            if ($resolved -ne $run -or (Split-Path -Leaf $resolved) -ne "SmartWarningIcons-$token") { throw 'Unexpected cleanup path.' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
            $notes.Add('Sandbox removed; original installation path and original saves were not targets of this run.')
        }
        catch {
            $passed = $false
            $notes.Add("Sandbox cleanup failed: $($_.Exception.Message)")
        }
    }
    $notes.Add("RESULT $(if ($passed) { 'PASS' } else { 'FAIL' })")
    if ($ownedEvidence) { [IO.File]::WriteAllLines((Join-Path $evidence 'run.txt'), $notes, $utf8) }
    $notes | Write-Host
}
if (-not $passed) { exit 1 }
