<#
.SYNOPSIS
  Builds the plugin and starts Pulsar with it, in a private copy of Pulsar.

.DESCRIPTION
  1. Builds ClientPlugin (Release) so compile errors show up first.
  2. Makes a private copy of Pulsar under .test\Pulsar (the launcher and its libraries, not your plugins or profiles),
     so the Pulsar you already use is left alone.
  3. Registers this repository there as a development folder, which Pulsar compiles from source exactly as it does on a
     player's PC, including the manifest's assets, and enables only this plugin.
     With -HubCommit, it installs the plugin the way the hub does instead: Pulsar downloads that commit from GitHub
     (it must be pushed) through a local hub holding a copy of the manifest, so assets arrive as on a player's PC.
  4. Optionally copies a Doom WAD into the plugin's storage folder (-Wad); without one the bundled Freedoom is used.
  5. Starts the private Pulsar, waits for it to exit, and stops what it started (by process id; a game you run
     yourself is never touched).

  Starting Pulsar opens the game window. Run this only when you are ready for that.

.EXAMPLE
  .\test.ps1
  .\test.ps1 -Wad "C:\Games\DOOM1.WAD"      # test with your own WAD instead of Freedoom
  .\test.ps1 -NoLaunch                       # build and prepare only
  .\test.ps1 -Refresh                        # recopy Pulsar from the source install
  .\test.ps1 -HubCommit <40-character hash>  # hub-style install of a pushed commit
#>
[CmdletBinding()]
param(
    [string]$PulsarSource,
    [string]$Wad,
    [string]$Configuration = 'Release',
    [switch]$NoLaunch,
    [switch]$Refresh,
    [string]$HubCommit,
    [string[]]$PulsarArgs = @('-noSplash', '-sources')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'ClientPlugin\ClientPlugin.csproj'
$manifestFile = Join-Path $root 'DoomForSe.xml'

# 1. Build, to catch compile errors early.
Write-Host "Building net48 ($Configuration)..."
& dotnet build $project -c $Configuration -f net48 --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# 2. A private copy of Pulsar.
if (-not $PulsarSource) {
    $PulsarSource = Join-Path $env:APPDATA 'Pulsar'
}
$pulsar = Join-Path $root '.test\Pulsar'
$data = Join-Path $pulsar 'Legacy'
if ($Refresh -and (Test-Path $pulsar)) { Remove-Item $pulsar -Recurse -Force }
if (-not (Test-Path (Join-Path $pulsar 'Legacy.exe'))) {
    if (-not (Test-Path (Join-Path $PulsarSource 'Legacy.exe'))) {
        throw "Legacy.exe not found in $PulsarSource. Pass -PulsarSource <Pulsar folder>."
    }
    Write-Host "Copying Pulsar to $pulsar"
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    Get-ChildItem $PulsarSource -File | Copy-Item -Destination $pulsar -Force
    Copy-Item (Join-Path $PulsarSource 'Libraries') $pulsar -Recurse -Force
    # The consent answer, so Pulsar does not ask on this first start.
    $config = Join-Path $PulsarSource 'Legacy\config.xml'
    if (Test-Path $config) { Copy-Item $config $data -Force }
}

# 3. Where Pulsar gets the plugin, and a profile that enables only this plugin.
$pluginId = ([xml](Get-Content $manifestFile -Raw)).PluginData.Id
$folder = $root.TrimEnd('\')
$sourcesDir = Join-Path $data 'Sources'
New-Item -ItemType Directory -Force -Path $sourcesDir | Out-Null
$profileFile = Join-Path $root '.test\DoomTest.xml'

if ($HubCommit) {
    if ($HubCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "-HubCommit needs a full 40-character commit hash." }

    # A local hub with a copy of the manifest pinned to the commit: Pulsar downloads it from GitHub as for a player.
    $hub = Join-Path $root '.test\Hub'
    New-Item -ItemType Directory -Force -Path $hub | Out-Null
    $manifest = [xml](Get-Content $manifestFile -Raw)
    $manifest.PluginData.Commit = $HubCommit
    $manifest.Save((Join-Path $hub 'DoomForSe.xml'))
@"
<?xml version="1.0"?>
<SourcesConfig>
  <ShowWarning>false</ShowWarning>
  <LocalHubSources>
    <LocalHub>
      <Name>DoomForSe test hub</Name>
      <Folder>$hub</Folder>
      <Enabled>true</Enabled>
    </LocalHub>
  </LocalHubSources>
</SourcesConfig>
"@ | Set-Content -Path (Join-Path $sourcesDir 'sources.xml') -Encoding UTF8
@"
<?xml version="1.0"?>
<Profile>
  <Name>DoomTest</Name>
  <GitHub>
    <GitHubPluginConfig>
      <Id>$pluginId</Id>
    </GitHubPluginConfig>
  </GitHub>
  <DevFolder />
  <Local />
  <Mods />
</Profile>
"@ | Set-Content -Path $profileFile -Encoding UTF8
}
else {
@"
<?xml version="1.0"?>
<SourcesConfig>
  <ShowWarning>false</ShowWarning>
  <LocalPluginSources>
    <LocalPlugin>
      <Name>DoomForSe</Name>
      <Folder>$folder</Folder>
      <File>DoomForSe.xml</File>
      <Enabled>true</Enabled>
    </LocalPlugin>
  </LocalPluginSources>
</SourcesConfig>
"@ | Set-Content -Path (Join-Path $sourcesDir 'sources.xml') -Encoding UTF8
$debug = if ($Configuration -eq 'Debug') { 'true' } else { 'false' }
@"
<?xml version="1.0"?>
<Profile>
  <Name>DoomTest</Name>
  <GitHub />
  <DevFolder>
    <LocalFolderConfig>
      <Id>$pluginId</Id>
      <DebugBuild>$debug</DebugBuild>
    </LocalFolderConfig>
  </DevFolder>
  <Local />
  <Mods />
</Profile>
"@ | Set-Content -Path $profileFile -Encoding UTF8
}
$PulsarArgs = @($PulsarArgs) + @('-profile', $profileFile)

# 4. Optional WAD of your own.
if ($Wad) {
    if (-not (Test-Path $Wad)) { throw "WAD not found: $Wad" }
    $storage = Join-Path $env:APPDATA 'SpaceEngineers\Storage\DoomForSe'
    New-Item -ItemType Directory -Force -Path $storage | Out-Null
    Copy-Item $Wad $storage -Force
    Write-Host "Copied $(Split-Path $Wad -Leaf) to $storage"
}

if ($NoLaunch) { Write-Host 'Done (-NoLaunch).'; return }

# 5. Launch, wait, and clean up.
$exe = Join-Path $pulsar 'Legacy.exe'
Write-Host "Starting $exe $($PulsarArgs -join ' ')"
# Pulsar quits at once while another Space Engineers runs, so say so instead of starting it.
if (Get-Process -Name 'SpaceEngineers', 'Legacy', 'Interim' -ErrorAction SilentlyContinue) {
    throw 'Space Engineers or Pulsar is already running. Close it first; this script never stops it for you.'
}

$started = Get-Date
$proc = Start-Process -FilePath $exe -ArgumentList $PulsarArgs -WorkingDirectory $pulsar -PassThru

# Stops the process this script started and the processes it started in turn, by process id, and nothing else:
# a game or Pulsar the player (or anything else) runs at the same time is left alone.
function Stop-StartedTree([int]$id) {
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$id" -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationDate -ge $started } |
            ForEach-Object { Stop-StartedTree ([int]$_.ProcessId) }
    }
    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
}

try {
    $proc.WaitForExit()
}
finally {
    Stop-StartedTree $proc.Id
    Write-Host 'The Pulsar this script started is closed.'
}

