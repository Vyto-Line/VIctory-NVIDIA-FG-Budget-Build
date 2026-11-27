@echo off
rem Installs a frame generation unlocker DLL you downloaded yourself (for example dashdogy's MFG Unlocker),
rem so DLSS Frame Generation runs on GPUs NVIDIA does not enable it on (RTX 30). This repository does not
rem contain it: it is third-party, unsigned code that runs inside the game, so get it from its author.
rem   Install-MFG-Unlocker.cmd "C:\path\to\MFGUnlocker.dll"   install it beside victory.exe as dxgi.dll
rem   Install-MFG-Unlocker.cmd remove                         take it out again
setlocal EnableExtensions
set "REL=%~dp0target\release"
if /i "%~1"=="remove" (
  if exist "%REL%\dxgi.dll" del "%REL%\dxgi.dll"
  if exist "%REL%\dxgi.dll.off" del "%REL%\dxgi.dll.off"
  echo Removed.
  exit /b 0
)
if "%~1"=="" (
  echo Usage: Install-MFG-Unlocker.cmd "C:\path\to\MFGUnlocker.dll"   or   Install-MFG-Unlocker.cmd remove 1>&2
  exit /b 2
)
if not exist "%~1" (
  echo Not found: %~1 1>&2
  exit /b 2
)
if not exist "%REL%\victory.exe" (
  echo Build first: cargo build --release --locked -p victory 1>&2
  exit /b 2
)
rem Explorer hides extensions, so a hand rename easily makes dxgi.dll.dll, which Windows never loads.
copy /y "%~1" "%REL%\dxgi.dll" >nul || (
  echo Could not copy: close the game first. 1>&2
  exit /b 1
)
echo Installed as %REL%\dxgi.dll
echo Turn frame generation on in the F1 menu (or start with -FrameGen) and restart the game.
exit /b 0
