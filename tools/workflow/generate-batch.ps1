# Day 29: makes several Shorts one after another (one video at a time, like the worker).
#   powershell -ExecutionPolicy Bypass -File tools/workflow/generate-batch.ps1 -Topics "A","B" [-OutFile ids.txt]
# Prints one summary line per video: seconds, status, plan attempts, scenes, length, job id.
param(
    [Parameter(Mandatory = $true)][string[]]$Topics,
    [string]$OutFile
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\prereqs.ps1"

$ids = @()
foreach ($topic in $Topics) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "generate.ps1") $topic -NoOpen -Yes 2>&1 | Out-String
    $id = [regex]::Match($out, "Job ([0-9a-f-]{36})").Groups[1].Value
    if (-not $id) { Write-Host "No job for '$topic':`n$out" -ForegroundColor Red; continue }
    $ids += $id
    $job = Invoke-RestMethod "$script:ApiUrl/api/videos/$id"
    $log = Join-Path $script:Repo "output\$id\job.log"
    $attempts = ([regex]::Match((Get-Content -Raw -Encoding UTF8 $log), "accepted on attempt (\d+) \((\d+) scenes")).Groups
    $secs = [regex]::Match($out, "Done in (\d+)s").Groups[1].Value
    $plan = try { Invoke-RestMethod "$script:ApiUrl/api/videos/$id/plan" } catch { $null }
    $length = if ($plan) { [math]::Round(($plan.scenes | Measure-Object durationSeconds -Sum).Sum, 1) } else { "?" }
    "{0,-42} {1,4}s  {2,-9} attempts={3} scenes={4} length={5}s  {6}" -f $topic, $secs, $job.status, $attempts[1].Value, $attempts[2].Value, $length, $id
}
if ($OutFile) { $ids | Out-File -Encoding ascii $OutFile }
