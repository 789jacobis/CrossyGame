[CmdletBinding()]
param(
    [string]$BucketName,
    [string]$RollbackBucketName,
    [string]$StackName = "CrossyRoadWebStack-production",
    [string]$Region = "ap-northeast-1",
    [string]$InventoryJson
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/WebBuildAssetReferences.ps1"

function Invoke-AwsJson {
    param([string[]]$Arguments)

    $result = & aws @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Read-only AWS command failed: aws $($Arguments -join ' ')"
    }

    return (($result -join "`n") | ConvertFrom-Json -AsHashtable)
}

function Invoke-AwsText {
    param([string[]]$Arguments)

    $result = & aws @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Read-only AWS command failed: aws $($Arguments -join ' ')"
    }

    return ($result -join "`n")
}

function Get-BuildObjectMap {
    param(
        [object[]]$Objects,
        [string]$Label,
        [string]$Prefix = ""
    )

    $map = [System.Collections.Generic.Dictionary[string, long]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($item in $Objects) {
        if ($null -eq $item -or [string]::IsNullOrWhiteSpace($item.Key) -or
            -not $item.Contains('Size')) {
            throw "$Label contains an object without a key or size."
        }

        $key = [string]$item.Key
        if (-not $key.StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
            throw "$Label contains an unexpected key: $key"
        }

        $size = [long]$item.Size
        if ($size -lt 0 -or -not $map.TryAdd($key, $size)) {
            throw "$Label contains an invalid or duplicate object: $key"
        }
    }

    return $map
}

if ($InventoryJson) {
    $inventory = $InventoryJson | ConvertFrom-Json -AsHashtable
}
else {
    if (-not $BucketName -or -not $RollbackBucketName) {
        $stack = Invoke-AwsJson -Arguments @(
            "cloudformation", "describe-stacks", "--stack-name", $StackName,
            "--region", $Region, "--output", "json")
        if (@($stack.Stacks).Count -ne 1) {
            throw "Expected one Web stack: $StackName"
        }

        foreach ($output in $stack.Stacks[0].Outputs) {
            if ($output.OutputKey -eq "WebBucketName") {
                $BucketName = $output.OutputValue
            }
            elseif ($output.OutputKey -eq "WebRollbackBucketName") {
                $RollbackBucketName = $output.OutputValue
            }
        }
    }

    if (-not $BucketName -or -not $RollbackBucketName) {
        throw "Web and rollback bucket names are required."
    }

    $liveIndex = Invoke-AwsText -Arguments @(
        "s3", "cp", "s3://$BucketName/index.html", "-",
        "--region", $Region, "--only-show-errors")
    $liveListing = Invoke-AwsJson -Arguments @(
        "s3api", "list-objects-v2", "--bucket", $BucketName,
        "--prefix", "Build/", "--region", $Region, "--output", "json")
    $snapshotListing = Invoke-AwsJson -Arguments @(
        "s3api", "list-objects-v2", "--bucket", $RollbackBucketName,
        "--delimiter", "/", "--region", $Region, "--output", "json")

    $snapshots = @()
    foreach ($entry in @($snapshotListing.CommonPrefixes)) {
        if ($null -eq $entry -or $entry.Prefix -notmatch '^[A-Za-z0-9._-]+/$') {
            throw "Unexpected rollback snapshot prefix. Refusing to classify assets."
        }

        $id = $entry.Prefix.TrimEnd('/')
        $snapshotIndex = Invoke-AwsText -Arguments @(
            "s3", "cp", "s3://$RollbackBucketName/$id/index.html", "-",
            "--region", $Region, "--only-show-errors")
        $snapshotObjects = Invoke-AwsJson -Arguments @(
            "s3api", "list-objects-v2", "--bucket", $RollbackBucketName,
            "--prefix", "$id/Build/", "--region", $Region, "--output", "json")

        $snapshots += [ordered]@{
            Id = $id
            IndexHtml = $snapshotIndex
            Objects = @($snapshotObjects.Contents)
        }
    }

    $inventory = [ordered]@{
        LiveIndexHtml = $liveIndex
        LiveObjects = @($liveListing.Contents)
        Snapshots = $snapshots
    }
}

if ($null -eq $inventory -or -not $inventory.Contains('LiveIndexHtml') -or
    -not $inventory.Contains('LiveObjects') -or -not $inventory.Contains('Snapshots')) {
    throw "Cleanup inventory is incomplete."
}

$liveObjects = Get-BuildObjectMap -Objects @($inventory.LiveObjects) -Label "Live bucket" -Prefix "Build/"
$liveKeys = @(Get-ReferencedBuildKeys -IndexHtml $inventory.LiveIndexHtml -Label "Live index.html")
$protected = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)

foreach ($key in $liveKeys) {
    if (-not $liveObjects.ContainsKey($key)) {
        throw "Live index.html references a missing live object: $key"
    }
    [void]$protected.Add($key)
}

$snapshotIds = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($snapshot in @($inventory.Snapshots)) {
    if ($null -eq $snapshot -or $snapshot.Id -notmatch '^[A-Za-z0-9._-]+$' -or
        -not $snapshotIds.Add([string]$snapshot.Id)) {
        throw "Cleanup inventory contains an invalid or duplicate snapshot ID."
    }

    $snapshotPrefix = "$($snapshot.Id)/"
    $snapshotObjects = Get-BuildObjectMap `
        -Objects @($snapshot.Objects) `
        -Label "Snapshot $($snapshot.Id)" `
        -Prefix "${snapshotPrefix}Build/"
    $snapshotKeys = @(Get-ReferencedBuildKeys `
        -IndexHtml $snapshot.IndexHtml `
        -Label "Snapshot $($snapshot.Id) index.html")

    foreach ($key in $snapshotKeys) {
        if (-not $snapshotObjects.ContainsKey("$snapshotPrefix$key")) {
            throw "Snapshot $($snapshot.Id) references a missing object: $key"
        }
        [void]$protected.Add($key)
    }
}

$assetPattern = '^Build/Release\.(?:loader(?:\.[a-f0-9]{64})?\.js|data(?:\.[a-f0-9]{64})?\.br|framework\.js(?:\.[a-f0-9]{64})?\.br|wasm(?:\.[a-f0-9]{64})?\.br)$'
$candidates = @()
$ignored = @()
$candidateBytes = [long]0
foreach ($key in @($liveObjects.Keys | Sort-Object)) {
    if ($key -notmatch $assetPattern) {
        $ignored += $key
    }
    elseif (-not $protected.Contains($key)) {
        $candidates += [ordered]@{
            Key = $key
            Bytes = $liveObjects[$key]
            Status = "unreferenced-now; not approved for deletion"
        }
        $candidateBytes += $liveObjects[$key]
    }
}

$report = [ordered]@{
    Mode = "read-only dry-run"
    LiveAssetKeys = $liveKeys
    SnapshotCount = $snapshotIds.Count
    ProtectedBuildKeys = @($protected | Sort-Object)
    UnreferencedCandidates = $candidates
    UnreferencedBytes = $candidateBytes
    IgnoredBuildKeys = $ignored
    Note = "Candidates require a separate 30-day unreferenced observation and manual review before any deletion."
}

$report | ConvertTo-Json -Depth 8
