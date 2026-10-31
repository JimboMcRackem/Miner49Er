# Boots a real match in a visible window, runs scripted steps, saves screenshots.
#   .\tools\playtest.ps1 -Out C:\shots -Steps "wait 3; shot start; hold move_right 1; shot moved"
# Steps: wait <s> | hold <action> <s> | tap <action> | press <action> | release <action>
#        shot <name> | burst <name> <count> <s> | timescale <factor>
# Actions are the names in game/InputBindings.cs (move_up, pickaxe, plant_explosive, ...).
# Options: -Options "mode=GrudgeMatch seed=7 floor=12 bots=3 time=120 lava flood pits caveins"
param(
	[Parameter(Mandatory)][string]$Out,
	[string]$Steps = "wait 3; shot start",
	[string]$Options = "",
	[int]$TimeoutSeconds = 120
)

$repo = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $Out | Out-Null

# Godot's user:// sits under APPDATA, so this keeps runs away from real settings, scores and saves.
# Kept short: shader cache paths are long and fail past MAX_PATH under a deep folder.
$sandbox = Join-Path $env:TEMP "m49pt"
New-Item -ItemType Directory -Force $sandbox | Out-Null
$realAppData = $env:APPDATA
$env:APPDATA = $sandbox

$godot = (Get-Content (Get-Command godot).Source | Select-String '"([^"]+\.exe)"').Matches[0].Groups[1].Value
$log = Join-Path $Out "playtest.log"
$userArgs = @("--out=$Out", "--steps=$Steps") + ($Options -split '\s+' | Where-Object { $_ } | ForEach-Object { "--$_" })

$quoted = (@("--path", $repo, "--audio-driver", "Dummy", "--log-file", $log, "res://game/dev/Harness.tscn", "--") + $userArgs) |
	ForEach-Object { '"' + $_ + '"' }
$proc = Start-Process $godot -ArgumentList $quoted -PassThru
$env:APPDATA = $realAppData
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
	$proc.Kill()
	Write-Output "TIMED OUT after $TimeoutSeconds s"
	exit 2
}

Get-Content $log | Select-String 'harness\]|ERROR|Unhandled' | ForEach-Object { $_.Line }
exit $proc.ExitCode
