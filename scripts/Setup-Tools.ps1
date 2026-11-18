[CmdletBinding()]
param([string]$ToolsDirectory)

$ErrorActionPreference = 'Stop'
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $PSScriptRoot '..\tools' }
$toolsRoot = [IO.Path]::GetFullPath($ToolsDirectory)
$cache = Join-Path $toolsRoot '.downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$curlPath = (Get-Command curl.exe -ErrorAction Stop).Source

function Get-VerifiedDownload {
    param([string]$Uri, [string]$Destination, [string]$Sha256)
    if ((Test-Path -LiteralPath $Destination) -and ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -eq $Sha256)) {
        return
    }
    Write-Host "Downloading $([IO.Path]::GetFileName($Destination))..."
    $temporary = $Destination + '.partial'
    & $curlPath --fail --location --silent --show-error --retry 2 --connect-timeout 30 --max-time 600 --output $temporary $Uri
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $Uri" }
    $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) { throw "SHA256 mismatch for $Uri. Expected $Sha256, received $actual" }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

$sevenZip = @(
    (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
    (Join-Path $cache '7zip\Files\7-Zip\7z.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $sevenZip) {
    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($command) { $sevenZip = $command.Source }
}
if (-not $sevenZip) {
    # Administrative extraction unpacks the MSI locally; it does not install 7-Zip.
    $msi = Join-Path $cache '7zip.msi'
    Get-VerifiedDownload 'https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.msi' $msi '6DC822569E777D9A3ECD5FFCBC22B55600AF87E6C5D9810A64E8566CF5A4AF9F'
    $extractRoot = Join-Path $cache '7zip'
    $arguments = @('/a', ('"' + $msi + '"'), '/qn', ('TARGETDIR="' + $extractRoot + '"'))
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "7-Zip extraction failed: $($process.ExitCode). Install 7-Zip and retry." }
    $sevenZip = Join-Path $extractRoot 'Files\7-Zip\7z.exe'
    if (-not (Test-Path -LiteralPath $sevenZip)) { throw '7z.exe was not extracted. Install 7-Zip and retry.' }
}

$djvuInstaller = Join-Path $cache 'djvulibre-3.5.29.exe'
Get-VerifiedDownload 'https://downloads.sourceforge.net/project/djvu/DjVuLibre_Windows/3.5.29%2B4.12/DjVuLibre-3.5.29_DjView-4.12_Setup.exe?download=1' $djvuInstaller '92233FBF891C63F3FB7A0B5E1CE108BAA4C29A40C89A442D1313F883AAE84670'
$tesseractInstaller = Join-Path $cache 'tesseract-5.5.3.exe'
Get-VerifiedDownload 'https://github.com/tesseract-ocr/tesseract/releases/download/5.5.3/tesseract-ocr-w64-setup-5.5.3.20260724.exe' $tesseractInstaller 'BEE9E3434BD94FD65387D9BE28CD467A41F61B1275383B55B0F59A1331270AE4'

$djvuRoot = Join-Path $toolsRoot 'djvulibre'
$tessRoot = Join-Path $toolsRoot 'tesseract'
foreach ($entry in @(@($djvuInstaller, $djvuRoot), @($tesseractInstaller, $tessRoot))) {
    & $sevenZip x $entry[0] ('-o' + $entry[1]) -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw "Could not unpack $($entry[0])" }
}

# DjVuLibre is x86 and imports MSVCP140/VCRUNTIME140. Supply app-local CRTs rather than
# depending on a machine-wide redistributable installed by Visual Studio.
$vclibs = Join-Path $cache 'Microsoft.VCLibs.x86.Desktop.appx'
Get-VerifiedDownload 'https://download.microsoft.com/download/4/7/c/47c6134b-d61f-4024-83bd-b9c9ea951c25/Microsoft.VCLibs.x86.14.00.Desktop.appx' $vclibs 'A7FB9D76E07B36D868179EB53FFD13740C25242176FA363F154798CF34EDD4A9'
& $sevenZip e $vclibs 'msvcp140.dll' 'vcruntime140.dll' ('-o' + $djvuRoot) -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw 'Could not extract the Visual C++ runtime.' }

$tessdata = Join-Path $tessRoot 'tessdata'
New-Item -ItemType Directory -Path $tessdata -Force | Out-Null
Get-VerifiedDownload 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/rus.traineddata' (Join-Path $tessdata 'rus.traineddata') 'E16E5E036CCE1D9EC2B00063CF8B54472625B9E14D893A169E2B0DEDEB4DF225'
Get-VerifiedDownload 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata' (Join-Path $tessdata 'eng.traineddata') '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
Get-VerifiedDownload 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/LICENSE' (Join-Path $tessdata 'LICENSE-models.txt') 'CFC7749B96F63BD31C3C42B5C471BF756814053E847C10F3EB003417BC523D30'
& (Join-Path $tessRoot 'tesseract.exe') --tessdata-dir $tessdata --list-langs
if ($LASTEXITCODE -ne 0) { throw 'Tesseract could not start.' }
Write-Host "Ready. Tools: $toolsRoot"
