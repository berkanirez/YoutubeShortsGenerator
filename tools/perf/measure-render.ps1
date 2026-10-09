# Day 27: measures where a render's time goes, on a real finished job, without the API.
#   powershell -ExecutionPolicy Bypass -File tools/perf/measure-render.ps1 -JobId <guid> [-Concurrency 4,6,8]
# Each step is timed separately: asset sync, bundling, and rendering from source (what the
# API did up to Day 26) vs. from a pre-built bundle, optionally at several concurrency levels.
param(
    [Parameter(Mandatory = $true)][string]$JobId,
    [int[]]$Concurrency = @()
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path "$PSScriptRoot\..\.."
$renderer = Join-Path $repo "renderer"
$cli = Join-Path $renderer "node_modules\@remotion\cli\remotion-cli.js"
$jobDir = Join-Path $repo "output\$JobId"
$out = Join-Path $env:TEMP "measure-render"
New-Item -ItemType Directory -Force $out | Out-Null

function Time([string]$label, [scriptblock]$block) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $block | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$label failed (exit $LASTEXITCODE)" }
    "{0,-46} {1,6:n1}s" -f $label, $sw.Elapsed.TotalSeconds
}

function Stage([string]$publicDir) {
    $stage = Join-Path $publicDir "jobs\$JobId"
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $jobDir "plan.json"), (Join-Path $jobDir "narration.wav") $stage
    $props = Join-Path $out "props.json"
    [IO.File]::WriteAllText($props, "{""planPath"":""jobs/$JobId/plan.json"",""narrationPath"":""jobs/$JobId/narration.wav""}")
    return $props
}

Push-Location $renderer
try {
    Time "asset sync (sync-public-assets.mjs)" { node scripts/sync-public-assets.mjs }
    $props = Stage (Join-Path $renderer "public")
    Time "render from source (bundle + render, Day 26)" { node $cli render src/index.ts ShortComposition (Join-Path $out "a.mp4") --props $props --log error }

    $bundleDir = Join-Path $out "bundle"
    Remove-Item -Recurse -Force $bundleDir -ErrorAction SilentlyContinue
    Time "bundle only (remotion bundle)" { node $cli bundle src/index.ts --out-dir $bundleDir --log error }
    $props = Stage (Join-Path $bundleDir "public")
    Time "render from pre-built bundle" { node $cli render $bundleDir ShortComposition (Join-Path $out "b.mp4") --props $props --log error }
    foreach ($c in $Concurrency) {
        Time "render from bundle, --concurrency $c" { node $cli render $bundleDir ShortComposition (Join-Path $out "c$c.mp4") --props $props --concurrency $c --log error }
    }
}
finally {
    Remove-Item -Recurse -Force (Join-Path $renderer "public\jobs\$JobId") -ErrorAction SilentlyContinue
    Pop-Location
}
