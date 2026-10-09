# Day 28: make one Short from the command line.
#   generate.cmd "Why Your Pull-Ups Aren't Increasing"
#   generate.cmd "Rest days" -ScriptFile my-script.txt     use your own narration (65-140 words)
#   generate.cmd "Rest days" -NoOpen                       don't open the folder at the end
# Starts the API in its own minimized window if it isn't running yet (it keeps running for
# the dashboard; close that window to stop it). Prints progress, then where the video is and
# the review checklist. Nothing is uploaded - publishing stays your decision.
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Topic,
    [string]$ScriptFile,
    [switch]$NoOpen,
    [switch]$Yes
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\prereqs.ps1"

if (-not (Test-ApiUp)) {
    if (-not (Test-Prerequisites -Fix -Yes:$Yes)) {
        Write-Host "Fix the items above, then run generate.cmd again." -ForegroundColor Red
        exit 1
    }

    Write-Host "Starting the API in a minimized window ..."
    Start-Process powershell -WindowStyle Minimized -ArgumentList @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "start.ps1"), "-SkipChecks", "-NoBrowser")
    for ($i = 0; $i -lt 120 -and -not (Test-ApiUp); $i++) { Start-Sleep 1 }
    if (-not (Test-ApiUp)) { Write-Host "The API did not start; look at its window for the error." -ForegroundColor Red; exit 1 }
}

$body = @{ topic = $Topic }
if ($ScriptFile) { $body.sourceScript = Get-Content -Raw -Encoding UTF8 $ScriptFile }
try {
    $job = Invoke-RestMethod -Method Post "$script:ApiUrl/api/videos" -ContentType "application/json; charset=utf-8" `
        -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json)))
} catch {
    # 400s carry {"error": "..."} - e.g. a script that's too short.
    $detail = $_.ErrorDetails.Message
    Write-Host "Not started: $(if ($detail) { ($detail | ConvertFrom-Json).error } else { $_.Exception.Message })" -ForegroundColor Red
    exit 1
}

Write-Host "Job $($job.id): '$Topic'"
$sw = [Diagnostics.Stopwatch]::StartNew()
$last = ""
while ($true) {
    Start-Sleep -Milliseconds 700
    $j = Invoke-RestMethod "$script:ApiUrl/api/videos/$($job.id)"
    if ($j.status -ne $last) {
        Write-Host ("  {0,5:n0}s  {1}" -f $sw.Elapsed.TotalSeconds, $j.status)
        $last = $j.status
    }
    if ($j.status -in "Completed", "Failed" -and -not $j.isRunning -and -not $j.isQueued) { break }
}

$folder = Join-Path $script:Repo "output\$($job.id)"
if ($j.status -eq "Failed") {
    Write-Host ""
    Write-Host "Failed: $($j.errorMessage)" -ForegroundColor Red
    Write-Host "Details: $folder\job.log"
    Write-Host "After fixing the cause, press Retry in the dashboard ($script:ApiUrl) - finished stages are kept."
    exit 1
}

Write-Host ""
Write-Host ("Done in {0:n0}s: {1}" -f $sw.Elapsed.TotalSeconds, $j.finalVideoPath) -ForegroundColor Green
Write-Host "Before publishing, go through the checklist (tick it in the dashboard: $script:ApiUrl):"
$review = Get-Content -Encoding UTF8 (Join-Path $folder "review.md") -ErrorAction SilentlyContinue
$review | Where-Object { $_.StartsWith("- [ ]") } | ForEach-Object { Write-Host "  $_" }
if (-not $NoOpen) { Start-Process explorer.exe -ArgumentList $folder }
