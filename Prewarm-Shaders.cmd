@echo off
rem Translates every shader of the game into the shader cache, then makes every pipeline earlier runs wrote
rem down (the launcher records them), so a run does not stutter on them. Run it after the first play, and
rem again after a rebuild or a driver update. Do not play while it runs. Goes on where it was stopped.
rem   Prewarm-Shaders.cmd "D:\path\to\your dump"   (the folder holding game_scarlett_master.exe)
rem   or set VICTORY_GAME_DIR first and run it without an argument.
setlocal EnableExtensions
set "REL=%~dp0target\release"
set "VICTORY=%REL%\victory.exe"
if not "%~1"=="" set "VICTORY_GAME_DIR=%~1"
if not defined VICTORY_GAME_DIR (
  echo Name your dump folder: Prewarm-Shaders.cmd "D:\path\to\your dump" 1>&2
  exit /b 2
)
if not exist "%VICTORY_GAME_DIR%\game_scarlett_master.exe" (
  echo No game_scarlett_master.exe in %VICTORY_GAME_DIR% 1>&2
  exit /b 2
)
if not exist "%VICTORY%" (
  echo Build first: cargo build --release --locked -p victory 1>&2
  exit /b 2
)
set "LOG=%LOCALAPPDATA%\victory\logs\prewarm.log"
if not exist "%LOCALAPPDATA%\victory\logs" mkdir "%LOCALAPPDATA%\victory\logs"
rem A frame generation unlocker installed as dxgi.dll is kept out of the many warm processes.
if exist "%REL%\dxgi.dll" ren "%REL%\dxgi.dll" dxgi.dll.off
echo Step 1 of 2: shaders (a few minutes). Log: %LOG%
echo === shaders %date% %time% > "%LOG%"
"%VICTORY%" shaders "%VICTORY_GAME_DIR%" >> "%LOG%" 2>&1
echo Step 2 of 2: pipelines (about 15-30 minutes the first time)
echo === warm %date% %time% >> "%LOG%"
"%VICTORY%" warm >> "%LOG%" 2>&1
set "CODE=%errorlevel%"
echo === done %date% %time% exit %CODE% >> "%LOG%"
if exist "%REL%\dxgi.dll.off" ren "%REL%\dxgi.dll.off" dxgi.dll
echo Done (exit %CODE%).
pause
exit /b %CODE%
