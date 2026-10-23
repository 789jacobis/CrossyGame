$ErrorActionPreference = "Stop"

$hash = "a" * 64
$assetKeys = @(
    "Build/Release.loader.$hash.js",
    "Build/Release.data.$hash.br",
    "Build/Release.framework.js.$hash.br",
    "Build/Release.wasm.$hash.br"
)
$global:SnapshotMockIndex = ($assetKeys | ForEach-Object {
    'buildUrl + "/' + $_.Substring(6) + '"'
}) -join "`n"
$global:SnapshotMockCalls = @()
$global:SnapshotMockExisting = "0"
$global:SnapshotMockMissingAsset = ""
$global:SnapshotMockChangeIndex = $false
$global:SnapshotMockIndexReads = 0

function aws {
    $tokens = @($args)
    $global:SnapshotMockCalls += ,$tokens
    $global:LASTEXITCODE = 0

    if ($tokens[0] -eq "s3api" -and $tokens[1] -eq "list-objects-v2") {
        return $global:SnapshotMockExisting
    }
    if ($tokens[0] -eq "s3api" -and $tokens[1] -eq "head-object") {
        if ($global:SnapshotMockMissingAsset -and
            $tokens[([array]::IndexOf($tokens, "--key") + 1)] -eq
                $global:SnapshotMockMissingAsset) {
            $global:LASTEXITCODE = 1
        }
        return "{}"
    }
    if ($tokens[0] -eq "s3" -and $tokens[1] -eq "cp" -and
        $tokens[3] -eq "-") {
        $global:SnapshotMockIndexReads++
        if ($global:SnapshotMockChangeIndex -and
            $global:SnapshotMockIndexReads -eq 2) {
            return ($global:SnapshotMockIndex + " changed")
        }
        return $global:SnapshotMockIndex
    }
}

function Reset-Mock {
    $global:SnapshotMockCalls = @()
    $global:SnapshotMockExisting = "0"
    $global:SnapshotMockMissingAsset = ""
    $global:SnapshotMockChangeIndex = $false
    $global:SnapshotMockIndexReads = 0
}

function Invoke-Snapshot {
    & "$PSScriptRoot/../scripts/Save-WebBuildSnapshot.ps1" `
        -BucketName "live-test" `
        -RollbackBucketName "archives-test" `
        -ReleaseId "test-release"
}

function Get-MutationCalls {
    return @($global:SnapshotMockCalls | Where-Object {
        $_[0] -eq "s3" -and ($_[1] -eq "sync" -or
            ($_[1] -eq "cp" -and $_[3] -ne "-"))
    })
}

function Assert-Rejected {
    param([string]$Reason)

    $rejected = $false
    try { Invoke-Snapshot | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) {
        throw "Unsafe snapshot was accepted: $Reason"
    }
}

try {
    Invoke-Snapshot | Out-Null
    $mutations = @(Get-MutationCalls)
    if ($mutations.Count -ne 6) {
        throw "Expected one static sync, four asset copies, and one index copy; got $($mutations.Count)."
    }
    $staticSync = $mutations[0]
    if ($staticSync[1] -ne "sync" -or
        @($staticSync | Where-Object { $_ -eq "Build/*" }).Count -ne 1 -or
        @($staticSync | Where-Object { $_ -eq "index.html" }).Count -ne 1) {
        throw "Static sync must exclude all Build files and index.html."
    }
    for ($i = 0; $i -lt 4; $i++) {
        if ($mutations[$i + 1][2] -ne "s3://live-test/$($assetKeys[$i])" -or
            $mutations[$i + 1][3] -ne
                "s3://archives-test/test-release/$($assetKeys[$i])") {
            throw "Snapshot did not copy the exact referenced asset $i."
        }
    }
    if ($mutations[5][2] -ne "s3://live-test/index.html" -or
        $mutations[5][3] -ne "s3://archives-test/test-release/index.html") {
        throw "Snapshot index.html was not copied last."
    }

    Reset-Mock
    $global:SnapshotMockExisting = "1"
    Assert-Rejected -Reason "existing snapshot ID"
    if (@(Get-MutationCalls).Count -ne 0) {
        throw "Existing snapshot ID caused writes."
    }

    Reset-Mock
    $global:SnapshotMockMissingAsset = $assetKeys[0]
    Assert-Rejected -Reason "missing live asset"
    if (@(Get-MutationCalls).Count -ne 0) {
        throw "Missing live asset caused writes."
    }

    Reset-Mock
    $global:SnapshotMockIndex = $global:SnapshotMockIndex.Replace(
        "/$($assetKeys[3].Substring(6))", "/missing.wasm.br")
    Assert-Rejected -Reason "malformed live index"
    if (@(Get-MutationCalls).Count -ne 0) {
        throw "Malformed live index caused writes."
    }

    Reset-Mock
    $global:SnapshotMockIndex = ($assetKeys | ForEach-Object {
        'buildUrl + "/' + $_.Substring(6) + '"'
    }) -join "`n"
    $global:SnapshotMockChangeIndex = $true
    Assert-Rejected -Reason "index changed during snapshot"
    if (@(Get-MutationCalls).Count -ne 5) {
        throw "Changing live index should prevent the final index copy."
    }

    Write-Host "Minimal WebGL rollback snapshot tests passed."
}
finally {
    Remove-Variable -Name SnapshotMockIndex, SnapshotMockCalls, `
        SnapshotMockExisting, SnapshotMockMissingAsset, `
        SnapshotMockChangeIndex, SnapshotMockIndexReads `
        -Scope Global -ErrorAction SilentlyContinue
}
