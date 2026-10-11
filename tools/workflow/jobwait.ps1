# Shared by generate.ps1 and continue.ps1: follows a job until it is done, failed, or waiting
# for pictures, prints what to do next, and returns the exit code.
function Complete-VideoJob([string]$JobId, [switch]$NoOpen) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $last = ""
    while ($true) {
        Start-Sleep -Milliseconds 700
        $j = Invoke-RestMethod "$script:ApiUrl/api/videos/$JobId"
        if ($j.status -ne $last) {
            Write-Host ("  {0,5:n0}s  {1}" -f $sw.Elapsed.TotalSeconds, $j.status)
            $last = $j.status
        }
        if ($j.status -in "Completed", "Failed", "WaitingForArt" -and -not $j.isRunning -and -not $j.isQueued) { break }
    }

    $folder = Join-Path $script:Repo "output\$JobId"
    if ($j.status -eq "WaitingForArt") {
        $requests = Invoke-RestMethod "$script:ApiUrl/api/videos/$JobId/art"
        Write-Host ""
        Write-Host "This video needs $(@($requests).Count) picture(s) the catalog doesn't have yet:" -ForegroundColor Yellow
        foreach ($r in $requests) {
            Write-Host ""
            Write-Host ("== {0}   (scene {1}{2})" -f $r.fileName, ($r.sceneIds -join ", "),
                $(if ($r.needsReferenceImage) { "; attach assets\mascot\mascot-idle.png as the reference" } else { "" })) -ForegroundColor Cyan
            Write-Host $r.prompt
        }
        Write-Host ""
        Write-Host "Save each image under the name shown - upload it in the dashboard ($script:ApiUrl)"
        Write-Host "or put it into assets\incoming\ - then run:  continue.cmd $JobId"
        Write-Host "(continue.cmd $JobId -SkipMissing goes on without the missing ones)"
        Write-Host "The prompts are also in $folder\art-requests.md"
        return 3
    }

    if ($j.status -eq "Failed") {
        Write-Host ""
        Write-Host "Failed: $($j.errorMessage)" -ForegroundColor Red
        Write-Host "Details: $folder\job.log"
        Write-Host "After fixing the cause, press Retry in the dashboard ($script:ApiUrl) - finished stages are kept."
        return 1
    }

    Write-Host ""
    Write-Host ("Done in {0:n0}s: {1}" -f $sw.Elapsed.TotalSeconds, $j.finalVideoPath) -ForegroundColor Green
    Write-Host "Before publishing, go through the checklist (tick it in the dashboard: $script:ApiUrl):"
    $review = Get-Content -Encoding UTF8 (Join-Path $folder "review.md") -ErrorAction SilentlyContinue
    $review | Where-Object { $_.StartsWith("- [ ]") } | ForEach-Object { Write-Host "  $_" }
    if (-not $NoOpen) { Start-Process explorer.exe -ArgumentList $folder }
    return 0
}
