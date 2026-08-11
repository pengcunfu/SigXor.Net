# SigXor publish script: self-contained single-file exe (no .NET install required)
# Usage:
#   .\scripts\publish.ps1
#   .\scripts\publish.ps1 -Runtime win-x64
#   .\scripts\publish.ps1 -Runtime win-arm64
#   .\scripts\publish.ps1 -Zip

[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "SigXor.csproj"))) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$Project = Join-Path $Root "SigXor.csproj"
$OutDir = Join-Path $Root ("publish\" + $Runtime)

Write-Host "=== SigXor Publish ===" -ForegroundColor Cyan
Write-Host ("Project:       " + $Project)
Write-Host ("Configuration: " + $Configuration)
Write-Host ("Runtime:       " + $Runtime)
Write-Host ("Output:        " + $OutDir)
Write-Host "Mode:          SelfContained + SingleFile (no .NET runtime required)"
Write-Host ""

if (Test-Path $OutDir) {
    Write-Host "Cleaning previous output..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $OutDir
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$publishArgs = @(
    "publish", $Project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $OutDir,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:PublishReadyToRun=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishTrimmed=false"
)

Write-Host ("Running: dotnet " + ($publishArgs -join " ")) -ForegroundColor DarkGray
Write-Host ""

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw ("dotnet publish failed, exit code: " + $LASTEXITCODE)
}

$exe = Get-ChildItem -Path $OutDir -Filter "SigXor.exe" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not $exe) {
    throw ("SigXor.exe not found in: " + $OutDir)
}

$sizeMb = [math]::Round($exe.Length / 1MB, 2)
Write-Host ""
Write-Host "Publish succeeded" -ForegroundColor Green
Write-Host ("  EXE:  " + $exe.FullName)
Write-Host ("  Size: " + $sizeMb + " MB")
Write-Host ""
Write-Host "Notes:" -ForegroundColor Yellow
Write-Host "  - Target PC does NOT need .NET Runtime installed"
Write-Host "  - First launch may download speech/OCR models"
Write-Host ("  - Distribute files under: " + $OutDir)

if ($Zip) {
    $version = "1.0.0"
    try {
        [xml]$csproj = Get-Content $Project
        $verNode = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        if ($verNode) { $version = $verNode.ToString().Trim() }
    } catch { }

    $zipPath = Join-Path $Root ("publish\SigXor-" + $version + "-" + $Runtime + ".zip")
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

    Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $zipPath -Force
    $zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host ""
    Write-Host ("Zip created: " + $zipPath + " (" + $zipMb + " MB)") -ForegroundColor Green
}