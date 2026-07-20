$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$dist = Join-Path $root 'dist\ScreenMind-win-x64'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet restore ScreenMind.sln
dotnet build ScreenMind.sln -c Release --no-restore
dotnet publish src\ScreenMind.App\ScreenMind.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $dist
$exe = Join-Path $dist 'SystemServiceHost.exe'
if (!(Test-Path $exe)) { throw "Executable was not produced: $exe" }
Write-Host "Build complete: $exe"
