param(
    [string]$ReleaseTag = "20260417",
    [string]$AssetName = "mpv-dev-x86_64-20260417-git-c865008.7z",
    [string]$DestinationPath = "3rdparty/mpv/libmpv-2.dll"
)

$ErrorActionPreference = "Stop"

$destinationFullPath = if ([System.IO.Path]::IsPathRooted($DestinationPath)) {
    $DestinationPath
} else {
    Join-Path (Get-Location) $DestinationPath
}
$destinationDirectory = Split-Path -Parent $destinationFullPath

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$tempBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
$tempRoot = Join-Path $tempBase "mpv-download"

if (Test-Path $tempRoot) {
    Remove-Item $tempRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null

$archivePath = Join-Path $tempRoot $AssetName
$extractPath = Join-Path $tempRoot "extract"
$downloadUrl = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$ReleaseTag/$AssetName"

Write-Host "Downloading libmpv package from $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

New-Item -ItemType Directory -Path $extractPath | Out-Null

Write-Host "Extracting $AssetName"
& 7z x $archivePath "-o$extractPath" -y | Out-Null

$dllPath = Join-Path $extractPath "libmpv-2.dll"
if (-not (Test-Path $dllPath)) {
    throw "libmpv-2.dll was not found in $AssetName"
}

Copy-Item $dllPath $destinationFullPath -Force
Write-Host "libmpv copied to $destinationFullPath"
