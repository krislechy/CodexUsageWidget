Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
 & $Command @Arguments
 if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE)." }
}

function Invoke-UiCapture([string]$Executable, [string]$Destination, [switch]$ContextOnly) {
 New-Item -ItemType Directory -Path $Destination -Force | Out-Null
 $captureArguments = @('--capture', ('"' + $Destination + '"'), '--language', 'en')
 if ($ContextOnly) { $captureArguments += '--context-only' }
 $process = Start-Process -FilePath $Executable -ArgumentList $captureArguments -WindowStyle Hidden -PassThru
 try {
  if (!$process.WaitForExit(30000)) { $process.Kill($true); throw 'English UI verification timed out.' }
  if ($process.ExitCode -ne 0) {
   $details = Join-Path $Destination 'capture-error.txt'
   if (Test-Path -LiteralPath $details) { Write-Host (Get-Content -LiteralPath $details -Raw) }
   throw "English UI verification failed (exit $($process.ExitCode))."
  }
 } finally { $process.Dispose() }
}

function Get-ProjectVersion([string]$Root) {
 [xml]$props = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
 $version = [string]$props.Project.PropertyGroup.Version
 if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Directory.Build.props must contain a numeric major.minor.patch version.' }
 return $version
}

function Invoke-UpdateHelperSmoke([string]$Executable, [string]$Destination) {
 $missingPlan = Join-Path $Destination ('missing-plan-' + [guid]::NewGuid().ToString('N') + '.json')
 $process = Start-Process -FilePath $Executable -ArgumentList @('--apply-update', ('"' + $missingPlan + '"')) -WindowStyle Hidden -PassThru
 try {
  if (!$process.WaitForExit(10000)) { $process.Kill($true); throw 'Updater helper verification timed out.' }
  if ($process.ExitCode -ne 1) { throw 'Updater helper did not safely reject a missing plan.' }
 } finally { $process.Dispose() }
}
