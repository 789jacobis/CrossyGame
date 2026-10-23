[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"

$source = (Resolve-Path -LiteralPath $SourcePath).Path
$destination = [System.IO.Path]::GetFullPath($DestinationPath)
$sourcePrefix = $source.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

if ($destination.Equals($source, [System.StringComparison]::OrdinalIgnoreCase) -or
    $destination.StartsWith($sourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The prepared build must be outside the source build directory."
}

if (Test-Path -LiteralPath $destination) {
    throw "The prepared build destination already exists: $destination"
}

$indexPath = Join-Path $source "index.html"
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Unity Web build is missing index.html: $source"
}

$assets = @(
    "Release.loader.js",
    "Release.data.br",
    "Release.framework.js.br",
    "Release.wasm.br"
)

foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath (Join-Path $source "Build/$asset") -PathType Leaf)) {
        throw "Unity Web build is missing Build/$asset"
    }
}

New-Item -ItemType Directory -Path $destination -ErrorAction Stop | Out-Null
Get-ChildItem -LiteralPath $source -Force |
    Copy-Item -Destination $destination -Recurse -Force

$preparedIndexPath = Join-Path $destination "index.html"
$index = [System.IO.File]::ReadAllText($preparedIndexPath)

foreach ($asset in $assets) {
    $assetPath = Join-Path $destination "Build/$asset"
    $hash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $extension = [System.IO.Path]::GetExtension($asset)
    $stem = $asset.Substring(0, $asset.Length - $extension.Length)
    $versionedAsset = "$stem.$hash$extension"
    $oldReference = "/$asset"

    if ([regex]::Matches($index, [regex]::Escape($oldReference)).Count -ne 1) {
        throw "Expected exactly one reference to $oldReference in index.html."
    }

    Move-Item -LiteralPath $assetPath -Destination (Join-Path $destination "Build/$versionedAsset")
    $index = $index.Replace($oldReference, "/$versionedAsset")
}

[System.IO.File]::WriteAllText(
    $preparedIndexPath,
    $index,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Prepared content-addressed WebGL build: $destination"
