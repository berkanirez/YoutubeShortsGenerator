# One-time setup for the local Kokoro TTS engine (free, offline after setup).
#   powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1
# Creates tools/tts/.venv (isolated from any global Python) and downloads the
# Kokoro-82M ONNX model files (Apache-2.0) into tools/tts/models/. Safe to re-run:
# existing venv/model files are reused.

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$venv = Join-Path $here ".venv"
$python = Join-Path $venv "Scripts/python.exe"
$models = Join-Path $here "models"

if (-not (Test-Path $python)) {
    # onnxruntime wheels lag behind the newest Python releases; 3.11 is known good.
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) { & py -3.11 -m venv $venv } else { & python -m venv $venv }
    if (-not (Test-Path $python)) { throw "Could not create a Python virtual environment at $venv." }
}

& $python -m pip install --disable-pip-version-check -r (Join-Path $here "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed." }

New-Item -ItemType Directory -Force $models | Out-Null
$base = "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0"
foreach ($file in "kokoro-v1.0.onnx", "voices-v1.0.bin") {
    $target = Join-Path $models $file
    if (-not (Test-Path $target)) {
        Write-Host "Downloading $file ..."
        curl.exe -sSL --fail -o "$target.part" "$base/$file"
        if ($LASTEXITCODE -ne 0) { throw "Download of $file failed." }
        Move-Item -Force "$target.part" $target
    }
}

Write-Host "Kokoro TTS is ready."
