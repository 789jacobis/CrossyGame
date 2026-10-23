function Get-ReferencedBuildKeys {
    param(
        [string]$IndexHtml,
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($IndexHtml)) {
        throw "$Label has an empty index.html. Refusing to classify assets."
    }

    $patterns = @(
        '/(?<asset>Release\.loader(?:\.[a-f0-9]{64})?\.js)(?![A-Za-z0-9.])',
        '/(?<asset>Release\.data(?:\.[a-f0-9]{64})?\.br)(?![A-Za-z0-9.])',
        '/(?<asset>Release\.framework\.js(?:\.[a-f0-9]{64})?\.br)(?![A-Za-z0-9.])',
        '/(?<asset>Release\.wasm(?:\.[a-f0-9]{64})?\.br)(?![A-Za-z0-9.])'
    )

    $keys = @()
    foreach ($pattern in $patterns) {
        $matches = [regex]::Matches($IndexHtml, $pattern)
        if ($matches.Count -ne 1) {
            throw "$Label must reference exactly one asset for pattern $pattern; found $($matches.Count)."
        }

        $keys += "Build/$($matches[0].Groups['asset'].Value)"
    }

    return $keys
}
