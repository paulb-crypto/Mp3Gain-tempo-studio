param(
    [ValidateSet("All", "Windows", "Mac")]
    [string]$Target = "All"
)

$Dotnet = "dotnet"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) {
    $Dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " MP3 Gain & BPM Studio - Build & Package" -ForegroundColor Cyan
Write-Host " Target: $Target" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$PublishDir = Join-Path $PSScriptRoot "publish"
if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
}

if ($Target -eq "All" -or $Target -eq "Windows") {
    Write-Host "`n[1] Building Windows Standalone Executable..." -ForegroundColor Yellow
    & $Dotnet publish "src\Mp3GainApp\Mp3GainApp.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $PublishDir
    Write-Host "Windows build created: $PublishDir\Mp3GainApp.exe" -ForegroundColor Green
}

if ($Target -eq "All" -or $Target -eq "Mac") {
    Write-Host "`n[2] Building macOS Apple Silicon (osx-arm64)..." -ForegroundColor Yellow
    & $Dotnet publish "src\Mp3GainApp.Mac\Mp3GainApp.Mac.csproj" -c Release -r osx-arm64 --self-contained true -o "$PublishDir\osx-arm64"

    Write-Host "`n[3] Building macOS Intel (osx-x64)..." -ForegroundColor Yellow
    & $Dotnet publish "src\Mp3GainApp.Mac\Mp3GainApp.Mac.csproj" -c Release -r osx-x64 --self-contained true -o "$PublishDir\osx-x64"

    Write-Host "`n[4] Packaging macOS .app Bundles, DMGs, and ZIPs..." -ForegroundColor Yellow
    & $Dotnet run --project "tools\PackageMacApp\PackageMacApp.csproj"
    Write-Host "macOS packages created in $PublishDir" -ForegroundColor Green
}

Write-Host "`nAll builds completed successfully!" -ForegroundColor Green

