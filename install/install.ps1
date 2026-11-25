# Installs CalmTerraria into tModLoader for the person running it.
# Run install.cmd, which runs this. tModLoader must be closed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$mod = Join-Path $here 'CalmTerraria.tmod'
if (-not (Test-Path $mod)) { Write-Host 'CalmTerraria.tmod is not beside this script. Nothing was changed.'; exit 1 }

$running = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like '*tModLoader.dll*' }
if ($running) { Write-Host 'tModLoader is running. Close it and run this again. Nothing was changed.'; exit 1 }

$save = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Terraria\tModLoader'
$mods = Join-Path $save 'Mods'
if (-not (Test-Path $save)) { Write-Host "tModLoader has not been run on this computer yet: $save does not exist. Start tModLoader once, close it, and run this again."; exit 1 }
New-Item -ItemType Directory -Force $mods | Out-Null
Copy-Item $mod (Join-Path $mods 'CalmTerraria.tmod') -Force

# Switch it on, and the older accessibility mod off: the two fight over the keyboard.
$enabledPath = Join-Path $mods 'enabled.json'
$enabled = @()
if (Test-Path $enabledPath) { $enabled = @(Get-Content $enabledPath -Raw | ConvertFrom-Json) }
$had = $enabled -contains 'TerrariaAccess'
$enabled = @($enabled | Where-Object { $_ -and $_ -ne 'TerrariaAccess' -and $_ -ne 'CalmTerraria' }) + 'CalmTerraria'
$json = '[' + (($enabled | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ']'
[IO.File]::WriteAllText($enabledPath, $json)

Write-Host 'CalmTerraria is installed and switched on.'
if ($had) { Write-Host 'Terraria Access was switched off; it is still installed, and can be switched back on by removing CalmTerraria.' }
Write-Host 'Start tModLoader with your screen reader running. The title menu should speak.'
