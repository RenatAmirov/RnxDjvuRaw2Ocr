[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts\app' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$toolRoot = Join-Path $projectRoot 'tools'
foreach ($file in @('djvulibre\djvused.exe', 'djvulibre\ddjvu.exe', 'djvulibre\msvcp140.dll', 'djvulibre\vcruntime140.dll', 'tesseract\tesseract.exe', 'tesseract\tessdata\rus.traineddata', 'tesseract\tessdata\eng.traineddata')) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolRoot $file))) { throw 'Run scripts\Setup-Tools.ps1 before publishing.' }
}
dotnet publish (Join-Path $projectRoot 'src\RnxDjvuRaw2Ocr.App\RnxDjvuRaw2Ocr.App.csproj') -c Release -p:Platform=x64 --self-contained true -o $destination --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($resource in @('App.xbf', 'MainWindow.xbf', 'RnxDjvuRaw2Ocr.App.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $destination $resource))) { throw "Missing WinUI publish resource: $resource" }
}
$targetTools = Join-Path $destination 'tools'
New-Item -ItemType Directory -Path (Join-Path $targetTools 'djvulibre') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetTools 'tesseract') -Force | Out-Null
# Keep upstream documentation and licenses with the native binaries.
foreach ($entry in @('djvulibre', 'tesseract')) {
    $source = Join-Path $toolRoot $entry
    $target = Join-Path $targetTools $entry
    Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -notin @('$PLUGINSDIR', 'uninst.exe', 'Uninstall.exe') } |
        Copy-Item -Destination $target -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $destination -Force
Write-Host "Application: $(Join-Path $destination 'RnxDjvuRaw2Ocr.App.exe')"
