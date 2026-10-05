<#
.SYNOPSIS
  Builds, tests and publishes ZipDrop; compiles the installer if Inno Setup is available.

.EXAMPLE
  ./build.ps1                    # test + framework-dependent publish (~0.6 MB, needs .NET Desktop Runtime)
  ./build.ps1 -SelfContained     # single-file exe with the runtime embedded (no prerequisites, larger)
  ./build.ps1 -SkipTests
#>
param(
    [switch]$SelfContained,
    [switch]$SkipTests,
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

[xml]$proj = Get-Content src/ZipDrop/ZipDrop.csproj
$version = ($proj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$publishDir = Join-Path $PSScriptRoot "artifacts/publish"

if (-not $SkipTests) {
    dotnet test tests/ZipDrop.Core.Tests -c $Configuration --nologo
    if ($LASTEXITCODE) { throw "Tests failed" }
}

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
$publishArgs = @("publish", "src/ZipDrop", "-c", $Configuration, "-r", "win-x64", "-o", $publishDir, "--nologo",
          "-p:PublishReadyToRun=true", "-p:DebugType=none")
if ($SelfContained) {
    $publishArgs += @("--self-contained", "true", "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
               "-p:IncludeNativeLibrariesForSelfExtract=true")
} else {
    $publishArgs += @("--self-contained", "false")
}
dotnet @publishArgs
if ($LASTEXITCODE) { throw "Publish failed" }
$size = (Get-ChildItem $publishDir -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Published ZipDrop {0} to {1} ({2:N1} MB)" -f $version, $publishDir, $size)

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    & $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" installer/ZipDrop.iss
    if ($LASTEXITCODE) { throw "Installer build failed" }
    Write-Host "Installer written to artifacts/installer"
} else {
    Write-Host "Inno Setup 6 not found: skipped installer. Install it (winget install JRSoftware.InnoSetup) and re-run." -ForegroundColor Yellow
}
