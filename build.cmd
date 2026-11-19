@echo off
rem Builds SteamSnitcher.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:SteamSnitcher.exe ^
  /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:Microsoft.CSharp.dll /r:System.Core.dll src\SteamSnitcher.cs
