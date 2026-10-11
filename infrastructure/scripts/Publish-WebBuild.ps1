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

& aws s3api head-object `
    --bucket $BucketName `
    --key "index.html" `
    --output json 2>$null | Out-Null

if ($LASTEXITCODE -eq 0) {
    Write-Host "Archiving the current release as $ReleaseId"
    & aws s3 sync "s3://$BucketName" `
        "s3://$RollbackBucketName/$ReleaseId" `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to archive the current WebGL release."
    }
}
else {
    Write-Host "No existing release was found; skipping the rollback snapshot."
}

Write-Host "Publishing $resolvedBuildPath to s3://$BucketName"
& aws s3 sync $resolvedBuildPath "s3://$BucketName" `
    --delete `
    --cache-control "no-cache" `
    --only-show-errors

if ($LASTEXITCODE -ne 0) {
    throw "S3 synchronization failed with exit code $LASTEXITCODE."
}

$compressedFiles = Get-ChildItem `
    -LiteralPath $resolvedBuildPath `
    -Recurse `
    -File `
    -Filter "*.br"

foreach ($file in $compressedFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath(
        $resolvedBuildPath,
        $file.FullName).Replace('\', '/')

    $contentType = switch -Regex ($file.Name) {
        '\.wasm\.br$' { "application/wasm"; break }
        '\.js\.br$' { "application/javascript"; break }
        '\.json\.br$' { "application/json"; break }
        default { "application/octet-stream" }
    }

    & aws s3 cp $file.FullName "s3://$BucketName/$relativePath" `
        --content-type $contentType `
        --content-encoding "br" `
        --cache-control "public,max-age=31536000,immutable" `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload compressed asset: $relativePath"
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

Write-Host "Published successfully: $GameUrl"
