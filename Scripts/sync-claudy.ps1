#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pulls the data Claudio shares with Claudy: Design/ and Fixtures/, nothing else.

.DESCRIPTION
    Claudy (macOS) is the reference for Claudio's look and for how Anthropic's answers are read.
    This script fetches only its Design/ and Fixtures/ folders at one tag or commit, through a
    partial, sparse clone: no Swift source is ever downloaded. The commit synced is written to
    claudy/REF.

.EXAMPLE
    ./Scripts/sync-claudy.ps1                 # the latest Claudy release
    ./Scripts/sync-claudy.ps1 -Ref v1.6.0     # a given tag
    ./Scripts/sync-claudy.ps1 -Ref develop    # the moving branch
#>
param(
    [string]$Ref
)

$ErrorActionPreference = 'Stop'
$repository = 'https://github.com/Endikk/Claudy'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'claudy'

if (-not $Ref) {
    $latest = Invoke-RestMethod -Uri 'https://api.github.com/repos/Endikk/Claudy/releases/latest' `
                                -Headers @{ 'User-Agent' = 'Claudio' }
    $Ref = $latest.tag_name
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("claudy-" + [guid]::NewGuid())
try {
    git clone --quiet --filter=blob:none --no-checkout --depth 1 --branch $Ref $repository $work
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch Claudy at $Ref" }
    git -C $work sparse-checkout set --no-cone /Design/ /Fixtures/
    git -C $work checkout --quiet
    if ($LASTEXITCODE -ne 0) { throw "Could not check out Design/ and Fixtures/" }

    foreach ($folder in 'Design', 'Fixtures') {
        $destination = Join-Path $target $folder
        if (Test-Path $destination) { Remove-Item -Recurse -Force $destination }
        Copy-Item -Recurse (Join-Path $work $folder) $destination
    }
    $commit = (git -C $work rev-parse HEAD).Trim()
    Set-Content -Path (Join-Path $target 'REF') -Value $commit -NoNewline:$false
    Write-Host "claudy/ now holds Design/ and Fixtures/ from Claudy $Ref ($commit)"
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}
