# Day 28: local prerequisite checks shared by start.ps1 and generate.ps1 (dot-sourced).
# Every check prints OK / MISSING with the exact fix. With -Fix, cheap fixes are offered
# (and applied after a "y", or straight away with -Yes): npm ci, the TTS setup script,
# starting the Ollama app, pulling the model. Nothing here is paid or uploads anything.

$script:Repo = Resolve-Path "$PSScriptRoot\..\.."
$script:ApiUrl = "http://localhost:5007"

function Get-AppSetting([string]$section, [string]$key, [string]$default) {
    # Same precedence as ASP.NET Core: an environment variable (Section__Key) wins.
    $fromEnv = [Environment]::GetEnvironmentVariable("${section}__${key}")
    if ($fromEnv) { return $fromEnv }
    $settings = Get-Content -Raw (Join-Path $script:Repo "src\ShortsGenerator.Api\appsettings.json") | ConvertFrom-Json
    $value = $settings.$section.$key
    if ($value) { return $value } else { return $default }
}

function Write-Check([bool]$ok, [string]$what, [string]$fix = "") {
    if ($ok) {
        Write-Host "  [OK]      $what" -ForegroundColor Green
    } else {
        Write-Host "  [MISSING] $what" -ForegroundColor Red
        if ($fix) { Write-Host "            -> $fix" -ForegroundColor Yellow }
    }
}

function Confirm-Fix([string]$question, [bool]$yes) {
    if ($yes) { return $true }
    $answer = Read-Host "            $question [y/N]"
    return $answer -match '^(y|yes|e|evet)$'
}

function Test-OllamaUp([string]$baseUrl) {
    try { Invoke-RestMethod "$baseUrl/api/version" -TimeoutSec 3 | Out-Null; return $true } catch { return $false }
}

# Returns $true when everything needed to make a video is in place.
function Test-Prerequisites([switch]$Fix, [switch]$Yes) {
    Write-Host "Checking local prerequisites (everything runs on this PC, no paid services):"
    $allOk = $true

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetOk = $dotnet -and ((& dotnet --version) -match '^(1[0-9])\.')
    Write-Check $dotnetOk ".NET 10 SDK" "Install it from https://dotnet.microsoft.com/download (free), then run this again."
    $allOk = $allOk -and $dotnetOk

    $node = Get-Command node -ErrorAction SilentlyContinue
    $nodeOk = $node -and ((& node --version) -match '^v(1[89]|[2-9][0-9])\.')
    Write-Check $nodeOk "Node.js 18+ (renderer)" "Install the LTS from https://nodejs.org (free), then run this again."
    $allOk = $allOk -and $nodeOk

    $remotion = Join-Path $script:Repo "renderer\node_modules\@remotion\cli\remotion-cli.js"
    $remotionOk = Test-Path $remotion
    Write-Check $remotionOk "Renderer packages (renderer/node_modules)" "cd renderer; npm ci"
    if (-not $remotionOk -and $Fix -and $nodeOk -and (Confirm-Fix "Install them now with 'npm ci' (a few minutes, one time)?" $Yes)) {
        Push-Location (Join-Path $script:Repo "renderer"); try { npm ci } finally { Pop-Location }
        $remotionOk = Test-Path $remotion
        Write-Check $remotionOk "Renderer packages (renderer/node_modules)"
    }
    $allOk = $allOk -and $remotionOk

    $tts = Join-Path $script:Repo "tools\tts"
    $ttsOk = (Test-Path "$tts\.venv\Scripts\python.exe") -and (Test-Path "$tts\models\kokoro-v1.0.onnx") -and (Test-Path "$tts\models\voices-v1.0.bin")
    Write-Check $ttsOk "Kokoro voice (tools/tts venv + model)" "powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1"
    if (-not $ttsOk -and $Fix -and (Confirm-Fix "Set it up now (Python 3.11 venv + ~350 MB model download, one time)?" $Yes)) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File "$tts\setup.ps1"
        $ttsOk = (Test-Path "$tts\.venv\Scripts\python.exe") -and (Test-Path "$tts\models\kokoro-v1.0.onnx")
        Write-Check $ttsOk "Kokoro voice (tools/tts venv + model)"
    }
    $allOk = $allOk -and $ttsOk

    $baseUrl = Get-AppSetting "Ollama" "BaseUrl" "http://127.0.0.1:11434"
    $model = Get-AppSetting "Ollama" "Model" "llama3.2:3b"
    $ollamaUp = Test-OllamaUp $baseUrl
    $ollamaExe = Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama app.exe"
    $installHint = if (Test-Path $ollamaExe) { "Start the Ollama app (Start menu)." } else { "winget install Ollama.Ollama   (free), then start the Ollama app." }
    Write-Check $ollamaUp "Ollama running at $baseUrl" $installHint
    if (-not $ollamaUp -and $Fix -and (Test-Path $ollamaExe) -and (Confirm-Fix "Start the Ollama app now?" $Yes)) {
        Start-Process $ollamaExe
        for ($i = 0; $i -lt 30 -and -not $ollamaUp; $i++) { Start-Sleep 1; $ollamaUp = Test-OllamaUp $baseUrl }
        Write-Check $ollamaUp "Ollama running at $baseUrl"
    }
    $allOk = $allOk -and $ollamaUp

    if ($ollamaUp) {
        $names = @((Invoke-RestMethod "$baseUrl/api/tags").models | ForEach-Object { $_.name })
        $modelOk = $names -contains $model -or ($names | Where-Object { $_ -like "${model}:*" })
        Write-Check ([bool]$modelOk) "Model '$model' pulled" "ollama pull $model"
        if (-not $modelOk -and $Fix -and (Confirm-Fix "Download it now with 'ollama pull $model' (~2 GB, one time)?" $Yes)) {
            & (Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe") pull $model
            $names = @((Invoke-RestMethod "$baseUrl/api/tags").models | ForEach-Object { $_.name })
            $modelOk = $names -contains $model -or ($names | Where-Object { $_ -like "${model}:*" })
            Write-Check ([bool]$modelOk) "Model '$model' pulled"
        }
        $allOk = $allOk -and [bool]$modelOk
    }

    $drive = (Get-Item $script:Repo).PSDrive
    $freeGb = [math]::Round($drive.Free / 1GB, 1)
    $diskOk = $freeGb -ge 2
    Write-Check $diskOk "Free disk space on ${drive}: ($freeGb GB; a video is ~5 MB, renders need ~1 GB scratch)" "Free up some space."
    $allOk = $allOk -and $diskOk

    return $allOk
}

function Test-ApiUp {
    try { Invoke-RestMethod "$script:ApiUrl/api/health" -TimeoutSec 2 | Out-Null; return $true } catch { return $false }
}
