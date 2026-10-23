[CmdletBinding()]
param(
    [string]$ReleaseId,
    [string]$OutputsPath = "$PSScriptRoot\..\cdk-outputs.json",
    [string]$StackName = "CrossyRoadWebStack-production",
    [string]$BucketName,
    [string]$RollbackBucketName,
    [string]$DistributionId,
    [string]$GameUrl
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/WebBuildAssetReferences.ps1"

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

if ($ReleaseId -and $ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw "ReleaseId may contain only letters, numbers, dots, underscores, and hyphens."
}

if (-not $ReleaseId) {
    $prefixOutput = & aws s3api list-objects-v2 `
        --bucket $RollbackBucketName `
        --delimiter "/" `
        --query "CommonPrefixes[].Prefix" `
        --output text

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to list rollback releases."
    }

    $availableIds = @(
        $prefixOutput -split '\s+' |
            Where-Object { $_ -and $_ -ne "None" } |
            ForEach-Object { $_.TrimEnd('/') } |
            Sort-Object -Descending
    )

    foreach ($candidateId in $availableIds) {
        & aws s3api head-object `
            --bucket $RollbackBucketName `
            --key "$candidateId/index.html" `
            --output json 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $ReleaseId = $candidateId
            break
        }
    }
}

if (-not $ReleaseId) {
    throw "No rollback release is available."
}

$targetIndexKey = "$ReleaseId/index.html"
& aws s3api head-object `
    --bucket $RollbackBucketName `
    --key $targetIndexKey `
    --output json 2>$null | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Rollback release is incomplete or does not exist: $ReleaseId"
}

$snapshotIndexOutput = & aws s3 cp `
    "s3://$RollbackBucketName/$ReleaseId/index.html" `
    - --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw "Failed to read rollback index.html: $ReleaseId"
}

$snapshotIndex = $snapshotIndexOutput -join "`n"
$snapshotAssets = @(Get-ReferencedBuildKeys `
    -IndexHtml $snapshotIndex `
    -Label "Rollback $ReleaseId index.html")
foreach ($key in $snapshotAssets) {
    & aws s3api head-object `
        --bucket $RollbackBucketName `
        --key "$ReleaseId/$key" `
        --output json 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Rollback release is missing $key; live release was not changed."
    }
}

$currentSnapshotId =
    (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") +
    "-pre-rollback"

Write-Host "Saving the current live release as $currentSnapshotId"
& "$PSScriptRoot/Save-WebBuildSnapshot.ps1" `
    -BucketName $BucketName `
    -RollbackBucketName $RollbackBucketName `
    -ReleaseId $currentSnapshotId

Write-Host "Restoring rollback release $ReleaseId"
& aws s3 sync "s3://$RollbackBucketName/$ReleaseId" `
    "s3://$BucketName" `
    --exclude "Build/*" `
    --exclude "index.html" `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore rollback support files for $ReleaseId."
}

foreach ($key in $snapshotAssets) {
    & aws s3 cp "s3://$RollbackBucketName/$ReleaseId/$key" `
        "s3://$BucketName/$key" `
        --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restore rollback asset: $key"
    }
}

& aws s3 cp "s3://$RollbackBucketName/$ReleaseId/index.html" `
    "s3://$BucketName/index.html" `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore rollback index.html."
}

# Snapshots created before content-addressed assets used fixed filenames with
# immutable caching. Give those restored URLs a fresh browser cache key.
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../Builds"))
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$legacyIndexPath = Join-Path $stagingRoot ("RollbackIndex-" + [guid]::NewGuid().ToString("N") + ".html")

try {
    & aws s3 cp "s3://$BucketName/index.html" $legacyIndexPath --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to inspect restored index.html."
    }

    $index = [System.IO.File]::ReadAllText($legacyIndexPath)
    if ($index.Contains('/Release.loader.js')) {
        foreach ($asset in @(
            "Release.loader.js",
            "Release.data.br",
            "Release.framework.js.br",
            "Release.wasm.br"
        )) {
            $pattern = '"/' + [regex]::Escape($asset) + '(?:\?v=[^"]*)?"'
            if ([regex]::Matches($index, $pattern).Count -ne 1) {
                throw "Restored index.html does not contain one $asset reference."
            }

            $index = [regex]::Replace(
                $index,
                $pattern,
                '"/' + $asset + '?v=' + $currentSnapshotId + '"')
        }

        [System.IO.File]::WriteAllText(
            $legacyIndexPath,
            $index,
            [System.Text.UTF8Encoding]::new($false))

        & aws s3 cp $legacyIndexPath "s3://$BucketName/index.html" `
            --content-type "text/html; charset=utf-8" `
            --cache-control "no-cache, max-age=0, must-revalidate" `
            --only-show-errors

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to cache-bust the legacy rollback index.html."
        }
    }
}
finally {
    $safePrefix = $stagingRoot + [System.IO.Path]::DirectorySeparatorChar + "RollbackIndex-"
    if (-not $legacyIndexPath.StartsWith($safePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected rollback index path: $legacyIndexPath"
    }

    if (Test-Path -LiteralPath $legacyIndexPath) {
        Remove-Item -LiteralPath $legacyIndexPath -Force
    }
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

Write-Host "Rollback completed: $ReleaseId -> $GameUrl"
