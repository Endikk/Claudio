#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Films the README's recording: a Debug Claudio plays it by itself (--film), this captures it.

.DESCRIPTION
    Claudio runs in a settings and profile folder of its own, with no Claude Code (its demo
    figures) and made-up sessions, agents and ports, and walks through its scenes on its own. Its
    windows are captured with PrintWindow, so they are filmed whatever lies over them, and nothing
    is clicked or typed on this PC. Each scene's frames land in <Out>/frames/<scene>/, named by
    the milliseconds since the scene began; Scripts/make-demo-video.py turns them into the GIFs.

.EXAMPLE
    dotnet build src/Claudio.App -p:Platform=x64
    ./Scripts/record-demo.ps1 -Out film
    python Scripts/make-demo-video.py film
#>
param(
    [string]$Out = 'film',
    [string]$Exe = 'src\Claudio.App\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\Claudio.exe',
    [int]$FrameMs = 66
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Resolve-Path (Join-Path $root $Exe)
$film = New-Item -ItemType Directory -Force -Path $Out
Remove-Item -Recurse -Force (Join-Path $film 'frames'), (Join-Path $film 'scene.txt') -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
public static class FilmCapture {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    struct R { public int L, T, Ri, B; }
    // The largest visible window of the process with this title.
    public static bool Capture(int pid, string title, string path) {
        IntPtr best = IntPtr.Zero; long area = 0; R rect = new R();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var text = new StringBuilder(256); GetWindowText(h, text, 256);
            if (text.ToString() != title) return true;
            R r; GetWindowRect(h, out r);
            long a = (long)(r.Ri - r.L) * (r.B - r.T);
            if (a > area && r.Ri - r.L > 20 && r.B - r.T > 20) { area = a; best = h; rect = r; }
            return true;
        }, IntPtr.Zero);
        if (best == IntPtr.Zero) return false;
        using (var bmp = new Bitmap(rect.Ri - rect.L, rect.B - rect.T)) {
            using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(best, dc, 2); g.ReleaseHdc(dc); }
            bmp.Save(path);
        }
        return true;
    }
}
'@
[FilmCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

# A settings and profile folder of Claudio's own: no Claude Code, so the demo figures.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("claudio-film-" + [guid]::NewGuid())
$data = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'data\Claudio')
$profileDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'profile')
$claude = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'no-claude')
@{ placement = 'Widget'; configDir = "$claude"; isMinimal = $true } | ConvertTo-Json |
    Set-Content -Path (Join-Path $data 'settings.json') -Encoding UTF8

$info = New-Object System.Diagnostics.ProcessStartInfo $exe
$info.Arguments = "--film `"$($film.FullName)`""
$info.UseShellExecute = $false
$info.WorkingDirectory = Split-Path $exe
$info.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $stage 'data'
$info.EnvironmentVariables['USERPROFILE'] = "$profileDir"
$process = [System.Diagnostics.Process]::Start($info)

$sceneFile = Join-Path $film 'scene.txt'
$scene = $null
$clock = [System.Diagnostics.Stopwatch]::new()
$frames = 0
try {
    while (-not $process.HasExited) {
        $tick = [System.Diagnostics.Stopwatch]::StartNew()
        if (Test-Path $sceneFile) {
            $lines = @(Get-Content $sceneFile -Encoding UTF8 -ErrorAction SilentlyContinue)
            if ($lines.Count -ge 1 -and $lines[0] -ne $scene) {
                $scene = $lines[0]
                $window = if ($lines.Count -ge 2) { $lines[1] } else { '' }
                if ($window -eq 'island') { $window = 'Claudio' }
                $clock.Restart()
                Write-Host "scene $scene ($window)"
                if ($scene -eq 'done') { break }
                $folder = New-Item -ItemType Directory -Force -Path (Join-Path $film "frames\$scene")
            }
        }
        if ($scene -and $scene -ne 'done' -and $window) {
            $path = Join-Path $folder ('{0:D6}.png' -f $clock.ElapsedMilliseconds)
            if ([FilmCapture]::Capture($process.Id, $window, $path)) { $frames++ }
        }
        $wait = $FrameMs - $tick.ElapsedMilliseconds
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    }
}
finally {
    if (-not $process.WaitForExit(5000)) { $process.Kill() }
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
Write-Host "$frames frames in $($film.FullName)\frames"
