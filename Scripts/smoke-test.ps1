#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Starts the published Claudio for real, once per placement, and fails if it does not come up.

.DESCRIPTION
    Claudio 1.0.0-beta.1 shipped without its compiled XAML and closed at launch, which no unit test
    could see. This runs the published folder the way a user does, in a settings folder of its own
    (so it never touches the real one), with no Claude Code on the machine (so it shows its demo
    set), and for each placement checks that:
      - the process is still alive after a few seconds, and stays alive;
      - the window it should show is on screen with a real size, and the others are not;
      - nothing was written to its log as a failure (unhandled, fatal, start failed, render).
    Whatever it finds is printed, and the log is kept for the build to upload.

.EXAMPLE
    ./Scripts/smoke-test.ps1 -Folder publish
#>
param(
    [Parameter(Mandatory)] [string]$Folder,
    [int]$Seconds = 12,
    [string]$Report = 'smoke'
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path (Resolve-Path $Folder) 'Claudio.exe'
if (-not (Test-Path $exe)) { throw "$exe does not exist" }

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SmokeWindows {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    public struct R { public int L, T, Ri, B; }
    /// <summary>Width x height of each visible top-level window of the process.</summary>
    public static List<string> Visible(uint pid) {
        var found = new List<string>();
        EnumWindows((h, l) => {
            uint owner; GetWindowThreadProcessId(h, out owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            var name = new StringBuilder(64); GetClassName(h, name, 64);
            R r; GetWindowRect(h, out r);
            if (name.ToString().StartsWith("WinUI")) found.Add((r.Ri - r.L) + "x" + (r.B - r.T) + "@" + r.L + "," + r.T);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("claudio-smoke-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $root | Out-Null
$failures = New-Object System.Collections.Generic.List[string]
$bad = 'unhandled|fatal|start failed|render:|background refresh'

foreach ($placement in 'Widget', 'NotificationArea', 'Notch') {
    $profileDir = Join-Path $root $placement
    $data = Join-Path $profileDir 'AppData\Local'
    New-Item -ItemType Directory -Force (Join-Path $data 'Claudio') | Out-Null
    # No Claude Code here: an empty folder stands in for it, so the demo set is what shows.
    $claude = Join-Path $profileDir 'claude'
    New-Item -ItemType Directory -Force $claude | Out-Null
    $settings = @{ placement = $placement; configDir = $claude } | ConvertTo-Json
    Set-Content -Path (Join-Path $data 'Claudio\settings.json') -Value $settings -Encoding UTF8

    $info = New-Object System.Diagnostics.ProcessStartInfo $exe
    $info.UseShellExecute = $false
    $info.WorkingDirectory = Split-Path $exe
    $info.EnvironmentVariables['LOCALAPPDATA'] = $data
    $info.EnvironmentVariables['USERPROFILE'] = $profileDir
    $info.EnvironmentVariables.Remove('CLAUDE_CONFIG_DIR')
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $deadline = (Get-Date).AddSeconds($Seconds)
        $windows = @()
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
            if ($process.HasExited) { break }
            $windows = [SmokeWindows]::Visible([uint32]$process.Id)
        }
        $label = "[$placement]"
        if ($process.HasExited) {
            $failures.Add("$label Claudio exited with code $($process.ExitCode) before $Seconds s")
            continue
        }
        # Widget: the card, a few hundred pixels wide. Notification area: nothing until the icon is
        # clicked. Notch: the island's ears, a thin band, at the top of the screen.
        $sizes = @($windows | ForEach-Object { $dim = ($_ -split '@')[0] -split 'x'; [pscustomobject]@{ W = [int]$dim[0]; H = [int]$dim[1] } })
        switch ($placement) {
            'Widget' { if (-not ($sizes | Where-Object { $_.W -gt 200 -and $_.H -gt 100 })) { $failures.Add("$label no card on screen (windows: $($windows -join ' '))") } }
            'NotificationArea' { if ($sizes.Count -gt 0) { $failures.Add("$label a window is on screen: $($windows -join ' ')") } }
            'Notch' { if (-not ($sizes | Where-Object { $_.W -gt 60 -and $_.H -gt 10 -and $_.H -lt 120 })) { $failures.Add("$label no island on screen (windows: $($windows -join ' '))") } }
        }
        Write-Host "$label alive, windows: $(if ($windows) { $windows -join ' ' } else { 'none' })"
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.WaitForExit(5000) | Out-Null
    }

    $log = Join-Path $data 'Claudio\api.log'
    if (Test-Path $log) {
        New-Item -ItemType Directory -Force $Report | Out-Null
        Copy-Item $log (Join-Path $Report "$placement.log") -Force
        $hits = Select-String -Path $log -Pattern $bad
        foreach ($hit in $hits) { $failures.Add("[$placement] log: $($hit.Line)") }
    }
}

Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host 'Smoke test passed: Claudio starts and shows what each placement should.'
