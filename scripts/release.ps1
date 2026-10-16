param(
 [switch]$Publish,
 [switch]$Commit,
 [switch]$Resume,
 [string]$Version,
 [string]$CommitMessage,
 [string]$Repository = 'krislechy/CodexUsageWidget',
 [string]$Remote = 'origin',
 [string]$OutputDirectory
)
. (Join-Path $PSScriptRoot 'common.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
 if ($Version) {
  if ($Resume -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw '-Version requires major.minor.patch and cannot be used with -Resume.' }
  $propsPath = Join-Path $projectRoot 'Directory.Build.props'
  $props = [IO.File]::ReadAllText($propsPath)
  $props = [regex]::Replace($props, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
  [IO.File]::WriteAllText($propsPath, $props, [Text.UTF8Encoding]::new($false))
 }
 $version = Get-ProjectVersion $projectRoot
 $tag = "v$version"
 if (!$OutputDirectory) { $OutputDirectory = "artifacts/local-release/$tag" }
 $output = [IO.Path]::GetFullPath($OutputDirectory)
 $notes = Join-Path $projectRoot "docs/releases/$tag.md"
 if (!(Test-Path -LiteralPath $notes)) {
  $changelog = Get-Content -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Raw
  $section = [regex]::Match($changelog, '(?ms)^## ' + [regex]::Escape($version) + '\b[^\r\n]*\r?\n(.*?)(?=^## |\z)')
  if (!$section.Success) { throw "Add an English CHANGELOG.md entry for $version first." }
  New-Item -ItemType Directory -Path (Split-Path -Parent $notes) -Force | Out-Null
  $body = "# Codex Usage Widget $tag`n`n" + $section.Groups[1].Value.Trim() + "`n`nPortable includes the runtime. Lite requires Microsoft .NET 10 Desktop Runtime (Windows x64). Both archives include SHA-256 checksums.`n"
  [IO.File]::WriteAllText($notes, $body, [Text.UTF8Encoding]::new($false))
 }
 # No GitHub command or remote read is issued without this explicit switch.
 if ($Publish) {
  Invoke-Checked 'gh' @('auth','status')
  $remoteUrl = (& git remote get-url $Remote).Trim()
  if ($LASTEXITCODE -ne 0 -or $remoteUrl -notmatch ('github\.com[:/]' + [regex]::Escape($Repository) + '(\.git)?$')) { throw 'The repository does not match the selected Git remote.' }
  $branch = (& git branch --show-current).Trim()
  if ($branch -ne 'main') { throw 'Publish from main; no force-push is performed.' }
 }
 $reportPath = Join-Path $output 'release-report.json'
 if ($Resume -and (!$Publish -or $Commit)) { throw '-Resume requires -Publish and cannot be combined with -Commit.' }
 if (!$Resume) {
  & (Join-Path $PSScriptRoot 'verify.ps1') -OutputDirectory (Join-Path $output 'ui') -UpdateScreenshots
  & (Join-Path $PSScriptRoot 'publish.ps1') -OutputDirectory $output -Package Both
 } else {
  if (!(Test-Path -LiteralPath $reportPath)) { throw 'No prepared release report; run preparation first.' }
  $prepared = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
  $currentHead = (& git rev-parse HEAD).Trim()
  if ($prepared.version -ne $version -or $prepared.commit -ne $currentHead) { throw 'Prepared release does not match the current version/commit.' }
 }
 $assets = @()
 $packages = @()
 foreach ($name in 'CodexUsageWidget-win-x64','CodexUsageWidget-win-x64-lite') {
  $folder = Join-Path $output $name
  $exe = Join-Path $folder 'CodexUsageWidget.exe'
  if (!$Resume) {
   Invoke-UiCapture $exe (Join-Path $output "smoke/$name")
   Invoke-UpdateHelperSmoke $exe (Join-Path $output "smoke/$name")
  }
  $zip = Join-Path $output ($name + '.zip')
  $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  $recorded = (Get-Content -LiteralPath ($zip + '.sha256') -Raw).Split(' ')[0]
  if ($hash -ne $recorded) { throw "Checksum mismatch: $name" }
  if ($Resume -and $hash -ne ($prepared.packages | Where-Object name -EQ $name).sha256) { throw 'Prepared archive changed since verification.' }
  $assets += $zip; $assets += ($zip + '.sha256')
  $packages += [ordered]@{ name=$name; executableBytes=(Get-Item -LiteralPath $exe).Length; archiveBytes=(Get-Item -LiteralPath $zip).Length; sha256=$hash }
 }
 Invoke-Checked 'git' @('diff','--check')
 if ($Commit) {
  Invoke-Checked 'git' @('add','--','AGENTS.md','CHANGELOG.md','Directory.Build.props','README.md','PRODUCT.md','DESIGN.md','.impeccable','docs','scripts','src','tests','.github')
  & git diff --cached --quiet
  if ($LASTEXITCODE -eq 1) {
   if (!$CommitMessage) { $CommitMessage = "Release $tag" }
   Invoke-Checked 'git' @('commit','-m',$CommitMessage)
  } elseif ($LASTEXITCODE -ne 0) { throw 'Could not inspect staged changes.' }
 }
 $head = (& git rev-parse HEAD).Trim()
 $report = [ordered]@{ version=$version; commit=$head; verifiedLanguage='en'; packages=$packages; published=$false }
 $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
 if ($Publish) {
  if (& git status --porcelain) { throw 'Working tree must be clean before publication. Use -Commit to commit prepared changes.' }
  $existingTag = & git tag --list $tag
  if ($existingTag) {
   $tagCommit = (& git rev-list -n 1 $tag).Trim()
   if ($tagCommit -ne $head) { throw "$tag already points to a different commit; bump the version. Tags are never moved." }
  } else { Invoke-Checked 'git' @('tag','-a',$tag,'-m',"Codex Usage Widget $tag") }
  Invoke-Checked 'git' @('push',$Remote,'HEAD',"refs/tags/$tag")
  & gh release view $tag --repo $Repository --json url *> $null
  if ($LASTEXITCODE -eq 0) {
   # Reruns verify immutable existing assets rather than overwrite a public release.
   $download = Join-Path $output 'published-check'
   New-Item -ItemType Directory -Path $download -Force | Out-Null
   Invoke-Checked 'gh' @('release','download',$tag,'--repo',$Repository,'--dir',$download,'--pattern','*.zip','--clobber')
   foreach ($asset in $assets | Where-Object { $_.EndsWith('.zip') }) {
    $published = Join-Path $download ([IO.Path]::GetFileName($asset))
    if (!(Test-Path -LiteralPath $published) -or (Get-FileHash $published).Hash -ne (Get-FileHash $asset).Hash) { throw 'Existing release differs; bump the version instead of overwriting it.' }
   }
  } else {
   Invoke-Checked 'gh' (@('release','create',$tag,'--repo',$Repository,'--verify-tag','--title',"Codex Usage Widget $tag",'--notes-file',$notes) + $assets)
  }
  $release = & gh release view $tag --repo $Repository --json url,assets | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw 'Release verification failed.' }
  foreach ($asset in $assets) { if ([IO.Path]::GetFileName($asset) -notin $release.assets.name) { throw 'A release asset is missing.' } }
  $report.published = $true; $report.releaseUrl = $release.url
  Write-Host "Published: $($release.url)"
 }
 $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
 Write-Host "Prepared both packages: $output"
} finally { Pop-Location }
