# Day 27: how many scene-plan attempts real topics need, and how long planning takes.
# Script + plan only (no narration/render), through the running API's manual endpoints.
#   powershell -ExecutionPolicy Bypass -File tools/perf/measure-planning.ps1 -ApiLog <path to the API's stdout log> -Topics "A","B"
param(
    [Parameter(Mandatory = $true)][string]$ApiLog,
    [Parameter(Mandatory = $true)][string[]]$Topics,
    [string]$Api = "http://localhost:5007"
)
$ErrorActionPreference = "Stop"
$results = foreach ($topic in $Topics) {
    $job = Invoke-RestMethod -Method Post "$Api/api/videos" -ContentType application/json `
        -Body (@{ topic = $topic; autoGenerate = $false } | ConvertTo-Json)
    Invoke-RestMethod -Method Post "$Api/api/videos/$($job.id)/script" | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Invoke-RestMethod -Method Post "$Api/api/videos/$($job.id)/plan" | Out-Null
    $seconds = $sw.Elapsed.TotalSeconds
    $accepted = Select-String -Path $ApiLog -Pattern "Job $($job.id): scene plan accepted on attempt (\d+)" | Select-Object -Last 1
    $attempts = if ($accepted) { [int]$accepted.Matches[0].Groups[1].Value } else { -1 }
    $rejections = @(Select-String -Path $ApiLog -Pattern "Job $($job.id): scene plan attempt \d+/\d+ rejected: (.*)$" |
        ForEach-Object { $_.Matches[0].Groups[1].Value.Split(" ")[0..3] -join " " })
    [pscustomobject]@{ Topic = $topic; Attempts = $attempts; Seconds = [math]::Round($seconds, 1); Rejected = ($rejections -join " | ") }
}
$results | Format-Table -AutoSize -Wrap
"average attempts {0:n2}, average planning time {1:n1}s" -f ($results | Measure-Object Attempts -Average).Average, ($results | Measure-Object Seconds -Average).Average
