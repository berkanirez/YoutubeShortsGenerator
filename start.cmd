@echo off
rem Checks the local prerequisites, starts the Shorts Generator and opens the dashboard.
rem Options: -CheckOnly  -Yes  -NoBrowser   (see tools\workflow\start.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\workflow\start.ps1" %*
set rc=%errorlevel%
rem Keep the window open on a problem when started by double-click; "pause" alone would
rem reset the exit code a calling script relies on.
if not %rc%==0 pause
exit /b %rc%
