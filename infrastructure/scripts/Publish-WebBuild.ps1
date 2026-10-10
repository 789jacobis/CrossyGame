[CmdletBinding()]
param(
    [string]$BuildPath = "$PSScriptRoot\..\..\Builds\Release",
    [string]$OutputsPath = "$PSScriptRoot\..\cdk-outputs.json",
    [string]$StackName = "CrossyRoadWebStack-production"
)

$ErrorActionPreference = "Stop"

$resolvedBuildPath = (Resolve-Path -LiteralPath $BuildPath).Path
$resolvedOutputsPath = (Resolve-Path -LiteralPath $OutputsPath).Path
$indexPath = Join-Path $resolvedBuildPath "index.html"

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Unity Web build is missing index.html: $resolvedBuildPath"
}

$outputs = Get-Content -LiteralPath $resolvedOutputsPath -Raw |
    ConvertFrom-Json
$stackOutputs = $outputs.$StackName

if ($null -eq $stackOutputs) {
    throw "Stack outputs not found: $StackName"
}

$bucketName = $stackOutputs.WebBucketName
$distributionId = $stackOutputs.CloudFrontDistributionId
$gameUrl = $stackOutputs.GameUrl

if (-not $bucketName -or -not $distributionId -or -not $gameUrl) {
    throw "Web deployment outputs are incomplete."
}

Write-Host "Publishing $resolvedBuildPath to s3://$bucketName"
& aws s3 sync $resolvedBuildPath "s3://$bucketName" `
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

    & aws s3 cp $file.FullName "s3://$bucketName/$relativePath" `
        --content-type $contentType `
        --content-encoding "br" `
        --cache-control "public,max-age=31536000,immutable" `
        --only-show-errors

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload compressed asset: $relativePath"
    }
}

$invalidationId = & aws cloudfront create-invalidation `
    --distribution-id $distributionId `
    --paths "/*" `
    --query "Invalidation.Id" `
    --output text

if ($LASTEXITCODE -ne 0) {
    throw "CloudFront invalidation failed with exit code $LASTEXITCODE."
}

Write-Host "Waiting for CloudFront invalidation $invalidationId"
& aws cloudfront wait invalidation-completed `
    --distribution-id $distributionId `
    --id $invalidationId

if ($LASTEXITCODE -ne 0) {
    throw "CloudFront invalidation wait failed with exit code $LASTEXITCODE."
}

Write-Host "Published successfully: $gameUrl"
