$ErrorActionPreference='Stop'
$vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $installation){throw 'Visual C++ build tools are required.'}
$developer=Join-Path $installation 'Common7/Tools/Launch-VsDevShell.ps1'
& $developer -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$destination=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force $destination | Out-Null
& cl.exe /nologo /std:c++20 /EHsc /MD /O2 /LD (Join-Path $PSScriptRoot 'Backdrop.cpp') "/Fo$destination/Backdrop.obj" /link dxguid.lib windowsapp.lib dwmapi.lib user32.lib gdi32.lib CoreMessaging.lib ole32.lib propsys.lib "/OUT:$destination/NewStart.Backdrop.dll" "/IMPLIB:$destination/NewStart.Backdrop.lib"
if($LASTEXITCODE -ne 0){throw 'Backdrop build failed.'}

