@echo off
rem Makes one Short: generate.cmd "Why Your Pull-Ups Aren't Increasing"
rem Options: -ScriptFile my-script.txt  -NoOpen  -Yes   (see tools\workflow\generate.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\workflow\generate.ps1" %*
