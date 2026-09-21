[CmdletBinding()]
param(
    [ValidateRange(1, 60)][double]$Speed = 20,
    [ValidateSet('dawn', 'sunset', 'night')][string[]]$Boundary = @('dawn', 'sunset', 'night'),
    [int]$WeatherCode = 1,
    [switch]$ExpectSteady,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\time-transition-runs.json')
)

$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'Invoke-WeatherDebug.ps1'
$runs = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($edge in $Boundary) {
        $samples = [System.Collections.Generic.List[object]]::new()
        $started = & $helper -Action test_start -Boundary $edge -WeatherCode $WeatherCode -Speed $Speed
        if (-not $started.success -or $started.testWindowDurationSeconds -le 0) { throw 'Playback did not return a valid window duration.' }
        $timeoutSeconds = ($started.testWindowDurationSeconds + 30) / $Speed + 20
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 500
            $sample = & $helper -Action snapshot
            $samples.Add($sample)
            if ($sample.transitionMode -eq 'unavailable') { throw $sample.transitionUnavailableReason }
            if ($sample.inCutscene -or $sample.whaleOverrideActive) { throw 'The run was interrupted by game ownership.' }
            if ($timer.Elapsed.TotalSeconds -gt $timeoutSeconds) { throw 'Playback timed out.' }
        } until ($sample.testComplete)
        $blending = @($samples | Where-Object { $_.transitionMode -eq 'scheduled' -and $_.transitionProgress -gt 0 -and $_.transitionProgress -lt 1 })
        $monotonic = $true
        for ($i = 1; $i -lt $blending.Count; $i++) {
            if ($blending[$i].transitionProgress -lt $blending[$i-1].transitionProgress) { $monotonic = $false }
        }
        $presentationPass = $blending.Count -ge 3 -and $monotonic
        if ($ExpectSteady) {
            # Explicit caller expectation, not a second copy of weather mapping rules.
            $window = @($samples | Where-Object { $_.testWindowProgress -gt 0 -and $_.testWindowProgress -lt 1 })
            $presentationPass = $window.Count -ge 3 -and $blending.Count -eq 0 -and
                @($window.expectedBase | Select-Object -Unique).Count -eq 1 -and
                @($window | Where-Object { -not $_.matchesExpected -or $_.transitionMode -ne 'settled' }).Count -eq 0
        }
        $pass = $presentationPass -and $sample.matchesExpected -and $sample.transitionMode -eq 'settled'
        $run = [pscustomobject]@{boundary=$edge;speed=$Speed;expectSteady=$ExpectSteady.IsPresent;pass=$pass;monotonic=$monotonic;blendingSamples=$blending.Count;final=$sample;samples=$samples}
        $runs.Add($run)
        [pscustomobject]@{boundary=$edge;pass=$pass;blendingSamples=$blending.Count;averageMs=$sample.testAverageFrameMs;p95Ms=$sample.testP95FrameMs;maxMs=$sample.testMaxFrameMs;over50ms=$sample.testFramesOver50ms;gen0=$sample.testGen0Collections} | ConvertTo-Json -Compress
        if (-not $pass) { throw "Transition assertions failed: $edge" }
    }
} finally {
    & $helper -Action test_stop | Out-Null
    $runs | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
