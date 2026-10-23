$ErrorActionPreference = "Stop"

$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../Builds"))
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$testRoot = Join-Path $stagingRoot ("WebBuildTest-" + [guid]::NewGuid().ToString("N"))
$source = Join-Path $testRoot "source"
$prepared = Join-Path $testRoot "prepared"
$build = Join-Path $source "Build"
$assets = @(
    "Release.loader.js",
    "Release.data.br",
    "Release.framework.js.br",
    "Release.wasm.br"
)

try {
    New-Item -ItemType Directory -Path $build -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $source "TemplateData") | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $source "index.html"),
        ($assets | ForEach-Object { 'buildUrl + "/' + $_ + '"' }) -join "`n")
    [System.IO.File]::WriteAllText(
        (Join-Path $source "TemplateData/style.css"),
        "body { color: white; }")

    foreach ($asset in $assets) {
        [System.IO.File]::WriteAllText((Join-Path $build $asset), "content:$asset")
    }

    & "$PSScriptRoot/../scripts/Prepare-WebBuild.ps1" `
        -SourcePath $source `
        -DestinationPath $prepared

    $preparedIndex = [System.IO.File]::ReadAllText((Join-Path $prepared "index.html"))
    foreach ($asset in $assets) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $build $asset) -Algorithm SHA256).Hash.ToLowerInvariant()
        $extension = [System.IO.Path]::GetExtension($asset)
        $stem = $asset.Substring(0, $asset.Length - $extension.Length)
        $versionedAsset = "$stem.$hash$extension"

        if (-not (Test-Path -LiteralPath (Join-Path $prepared "Build/$versionedAsset") -PathType Leaf)) {
            throw "Missing content-addressed asset: $versionedAsset"
        }

        if ($preparedIndex -notlike "*/$versionedAsset*") {
            throw "index.html does not reference $versionedAsset"
        }

        if ($preparedIndex.Contains("/$asset") -or
            (Test-Path -LiteralPath (Join-Path $prepared "Build/$asset"))) {
            throw "Unversioned asset remains in the prepared build: $asset"
        }

        if (-not (Test-Path -LiteralPath (Join-Path $build $asset) -PathType Leaf)) {
            throw "Preparing the build modified its source: $asset"
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $prepared "TemplateData/style.css") -PathType Leaf)) {
        throw "Preparing the build omitted other static files."
    }

    $invalidIndex = [System.IO.File]::ReadAllText((Join-Path $source "index.html"))
    [System.IO.File]::WriteAllText(
        (Join-Path $source "index.html"),
        $invalidIndex.Replace('/Release.wasm.br', '/missing.wasm.br'))

    $rejected = $false
    try {
        & "$PSScriptRoot/../scripts/Prepare-WebBuild.ps1" `
            -SourcePath $source `
            -DestinationPath (Join-Path $testRoot "invalid-output")
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Preparing a build with an unversioned missing reference should fail."
    }

    Write-Host "WebGL cache-busting preparation passed."
}
finally {
    $safePrefix = $stagingRoot + [System.IO.Path]::DirectorySeparatorChar + "WebBuildTest-"
    if (-not $testRoot.StartsWith($safePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected test directory: $testRoot"
    }

    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
