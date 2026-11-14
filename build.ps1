param([string]$Dotnet='dotnet',[string]$Configuration='Release')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'native/build.ps1')
$destination=Join-Path $PSScriptRoot 'artifacts'
& $Dotnet build (Join-Path $PSScriptRoot 'NewStart.csproj') -c $Configuration -o $destination
if($LASTEXITCODE -ne 0){throw 'Managed build failed.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'NOTICE') -Destination $destination
Write-Output "Build ready in $destination"
