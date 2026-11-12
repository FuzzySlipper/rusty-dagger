# Pulls the containers the game opens (local\data: daggerfall-data.rpak, music.rpak, first-person.rpak) into a Windows
# checkout; scripts/data-sync.sh says what they are. The default source is den-agents' checkout through the P: share
# (mapped on the console desktop only); -Store names another, such as the NAS store's lan\ root. Changed files are
# copied whole: Windows has no rsync. Adds and updates, deletes nothing; -Mirror also deletes what the source lacks.
# den-agents is the one machine that imports, so a Windows checkout only pulls; -Status only compares revisions.
#
#   powershell -File scripts\data-sync.ps1
#   powershell -File scripts\data-sync.ps1 -Status
#   powershell -File scripts\data-sync.ps1 -Store \\server\share\rusty-dagger\lan
param(
    [string]$Store = 'P:\dev\rusty-dagger\local\data',
    [switch]$Mirror,
    [switch]$Status
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$data = Join-Path $root 'local\data'

function Report {
    # The import records its revision in the container's first entry, worldrpg/data-container.json.
    $container = 'none (no local\data\daggerfall-data.rpak)'
    $path = Join-Path $data 'daggerfall-data.rpak'
    if (Test-Path $path) {
        $stream = [System.IO.File]::OpenRead($path)
        try { $head = New-Object byte[] 8192; $read = $stream.Read($head, 0, $head.Length) } finally { $stream.Close() }
        $text = [System.Text.Encoding]::UTF8.GetString($head, 0, $read)
        $match = [regex]::Match($text, '"importerRevision": *"([0-9a-f]{64})"')
        if ($match.Success) { $container = $match.Groups[1].Value }
        # A whole import records no departures; a repack over an earlier one records one.
        $whole = $text -match '"departures": *\[\]'
    }
    # The same target the build stamps the revision with; no restore or compile, about a second.
    $checkout = 'unknown (dotnet is not on PATH)'
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $project = Join-Path $root 'src\Daggerfall.Import\Daggerfall.Import.csproj'
        $value = (& dotnet msbuild $project -nologo -t:StampImporterRevision -getProperty:_ImporterRevision 2>&1 | Out-String).Trim()
        $checkout = if ($value -match '^[0-9a-f]{64}$') { $value } else { 'unknown (msbuild could not compute it)' }
    }
    Write-Output "importer revision of the container: $container"
    Write-Output "importer revision of this checkout: $checkout"
    if ($container -notmatch '^[0-9a-f]{64}$' -or $checkout -notmatch '^[0-9a-f]{64}$') { Write-Output 'Cannot compare them.' }
    elseif ($container -ne $checkout) {
        Write-Output 'They differ: the game will call this data stale. Pull from den-agents once it has imported this revision; never import here.'
    }
    if ($container -match '^[0-9a-f]{64}$' -and -not $whole) {
        Write-Output 'The container is not one whole import (an import --only or pack repack), so the game calls it stale.'
    }
}

if ($Status) { Report; exit 0 }
if (-not (Test-Path $Store)) { throw "data-sync: $Store is not reachable" }
$mode = if ($Mirror) { '/MIR' } else { '/E' }
robocopy $Store $data $mode /FFT /NP /NFL /NDL /R:2 /W:2
if ($LASTEXITCODE -ge 8) { throw "data-sync: robocopy failed ($LASTEXITCODE)" }
Report
exit 0
