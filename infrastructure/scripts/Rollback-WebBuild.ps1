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

    $ReleaseId = @(
        $prefixOutput -split '\s+' |
            Where-Object { $_ -and $_ -ne "None" } |
            ForEach-Object { $_.TrimEnd('/') } |
            Sort-Object -Descending
    ) | Select-Object -First 1
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

$currentSnapshotId =
    (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") +
    "-pre-rollback"

Write-Host "Saving the current live release as $currentSnapshotId"
& aws s3 sync "s3://$BucketName" `
    "s3://$RollbackBucketName/$currentSnapshotId" `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Failed to preserve the current live release before rollback."
}

Write-Host "Restoring rollback release $ReleaseId"
& aws s3 sync "s3://$RollbackBucketName/$ReleaseId" `
    "s3://$BucketName" `
    --delete `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore rollback release $ReleaseId."
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
