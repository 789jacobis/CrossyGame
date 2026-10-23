[CmdletBinding()]
param(
    [string]$BuildPath = "$PSScriptRoot\..\..\Builds\Release",
    [string]$OutputsPath = "$PSScriptRoot\..\cdk-outputs.json",
    [string]$StackName = "CrossyRoadWebStack-production",
    [string]$BucketName,
    [string]$RollbackBucketName,
    [string]$DistributionId,
    [string]$GameUrl,
    [string]$ReleaseId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
)

$ErrorActionPreference = "Stop"

$resolvedBuildPath = (Resolve-Path -LiteralPath $BuildPath).Path
$indexPath = Join-Path $resolvedBuildPath "index.html"

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Unity Web build is missing index.html: $resolvedBuildPath"
}

if (-not $BucketName -or -not $RollbackBucketName -or
    -not $DistributionId -or -not $GameUrl) {
    $resolvedOutputsPath = (Resolve-Path -LiteralPath $OutputsPath).Path
    $outputs = Get-Content -LiteralPath $resolvedOutputsPath -Raw |
        ConvertFrom-Json
    $stackOutputs = $outputs.$StackName

    if ($null -eq $stackOutputs) {
        throw "Stack outputs not found: $StackName"
    }

    $BucketName = $stackOutputs.WebBucketName
    $RollbackBucketName = $stackOutputs.WebRollbackBucketName
    $DistributionId = $stackOutputs.CloudFrontDistributionId
    $GameUrl = $stackOutputs.GameUrl
}

if (-not $BucketName -or -not $RollbackBucketName -or
    -not $DistributionId -or -not $GameUrl) {
    throw "Web deployment outputs are incomplete."
}

if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw "ReleaseId may contain only letters, numbers, dots, underscores, and hyphens."
}

$stagingRoot = if ($env:RUNNER_TEMP) {
    [System.IO.Path]::GetFullPath((Join-Path $env:RUNNER_TEMP "CrossyRoadWebPublish"))
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../Builds"))
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$preparedPath = Join-Path $stagingRoot ("WebPublish-" + [guid]::NewGuid().ToString("N"))

try {
    & "$PSScriptRoot/Prepare-WebBuild.ps1" `
        -SourcePath $resolvedBuildPath `
        -DestinationPath $preparedPath

    & aws s3api head-object `
        --bucket $BucketName `
        --key "index.html" `
        --output json 2>$null | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Archiving the current release as $ReleaseId"
        & "$PSScriptRoot/Save-WebBuildSnapshot.ps1" `
            -BucketName $BucketName `
            -RollbackBucketName $RollbackBucketName `
            -ReleaseId $ReleaseId
    }
    else {
        Write-Host "No existing release was found; skipping the rollback snapshot."
    }

    # Upload assets first and publish index.html last. Existing immutable assets
    # remain available to players who opened an older page before this release.
    Write-Host "Publishing $preparedPath to s3://$BucketName"
    & aws s3 sync $preparedPath "s3://$BucketName" `
        --exclude "index.html" `
        --cache-control "no-cache" `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "S3 synchronization failed with exit code $LASTEXITCODE."
    }

    $metadataFiles = Get-ChildItem `
        -LiteralPath $preparedPath `
        -Recurse `
        -File | Where-Object {
            $_.Extension -eq ".br" -or
            $_.Name -match '^Release\.loader\.[a-f0-9]{64}\.js$'
        }

    foreach ($file in $metadataFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $preparedPath,
            $file.FullName).Replace('\', '/')

        $contentType = switch -Regex ($file.Name) {
            '\.js$' { "application/javascript"; break }
            '\.wasm(?:\.[a-f0-9]{64})?\.br$' { "application/wasm"; break }
            '\.js(?:\.[a-f0-9]{64})?\.br$' { "application/javascript"; break }
            '\.json(?:\.[a-f0-9]{64})?\.br$' { "application/json"; break }
            default { "application/octet-stream" }
        }

        $isVersioned = $relativePath -match '^Build/Release\..+\.[a-f0-9]{64}\.(?:br|js)$'
        $cacheControl = if ($isVersioned) {
            "public,max-age=31536000,immutable"
        }
        else {
            "no-cache"
        }

        $metadataArgs = @(
            "--content-type", $contentType,
            "--cache-control", $cacheControl,
            "--only-show-errors"
        )
        if ($file.Extension -eq ".br") {
            $metadataArgs += @("--content-encoding", "br")
        }

        & aws s3 cp $file.FullName "s3://$BucketName/$relativePath" @metadataArgs

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to upload WebGL asset metadata: $relativePath"
        }
    }

    & aws s3 cp (Join-Path $preparedPath "index.html") "s3://$BucketName/index.html" `
        --content-type "text/html; charset=utf-8" `
        --cache-control "no-cache, max-age=0, must-revalidate" `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload index.html."
    }

    $invalidationId = & aws cloudfront create-invalidation `
        --distribution-id $DistributionId `
        --paths "/*" `
        --query "Invalidation.Id" `
        --output text

    if ($LASTEXITCODE -ne 0) {
        throw "CloudFront invalidation failed with exit code $LASTEXITCODE."
    }

    Write-Host "Waiting for CloudFront invalidation $invalidationId"
    & aws cloudfront wait invalidation-completed `
        --distribution-id $DistributionId `
        --id $invalidationId

    if ($LASTEXITCODE -ne 0) {
        throw "CloudFront invalidation wait failed with exit code $LASTEXITCODE."
    }

    Write-Host "Published successfully: $GameUrl"
}
finally {
    $safePrefix = $stagingRoot + [System.IO.Path]::DirectorySeparatorChar + "WebPublish-"
    if (-not $preparedPath.StartsWith($safePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected prepared build path: $preparedPath"
    }

    if (Test-Path -LiteralPath $preparedPath) {
        Remove-Item -LiteralPath $preparedPath -Recurse -Force
    }
}
