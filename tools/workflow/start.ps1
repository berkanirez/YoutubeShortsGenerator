# Day 28: one command from "PC just turned on" to the dashboard.
#   start.cmd                 (double-click, or from a terminal)
#   start.cmd -CheckOnly      only report what's missing
#   start.cmd -Yes            apply the offered fixes without asking
#   start.cmd -NoBrowser      don't open the dashboard
# The API runs in this window: its log is here, and closing the window (or Ctrl+C) stops it.
# Interrupted videos resume on the next start (Day 26).
param([switch]$CheckOnly, [switch]$Yes, [switch]$NoBrowser, [switch]$SkipChecks)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\prereqs.ps1"

if (-not $SkipChecks) {
    $ok = Test-Prerequisites -Fix:(-not $CheckOnly) -Yes:$Yes
    if ($CheckOnly) { if ($ok) { Write-Host "All set." -ForegroundColor Green; exit 0 } else { exit 1 } }
    if (-not $ok) {
        Write-Host "Fix the items above, then run start.cmd again." -ForegroundColor Red
        exit 1
    }
}

if (Test-ApiUp) {
    Write-Host "The API is already running at $script:ApiUrl." -ForegroundColor Green
    if (-not $NoBrowser) { Start-Process $script:ApiUrl }
    exit 0
}

if (-not $NoBrowser) {
    # Opens the dashboard as soon as the API answers (the build can take a few seconds).
    Start-Job -ArgumentList $script:ApiUrl -ScriptBlock {
        param($url)
        for ($i = 0; $i -lt 120; $i++) {
            try { Invoke-RestMethod "$url/api/health" -TimeoutSec 2 | Out-Null; Start-Process $url; return } catch { Start-Sleep 1 }
        }
    } | Out-Null
}

Write-Host ""
Write-Host "Starting the Shorts Generator at $script:ApiUrl  (Ctrl+C or close this window to stop)" -ForegroundColor Cyan
Write-Host "Videos land in output\<job-id>\final.mp4 - output\INDEX.md lists them by title."
Write-Host ""
& dotnet run --project (Join-Path $script:Repo "src\ShortsGenerator.Api") --launch-profile http
exit $LASTEXITCODE
