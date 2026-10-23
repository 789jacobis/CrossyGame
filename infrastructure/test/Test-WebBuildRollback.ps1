$ErrorActionPreference = "Stop"

$hash = "a" * 64
$assetKeys = @(
    "Build/Release.loader.$hash.js",
    "Build/Release.data.$hash.br",
    "Build/Release.framework.js.$hash.br",
    "Build/Release.wasm.$hash.br"
)
$global:RollbackMockIndex = ($assetKeys | ForEach-Object {
    'buildUrl + "/' + $_.Substring(6) + '"'
}) -join "`n"
$global:RollbackMockCalls = @()
$global:RollbackMockMissingKey = ""
$global:RollbackMockIncompleteLatest = $false

function aws {
    $tokens = @($args)
    $global:RollbackMockCalls += ,$tokens
    $global:LASTEXITCODE = 0

    if ($tokens[0] -eq "s3api" -and $tokens[1] -eq "list-objects-v2") {
        if (@($tokens | Where-Object { $_ -eq "CommonPrefixes[].Prefix" }).Count) {
            return "z-incomplete/ a-complete/"
        }
        return "0"
    }
    if ($tokens[0] -eq "s3api" -and $tokens[1] -eq "head-object") {
        $key = $tokens[([array]::IndexOf($tokens, "--key") + 1)]
        if ($key -eq $global:RollbackMockMissingKey -or
            ($global:RollbackMockIncompleteLatest -and
                $key -eq "z-incomplete/index.html")) {
            $global:LASTEXITCODE = 1
            return
        }
        return "{}"
    }
    if ($tokens[0] -eq "s3" -and $tokens[1] -eq "cp") {
        if ($tokens[3] -eq "-") {
            return $global:RollbackMockIndex
        }
        if ($tokens[3] -like "*.html" -and
            $tokens[3] -notlike "s3://*") {
            [System.IO.File]::WriteAllText($tokens[3], $global:RollbackMockIndex)
        }
    }
    if ($tokens[0] -eq "cloudfront" -and $tokens[1] -eq "create-invalidation") {
        return "test-invalidation"
    }
}

function Invoke-Rollback {
    param([string]$ReleaseId = "target-release")

    & "$PSScriptRoot/../scripts/Rollback-WebBuild.ps1" `
        -ReleaseId $ReleaseId `
        -BucketName "live-test" `
        -RollbackBucketName "archives-test" `
        -DistributionId "distribution-test" `
        -GameUrl "https://example.invalid"
}

function Get-LiveWrites {
    return @($global:RollbackMockCalls | Where-Object {
        $_[0] -eq "s3" -and
        ($_[1] -eq "sync" -or $_[1] -eq "cp") -and
        $_[3] -like "s3://live-test*"
    })
}

try {
    Invoke-Rollback | Out-Null
    $writes = @(Get-LiveWrites)
    if ($writes.Count -ne 6 -or $writes[0][1] -ne "sync" -or
        @($writes[0] | Where-Object { $_ -eq "Build/*" }).Count -ne 1 -or
        @($writes[0] | Where-Object { $_ -eq "index.html" }).Count -ne 1) {
        throw "Rollback did not restore support files separately from Build and index.html."
    }
    for ($i = 0; $i -lt 4; $i++) {
        if ($writes[$i + 1][2] -ne
                "s3://archives-test/target-release/$($assetKeys[$i])" -or
            $writes[$i + 1][3] -ne "s3://live-test/$($assetKeys[$i])") {
            throw "Rollback did not force-copy the exact referenced asset $i."
        }
    }
    if ($writes[5][2] -ne "s3://archives-test/target-release/index.html" -or
        $writes[5][3] -ne "s3://live-test/index.html") {
        throw "Rollback did not restore index.html after its assets."
    }

    $global:RollbackMockCalls = @()
    $global:RollbackMockMissingKey = "target-release/$($assetKeys[0])"
    $rejected = $false
    try { Invoke-Rollback | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected -or @(Get-LiveWrites).Count -ne 0) {
        throw "Incomplete rollback snapshot changed the live release."
    }

    $global:RollbackMockCalls = @()
    $global:RollbackMockMissingKey = ""
    $global:RollbackMockIncompleteLatest = $true
    Invoke-Rollback -ReleaseId "" | Out-Null
    $writes = @(Get-LiveWrites)
    if ($writes[5][2] -ne "s3://archives-test/a-complete/index.html") {
        throw "Automatic rollback selected an incomplete snapshot."
    }

    Write-Host "WebGL rollback tests passed."
}
finally {
    Remove-Variable -Name RollbackMockIndex, RollbackMockCalls, `
        RollbackMockMissingKey, RollbackMockIncompleteLatest `
        -Scope Global -ErrorAction SilentlyContinue
}
