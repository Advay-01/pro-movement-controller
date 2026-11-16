$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$references = @('System.Windows.Forms.dll','System.Drawing.dll',(Join-Path $framework 'System.Runtime.dll'),(Join-Path $framework 'System.Runtime.WindowsRuntime.dll'))
$metadata = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata\*\Windows.winmd" | Where-Object { $_.Directory.Name -match '^\d+\.' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $metadata) { throw 'Building requires the Windows 10/11 SDK UnionMetadata Windows.winmd.' }
$references += $metadata.FullName
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $PSScriptRoot 'MovementController.exe')))
$compilerArgs += $references | ForEach-Object { '/reference:' + $_ }
$compilerArgs += @((Join-Path $PSScriptRoot 'MovementController.cs'),(Join-Path $PSScriptRoot 'EncounterRecognition.cs'),(Join-Path $PSScriptRoot 'FocusRecovery.cs'),(Join-Path $PSScriptRoot 'RunAwayKey.cs'),(Join-Path $PSScriptRoot 'EncounterPacing.cs'),(Join-Path $PSScriptRoot 'RunButtonMatch.cs'))
$compilerArgs += (Join-Path $PSScriptRoot 'BattleVisual.cs')
$compilerArgs += (Join-Path $PSScriptRoot 'BattleControlReady.cs')
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'Built MovementController.exe'
