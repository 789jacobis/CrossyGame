$ErrorActionPreference = "Stop"

$inspector = Join-Path $PSScriptRoot "../scripts/Inspect-WebBuildCleanup.ps1"
$hashA = "a" * 64
$hashB = "b" * 64
$hashC = "c" * 64

function New-Index {
    param([string[]]$Keys)

    return (($Keys | ForEach-Object {
        'buildUrl + "/' + $_.Substring("Build/".Length) + '"'
    }) -join "`n")
}

function New-Object {
    param([string]$Key, [long]$Size = 10)

    return @{ Key = $Key; Size = $Size }
}

function Invoke-Inspection {
    param([hashtable]$Inventory)

    $json = $Inventory | ConvertTo-Json -Depth 10 -Compress
    return (& $inspector -InventoryJson $json | ConvertFrom-Json -AsHashtable)
}

function Assert-Rejected {
    param([hashtable]$Inventory, [string]$Reason)

    $rejected = $false
    try {
        [void](Invoke-Inspection -Inventory $Inventory)
    }
    catch {
        $rejected = $true
    }

    if (-not $rejected) {
        throw "Unsafe cleanup inventory was accepted: $Reason"
    }
}

function Copy-Inventory {
    param([hashtable]$Inventory)

    return ($Inventory | ConvertTo-Json -Depth 10 -Compress |
        ConvertFrom-Json -AsHashtable)
}

$liveKeys = @(
    "Build/Release.loader.$hashA.js",
    "Build/Release.data.$hashA.br",
    "Build/Release.framework.js.$hashA.br",
    "Build/Release.wasm.$hashA.br"
)
$legacyKeys = @(
    "Build/Release.loader.js",
    "Build/Release.data.br",
    "Build/Release.framework.js.br",
    "Build/Release.wasm.br"
)
$previousData = "Build/Release.data.$hashB.br"
$orphan = "Build/Release.data.$hashC.br"
$snapshotOne = "20261021T120347Z-old"
$snapshotTwo = "20261023T104641Z-previous"
$snapshotTwoKeys = @($liveKeys[0], $previousData, $liveKeys[2], $liveKeys[3])

$inventory = @{
    LiveIndexHtml = New-Index -Keys $liveKeys
    LiveObjects = @(
        $liveKeys | ForEach-Object { New-Object -Key $_ }
    ) + @(
        $legacyKeys | ForEach-Object { New-Object -Key $_ }
    ) + @(
        New-Object -Key $previousData
        New-Object -Key $orphan -Size 123
        New-Object -Key "Build/notes.txt"
    )
    Snapshots = @(
        @{
            Id = $snapshotOne
            IndexHtml = New-Index -Keys $legacyKeys
            Objects = @($legacyKeys | ForEach-Object {
                New-Object -Key "$snapshotOne/$_"
            })
        },
        @{
            Id = $snapshotTwo
            IndexHtml = New-Index -Keys $snapshotTwoKeys
            Objects = @($snapshotTwoKeys | ForEach-Object {
                New-Object -Key "$snapshotTwo/$_"
            })
        }
    )
}

$report = Invoke-Inspection -Inventory $inventory
if ($report.Mode -ne "read-only dry-run" -or
    $report.SnapshotCount -ne 2 -or
    @($report.UnreferencedCandidates).Count -ne 1 -or
    $report.UnreferencedCandidates[0].Key -ne $orphan -or
    $report.UnreferencedBytes -ne 123 -or
    @($report.ProtectedBuildKeys).Count -ne 9 -or
    @($report.IgnoredBuildKeys).Count -ne 1) {
    throw "Cleanup dry-run did not distinguish live, rollback, orphan, and ignored files: $($report | ConvertTo-Json -Depth 8 -Compress)"
}

$cacheBustedLegacy = Copy-Inventory $inventory
$cacheBustedLegacy.Snapshots[0].IndexHtml =
    $cacheBustedLegacy.Snapshots[0].IndexHtml.Replace('"', '?v=rollback-test"')
$cacheBustedReport = Invoke-Inspection -Inventory $cacheBustedLegacy
if (@($cacheBustedReport.UnreferencedCandidates).Count -ne 1) {
    throw "Legacy snapshot URLs with cache-busting queries were not protected."
}

$missingLive = Copy-Inventory $inventory
$missingLive.LiveObjects = @($missingLive.LiveObjects | Where-Object {
    $_.Key -ne $liveKeys[0]
})
Assert-Rejected -Inventory $missingLive -Reason "missing live asset"

$missingSize = Copy-Inventory $inventory
$missingSize.LiveObjects[0].Remove('Size')
Assert-Rejected -Inventory $missingSize -Reason "missing object size"

$missingSnapshotAsset = Copy-Inventory $inventory
$missingSnapshotAsset.Snapshots[0].Objects = @(
    $missingSnapshotAsset.Snapshots[0].Objects | Where-Object {
        $_.Key -ne "$snapshotOne/$($legacyKeys[0])"
    })
Assert-Rejected -Inventory $missingSnapshotAsset -Reason "incomplete rollback snapshot"

$missingReference = Copy-Inventory $inventory
$missingReference.LiveIndexHtml = $missingReference.LiveIndexHtml.Replace(
    "/$($liveKeys[3].Substring(6))", "/missing.wasm.br")
Assert-Rejected -Inventory $missingReference -Reason "missing index reference"

$duplicateReference = Copy-Inventory $inventory
$duplicateReference.LiveIndexHtml += "`n" +
    ('buildUrl + "/' + $liveKeys[0].Substring(6) + '"')
Assert-Rejected -Inventory $duplicateReference -Reason "duplicate index reference"

$invalidSnapshotId = Copy-Inventory $inventory
$invalidSnapshotId.Snapshots[0].Id = "../unexpected"
Assert-Rejected -Inventory $invalidSnapshotId -Reason "invalid snapshot ID"

Write-Host "WebGL cleanup dry-run tests passed."
