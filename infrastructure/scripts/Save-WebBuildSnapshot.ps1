[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BucketName,
    [Parameter(Mandatory)][string]$RollbackBucketName,
    [Parameter(Mandatory)][string]$ReleaseId,
    [string]$Region = "ap-northeast-1"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/WebBuildAssetReferences.ps1"

if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw "ReleaseId may contain only letters, numbers, dots, underscores, and hyphens."
}

function Invoke-CheckedAws {
    param([string[]]$Arguments)

    $result = & aws @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "AWS command failed: aws $($Arguments -join ' ')"
    }

    return ($result -join "`n")
}

$sourceIndex = Invoke-CheckedAws -Arguments @(
    "s3", "cp", "s3://$BucketName/index.html", "-",
    "--region", $Region, "--only-show-errors")
$assetKeys = @(Get-ReferencedBuildKeys -IndexHtml $sourceIndex -Label "Live index.html")

$existing = Invoke-CheckedAws -Arguments @(
    "s3api", "list-objects-v2", "--bucket", $RollbackBucketName,
    "--prefix", "$ReleaseId/", "--max-keys", "1",
    "--query", "KeyCount", "--output", "text", "--region", $Region)
if ($existing.Trim() -ne "0") {
    throw "Rollback snapshot ID is not empty: $ReleaseId"
}

# Fail before writing anything if a release references a missing WebGL asset.
foreach ($key in $assetKeys) {
    [void](Invoke-CheckedAws -Arguments @(
        "s3api", "head-object", "--bucket", $BucketName,
        "--key", $key, "--output", "json", "--region", $Region))
}

$destination = "s3://$RollbackBucketName/$ReleaseId"
Write-Host "Saving minimal rollback snapshot $ReleaseId"

# Keep support files such as TemplateData, but never copy stale Build files.
# The snapshot's index is published last so incomplete snapshots are not usable.
[void](Invoke-CheckedAws -Arguments @(
    "s3", "sync", "s3://$BucketName", $destination,
    "--exclude", "Build/*", "--exclude", "index.html",
    "--region", $Region, "--only-show-errors"))

foreach ($key in $assetKeys) {
    [void](Invoke-CheckedAws -Arguments @(
        "s3", "cp", "s3://$BucketName/$key", "$destination/$key",
        "--region", $Region, "--only-show-errors"))
}

$latestIndex = Invoke-CheckedAws -Arguments @(
    "s3", "cp", "s3://$BucketName/index.html", "-",
    "--region", $Region, "--only-show-errors")
if ($latestIndex -cne $sourceIndex) {
    throw "Live index.html changed while saving snapshot $ReleaseId; index was not copied."
}

[void](Invoke-CheckedAws -Arguments @(
    "s3", "cp", "s3://$BucketName/index.html", "$destination/index.html",
    "--region", $Region, "--only-show-errors"))

Write-Host "Saved rollback snapshot $ReleaseId with $($assetKeys.Count) referenced WebGL assets."
