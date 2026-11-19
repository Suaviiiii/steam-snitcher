@echo off
rem Builds SteamSnitcher.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
rem The WebView2 libraries in lib\ (Microsoft, from the Microsoft.Web.WebView2 NuGet package) are embedded in the .exe.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:SteamSnitcher.exe ^
  /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /resource:lib\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll ^
  /resource:lib\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll ^
  /resource:lib\WebView2Loader.dll,WebView2Loader.dll ^
  src\SteamSnitcher.cs
