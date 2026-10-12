param([string]$OutputDirectory = 'artifacts/release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$portable = Join-Path $outputRoot 'CodexUsageWidget-win-x64'
New-Item -ItemType Directory -Path $portable -Force | Out-Null
dotnet publish (Join-Path $projectRoot 'src/CodexUsageWidget/CodexUsageWidget.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $portable
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $portable
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portable
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $portable -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'DESIGN.md') -Destination $portable
$archive = Join-Path $outputRoot 'CodexUsageWidget-win-x64.zip'
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $archive -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  CodexUsageWidget-win-x64.zip" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Output $archive
