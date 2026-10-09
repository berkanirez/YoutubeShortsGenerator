# Day 29: one image per video with a frame from the middle of every scene, taken from the real
# final.mp4 (captions included) - for reviewing a batch of videos at a glance.
#   powershell -ExecutionPolicy Bypass -File tools/workflow/contact-sheet.ps1 -JobId <guid> -Out sheet.jpg
param([Parameter(Mandatory = $true)][string]$JobId, [Parameter(Mandatory = $true)][string]$Out)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$repo = Resolve-Path "$PSScriptRoot\..\.."
$jobDir = Join-Path $repo "output\$JobId"
$plan = Get-Content -Raw -Encoding UTF8 (Join-Path $jobDir "plan.json") | ConvertFrom-Json
$ffmpeg = Join-Path $repo "renderer\node_modules\@remotion\compositor-win32-x64-msvc\ffmpeg.exe"
$tmp = Join-Path $env:TEMP "sheet-$JobId"; New-Item -ItemType Directory -Force $tmp | Out-Null

$start = 0.0; $frames = @()
foreach ($scene in ($plan.scenes | Sort-Object order)) {
    $t = $start + $scene.durationSeconds * 0.6
    $file = Join-Path $tmp ("{0:00}.png" -f $scene.order)
    & $ffmpeg -loglevel error -y -ss ([string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:0.000}", $t)) -i (Join-Path $jobDir "final.mp4") -frames:v 1 -vf "scale=270:480" $file
    $frames += $file
    $start += $scene.durationSeconds
}

$cols = [Math]::Min(5, $frames.Count); $rows = [Math]::Ceiling($frames.Count / $cols)
$sheet = New-Object System.Drawing.Bitmap (270 * $cols), (480 * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::Black)
for ($i = 0; $i -lt $frames.Count; $i++) {
    $img = [System.Drawing.Image]::FromFile($frames[$i]); $g.DrawImage($img, ($i % $cols) * 270, [Math]::Floor($i / $cols) * 480, 270, 480); $img.Dispose()
}
$enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
$p = New-Object System.Drawing.Imaging.EncoderParameters 1; $p.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, [long]80)
$sheet.Save($Out, $enc, $p); $g.Dispose(); $sheet.Dispose()
Remove-Item -Recurse -Force $tmp
