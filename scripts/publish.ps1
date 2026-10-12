param([string]$OutputDirectory = 'artifacts/release', [ValidateSet('Portable','Lite','Both')][string]$Package = 'Both')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory)) }
$packages = if ($Package -eq 'Both') { @('Portable','Lite') } else { @($Package) }
foreach ($variant in $packages) {
$name = if ($variant -eq 'Lite') { 'CodexUsageWidget-win-x64-lite' } else { 'CodexUsageWidget-win-x64' }
$portable = Join-Path $outputRoot $name
# Prevent stale framework/files or developer screenshots from entering a repeated package.
if (Test-Path -LiteralPath $portable) {
 $resolved = [IO.Path]::GetFullPath($portable)
 $expected = [IO.Path]::GetFullPath($outputRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
 if (!$resolved.StartsWith($expected, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -ne $name) { throw 'Unsafe package cleanup path.' }
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $portable -Force | Out-Null
$selfContained = if ($variant -eq 'Portable') { 'true' } else { 'false' }
dotnet publish (Join-Path $projectRoot 'src/CodexUsageWidget/CodexUsageWidget.csproj') -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$selfContained -p:DebugType=None -p:DebugSymbols=false -o $portable
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $portable
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portable
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $portable
$docs = Join-Path $portable 'docs'
New-Item -ItemType Directory -Path (Join-Path $docs 'screenshots') -Force | Out-Null
# Keep developer mockups and all-language QA captures out of the runtime package.
foreach ($file in 'architecture.md','privacy.md','troubleshooting.md','package-size.md','releasing.md') {
 Copy-Item -LiteralPath (Join-Path $projectRoot "docs/$file") -Destination $docs -Force
}
foreach ($file in 'main.png','compact.png') {
 Copy-Item -LiteralPath (Join-Path $projectRoot "docs/screenshots/$file") -Destination (Join-Path $docs 'screenshots') -Force
}
if ($variant -eq 'Lite') {
 'Requires Microsoft .NET 10 Desktop Runtime (Windows x64). https://dotnet.microsoft.com/en-us/download/dotnet/10.0' | Set-Content -LiteralPath (Join-Path $portable 'RUNTIME-REQUIRED.txt') -Encoding utf8
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'DESIGN.md') -Destination $portable
$archive = Join-Path $outputRoot ($name + '.zip')
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $archive -Force
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $name.zip" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Output $archive
}
