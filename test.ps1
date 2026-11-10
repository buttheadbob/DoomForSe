<#
.SYNOPSIS
  Builds the plugin and starts Pulsar with it, in a private copy of Pulsar.

.DESCRIPTION
  1. Builds ClientPlugin (Release) so compile errors show up first.
  2. Makes a private copy of Pulsar under .test\Pulsar (the launcher and its libraries, not your plugins or profiles),
     so the Pulsar you already use is left alone.
  3. Registers this repository there as a development folder, which Pulsar compiles from source exactly as it does on a
     player's PC, including the manifest's assets (the images and the Freedoom download), and enables only this plugin.
  4. Optionally copies a Doom WAD into the plugin's storage folder (-Wad); without one the bundled Freedoom is used.
  5. Starts the private Pulsar, waits for it to exit, and stops anything it left running.

  Starting Pulsar opens the game window. Run this only when you are ready for that.

.EXAMPLE
  .\test.ps1
  .\test.ps1 -Wad "C:\Games\DOOM1.WAD"      # test with your own WAD instead of Freedoom
  .\test.ps1 -NoLaunch                       # build and prepare only
  .\test.ps1 -Refresh                        # recopy Pulsar from the source install
#>
[CmdletBinding()]
param(
    [string]$PulsarSource,
    [string]$Wad,
    [string]$Configuration = 'Release',
    [switch]$NoLaunch,
    [switch]$Refresh,
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

# 2. A private copy of Pulsar. Claude runs as a packaged app, so its Roaming folder is redirected.
if (-not $PulsarSource) {
    $packaged = Join-Path $env:LOCALAPPDATA 'Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Pulsar'
    $plain = Join-Path $env:APPDATA 'Pulsar'
    $PulsarSource = if (Test-Path (Join-Path $plain 'Legacy.exe')) { $plain } else { $packaged }
}
if (-not (Test-Path (Join-Path $PulsarSource 'Legacy.exe'))) {
    throw "Legacy.exe not found in $PulsarSource. Pass -PulsarSource <Pulsar folder>."
}

$pulsar = Join-Path $root '.test\Pulsar'
$data = Join-Path $pulsar 'Legacy'
if ($Refresh -and (Test-Path $pulsar)) { Remove-Item $pulsar -Recurse -Force }
if (-not (Test-Path (Join-Path $pulsar 'Legacy.exe'))) {
    Write-Host "Copying Pulsar to $pulsar"
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    Get-ChildItem $PulsarSource -File | Copy-Item -Destination $pulsar -Force
    Copy-Item (Join-Path $PulsarSource 'Libraries') $pulsar -Recurse -Force
    # The consent answer, so Pulsar does not ask on this first start.
    $config = Join-Path $PulsarSource 'Legacy\config.xml'
    if (Test-Path $config) { Copy-Item $config $data -Force }
}

# 3. The development folder, and a profile that enables only this plugin.
$pluginId = ([xml](Get-Content $manifestFile -Raw)).PluginData.Id
$folder = $root.TrimEnd('\')
$sourcesDir = Join-Path $data 'Sources'
New-Item -ItemType Directory -Force -Path $sourcesDir | Out-Null
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

$profileFile = Join-Path $root '.test\DoomTest.xml'
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
$proc = Start-Process -FilePath $exe -ArgumentList $PulsarArgs -WorkingDirectory $pulsar -PassThru
try {
    $proc.WaitForExit()
}
finally {
    Get-Process -Name 'SpaceEngineers', 'Legacy' -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host 'Pulsar and the game are closed.'
}

