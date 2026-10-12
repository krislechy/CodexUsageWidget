param([string]$OutputDirectory = 'artifacts/verification', [switch]$UpdateScreenshots)
. (Join-Path $PSScriptRoot 'common.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
 Invoke-Checked 'dotnet' @('build','CodexUsageWidget.sln','-c','Release')
 Invoke-Checked 'dotnet' @('run','--project','tests/CodexUsageWidget.Tests','-c','Release','--no-build')
 $output = [IO.Path]::GetFullPath($OutputDirectory)
 Invoke-UiCapture (Join-Path $projectRoot 'src/CodexUsageWidget/bin/Release/net10.0-windows/CodexUsageWidget.exe') $output
 if ($UpdateScreenshots) {
  Copy-Item -Path (Join-Path $output '*.png') -Destination (Join-Path $projectRoot 'docs/screenshots') -Force
 }
 Write-Host 'Verification passed: core regressions and English native UI.'
} finally { Pop-Location }
