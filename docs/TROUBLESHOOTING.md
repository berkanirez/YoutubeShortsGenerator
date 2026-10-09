# Troubleshooting

First steps for any problem:

1. `start.cmd -CheckOnly` - lists every missing prerequisite with the exact fix.
2. The dashboard shows a failed job's message; the full story is in
   `output/<job-id>/job.log` ("Open folder").
3. After fixing the cause, press **Retry**: finished stages (script, plan, voice-over) are kept.

| Symptom | Cause | Fix |
|---|---|---|
| `Could not reach Ollama at http://127.0.0.1:11434/` | Ollama not running | Start the Ollama app (Start menu), or `start.cmd` offers it. |
| `Ollama model 'llama3.2:3b' is not available` | model not pulled | `ollama pull llama3.2:3b` |
| `Ollama returned HTTP 500: model requires more system memory ...` | model too big for free RAM | Close memory-heavy apps, or set a smaller `Ollama:Model`. |
| `Ollama did not answer within 120 seconds` | model still loading / PC busy | Retry; if it repeats, use a smaller model. |
| `Local TTS (kokoro) is not ready: ...` | venv or model files missing | `powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1` |
| `Renderer ... not ready: Remotion is not installed` | `renderer/node_modules` missing | `cd renderer; npm ci` |
| `Invalid plan JSON (...): scenes[3].id: ...` | a hand-edited or broken plan.json | Fix the named field, or generate the plan again. |
| `The scene plan of video job ... can't be read` | corrupt `plan.json` | Delete it and Retry (the plan is generated again). |
| `Scene 's2' uses asset 'x', which is not in the asset catalog` | the manifest changed | Pick another visual with **Change** in the scene plan. |
| `Your script can't be used: Too short (...)` | own script outside 65-140 words or with a medical claim | Edit the script. |
| `Stopped after being interrupted 3 times in a row` | the API stopped/crashed during this job three times | Read `job.log`, then Retry. |
| A video takes 2-3x longer than usual | Windows sometimes throttles a long-running API's render processes (Day 29, not fully explained) | Restart the API (close its window, `start.cmd`). Measure with `tools/perf/measure-render.ps1 -JobId <id>`. |
| The dashboard says "Paused" | a job created with `autoGenerate=false`, or from before Day 26 | **Continue**. |
| The video looks outdated after an edit | the plan is newer than the video | **Re-render** (it regenerates the voice-over first if narration text changed). |
| Port 5007 in use | another API instance | `start.cmd` detects a running API and just opens the dashboard; otherwise stop the other process. |
| The PC goes to sleep during a batch | Windows power settings | Keep it awake while generating (a long batch is CPU-bound for minutes). |

## Checking that everything still works

```
dotnet test                      # 413 backend tests, no engines needed
cd renderer; npm test            # 64 renderer tests
cd renderer; npm run smoke       # real low-res render of every scene type (~25s)
generate.cmd "Why Your Pull-Ups Aren't Increasing"   # the real thing, ~1 minute
```
