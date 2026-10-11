# Day 28: make one Short from the command line.
#   generate.cmd "Why Your Pull-Ups Aren't Increasing"
#   generate.cmd "Rest days" -ScriptFile my-script.txt     use your own narration (65-140 words), or a
#                                                          storyboard script (FORMAT: ..., docs/CHATGPT_SCRIPT_PROMPT.md)
#   generate.cmd "Rest days" -NoOpen                       don't open the folder at the end
# If the video needs pictures the catalog doesn't have, it stops and prints a ready prompt for
# each; add the images (dashboard upload, or assets/incoming/) and run continue.cmd <job-id>.
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
# [IO.File], not Get-Content -Raw: in Windows PowerShell 5.1 that string carries PSPath etc. and
# ConvertTo-Json writes it as an object ({"value": ...}) - the API rejected every -ScriptFile.
if ($ScriptFile) { $body.sourceScript = [IO.File]::ReadAllText((Resolve-Path $ScriptFile), [Text.Encoding]::UTF8) }
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
. "$PSScriptRoot\jobwait.ps1"
exit (Complete-VideoJob -JobId $job.id -NoOpen:$NoOpen)
