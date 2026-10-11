# Continues a video that stopped for pictures (status WaitingForArt):
#   continue.cmd <job-id>                 imports the images from assets\incoming\ and goes on
#   continue.cmd <job-id> -SkipMissing    goes on even if some are still missing
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$JobId,
    [switch]$SkipMissing,
    [switch]$NoOpen
)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\prereqs.ps1"
. "$PSScriptRoot\jobwait.ps1"

if (-not (Test-ApiUp)) { Write-Host "The API is not running; start it with start.cmd." -ForegroundColor Red; exit 1 }

Write-Host "Importing the pictures (HD upscale, background removal) ..."
try {
    $result = Invoke-RestMethod -Method Post "$script:ApiUrl/api/videos/$JobId/art/continue?skipMissing=$($SkipMissing.IsPresent.ToString().ToLower())"
} catch {
    $detail = $_.ErrorDetails.Message
    Write-Host "Not continued: $(if ($detail) { ($detail | ConvertFrom-Json).error } else { $_.Exception.Message })" -ForegroundColor Red
    exit 1
}

if ($result.imported) { Write-Host "Added to the catalog: $($result.imported -join ', ')" }
if (-not $result.resumed) {
    Write-Host "Still missing: $($result.missing -join ', ')" -ForegroundColor Yellow
    Write-Host "Put them into assets\incoming\ (exactly these names) and run continue.cmd $JobId again,"
    Write-Host "or continue.cmd $JobId -SkipMissing to go on without them."
    exit 3
}

exit (Complete-VideoJob -JobId $JobId -NoOpen:$NoOpen)
