@echo off
rem Builds VIctory-Prewarm.exe into the repository root with the C# compiler that ships with Windows (.NET Framework 4).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 ^
  /win32manifest:app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /out:..\..\VIctory-Prewarm.exe Program.cs
