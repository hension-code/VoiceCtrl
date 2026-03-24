Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\VoiceCtrl\VoiceCtrl.csproj"
$project = [System.IO.Path]::GetFullPath($project)

dotnet publish $project `
  -c Release `
  -r win-x64 `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false

Write-Host "Build complete."
