@echo off
rem Continues a video that stopped for pictures: continue.cmd <job-id> [-SkipMissing]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\workflow\continue.ps1" %*
