# Pulls the containers the game opens (local\data: daggerfall-data.rpak, music.rpak, first-person.rpak) into a Windows
# checkout; scripts/data-sync.sh says what they are. The default source is den-agents' shared checkout through the P:
# share (mapped on the console desktop only), which holds the data of main's importer revision; -Store names another
# checkout's local\data, or a store root that keeps revisions\REVISION. Either way it copies only data at this
# checkout's own importer revision, and says how to get it made when the source has none. Changed files are copied
# whole: Windows has no rsync. A Windows checkout only pulls; -Status only compares revisions.
#
#   powershell -File scripts\data-sync.ps1
#   powershell -File scripts\data-sync.ps1 -Status
#   powershell -File scripts\data-sync.ps1 -Store \\server\share\rusty-dagger\lan
param(
    [string]$Store = 'P:\dev\rusty-dagger\local\data',
    [switch]$Status
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $root 'local\data'

# The import records its revision, and whether it departs from one whole import, in the container's first entry,
# worldrpg/data-container.json: returns the revision (or $null) and whether it is whole.
function Describe([string]$path) {
    if (-not (Test-Path $path)) { return $null, $false }
    $stream = [System.IO.File]::OpenRead($path)
    try { $head = New-Object byte[] 8192; $read = $stream.Read($head, 0, $head.Length) } finally { $stream.Close() }
    $text = [System.Text.Encoding]::UTF8.GetString($head, 0, $read)
    $match = [regex]::Match($text, '"importerRevision": *"([0-9a-f]{64})"')
    $revision = if ($match.Success) { $match.Groups[1].Value } else { $null }
    return $revision, ($text -match '"departures": *\[\]')
}

# The same target the build stamps the revision with; no restore or compile, about a second.
$checkout = 'unknown (dotnet is not on PATH)'
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $project = Join-Path $root 'src\Daggerfall.Import\Daggerfall.Import.csproj'
    $value = (& dotnet msbuild $project -nologo -t:StampImporterRevision -getProperty:_ImporterRevision 2>&1 | Out-String).Trim()
    $checkout = if ($value -match '^[0-9a-f]{64}$') { $value } else { 'unknown (msbuild could not compute it)' }
}
$missing = "The source $Store has no data for importer revision $checkout. Import it with " +
    "scripts/route.py data $((& git -C $root rev-parse HEAD 2>$null)) from a den-agents worktree; once main's commit has it, " +
    "den-agents' shared checkout pulls it with scripts/data-sync.sh pull."

function Report {
    $container, $whole = Describe (Join-Path $data 'daggerfall-data.rpak')
    if (-not $container) { $container = 'none (no readable local\data\daggerfall-data.rpak)' }
    Write-Output "importer revision of the container: $container"
    Write-Output "importer revision of this checkout: $checkout"
    if ($container -notmatch '^[0-9a-f]{64}$' -or $checkout -notmatch '^[0-9a-f]{64}$') { Write-Output 'Cannot compare them.' }
    elseif ($container -ne $checkout) { Write-Output "They differ: the game will call this data stale. Pull again." }
    if ($container -match '^[0-9a-f]{64}$' -and -not $whole) {
        Write-Output 'The container is not one whole import (an import --only or pack repack), so the game calls it stale.'
    }
}

if ($Status) { Report; exit 0 }
if (-not (Test-Path $Store)) { throw "data-sync: $Store is not reachable" }
if ($checkout -notmatch '^[0-9a-f]{64}$') { throw "data-sync: cannot tell which data this checkout needs: $checkout" }
# A store root keeps each revision apart and first-person.rpak once; a checkout's local\data holds one revision.
$source = $Store
if (Test-Path (Join-Path $Store 'revisions')) { $source = Join-Path $Store "revisions\$checkout" }
$revision, $whole = Describe (Join-Path $source 'daggerfall-data.rpak')
if ($revision -ne $checkout -or -not $whole) { Write-Output $missing; exit 1 }
robocopy $source $data daggerfall-data.rpak music.rpak /FFT /NP /NFL /NDL /R:2 /W:2
if ($LASTEXITCODE -ge 8) { throw "data-sync: robocopy failed ($LASTEXITCODE)" }
# first-person.rpak is not an import output: it sits beside the containers in a checkout and at a store's root.
if (Test-Path (Join-Path $Store 'first-person.rpak')) {
    robocopy $Store $data first-person.rpak /FFT /NP /NFL /NDL /R:2 /W:2
    if ($LASTEXITCODE -ge 8) { throw "data-sync: robocopy failed ($LASTEXITCODE)" }
}
Report
exit 0
