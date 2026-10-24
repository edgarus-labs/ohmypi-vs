#Requires -Version 7
param(
    [Parameter(Mandatory)] [string] $VsixPath,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$errors = [System.Collections.Generic.List[string]]::new()

function Compare-Version([string] $source, [string] $actual, [string] $expected) {
    if ($actual -ne $expected) {
        $errors.Add("$source is '$actual', expected '$expected'")
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Output "::error::Expected version '$Version' must look like 1.2.3"
    exit 1
}
if (-not (Test-Path -LiteralPath $VsixPath -PathType Leaf)) {
    Write-Output "::error::VSIX '$VsixPath' does not exist"
    exit 1
}

Compare-Version 'VSIX file name' (Split-Path -Leaf $VsixPath) "edgarus-labs.ohmypi.vs.v$Version.vsix"

Add-Type -AssemblyName System.IO.Compression.FileSystem
$assemblyFile = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName() + '.dll')
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $VsixPath).Path)
try {
    $manifestEntry = $archive.GetEntry('extension.vsixmanifest')
    if ($null -eq $manifestEntry) {
        $errors.Add('VSIX has no extension.vsixmanifest')
    }
    else {
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $namespaces = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
        $namespaces.AddNamespace('vsx', 'http://schemas.microsoft.com/developer/vsx-schema/2011')
        $identity = $manifest.SelectSingleNode('/vsx:PackageManifest/vsx:Metadata/vsx:Identity', $namespaces)
        if ($null -eq $identity) {
            $errors.Add('extension.vsixmanifest has no Identity element')
        }
        else {
            Compare-Version 'Manifest Identity Version' $identity.GetAttribute('Version') $Version
        }
    }

    $assemblyEntry = $archive.GetEntry('OhMyPi.VisualStudio.dll')
    if ($null -eq $assemblyEntry) {
        $errors.Add('VSIX has no OhMyPi.VisualStudio.dll')
    }
    else {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($assemblyEntry, $assemblyFile)
        $fileInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyFile)
        Compare-Version 'Assembly version' ([System.Reflection.AssemblyName]::GetAssemblyName($assemblyFile).Version.ToString()) "$Version.0"
        Compare-Version 'File version' $fileInfo.FileVersion "$Version.0"
        Compare-Version 'Product version' ($fileInfo.ProductVersion -replace '\+.*$', '') $Version
    }
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $assemblyFile) { Remove-Item -LiteralPath $assemblyFile }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Output "::error::$_" }
    exit 1
}
Write-Output "VSIX '$VsixPath' is consistently versioned $Version"
