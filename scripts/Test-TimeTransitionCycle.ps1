[CmdletBinding()]
param(
    [ValidateRange(60, 600)][double]$Speed = 600,
    [ValidateRange(1, 7)][int]$Days = 2,
    [int]$WeatherCode = 1,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\time-transition-cycle.json')
)
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'Invoke-WeatherDebug.ps1'
$samples = [System.Collections.Generic.List[object]]::new()
$edges = @{}
$report = [ordered]@{pass=$false;days=$Days;speed=$Speed;weather=$WeatherCode}
$gameDir = (& dotnet msbuild (Join-Path $PSScriptRoot '..\RealTimeWeatherMod\RealTimeWeather.csproj') -getProperty:GameDir).Trim()
$triggerPath = Join-Path $gameDir 'BepInEx\config\chillwithyou.envsync.triggerstate'
$triggerBefore = if (Test-Path -LiteralPath $triggerPath) { (Get-FileHash -LiteralPath $triggerPath).Hash } else { $null }
function Read-Resources {
    $code = '"{\"materials\":"+UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>().Length+",\"transientMaterials\":"+UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Material>().Count(m=>m.hideFlags==UnityEngine.HideFlags.HideAndDontSave)+",\"profiles\":"+UnityEngine.Resources.FindObjectsOfTypeAll(System.Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime")).Length+",\"managedBytes\":"+System.GC.GetTotalMemory(false)+"}"'
    $rpc=@{jsonrpc='2.0';id=65;method='tools/call';params=@{name='execute_code';arguments=@{code=$code}}}|ConvertTo-Json -Depth 8 -Compress
    $response=Invoke-RestMethod -Uri 'http://localhost:8972/mcp' -Method Post -ContentType 'application/json' -Headers @{Accept='application/json, text/event-stream';'MCP-Protocol-Version'='2025-11-25'} -Body $rpc -TimeoutSec 15
    (($response.result.content.text | ConvertFrom-Json).result | ConvertFrom-Json)
}
try {
    $ready = & $helper -Action snapshot
    if (-not $ready.runtimeReady -or $ready.inCutscene) { throw 'Game is not ready for an uninterrupted cycle.' }
    # Resource discovery happens outside the timed playback, avoiding diagnostic spikes.
    $report.before = Read-Resources
    & $helper -Action test_cycle -Days $Days -WeatherCode $WeatherCode -Speed $Speed | Out-Null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 150
        $sample = & $helper -Action snapshot
        $samples.Add($sample)
        if (-not $sample.testCycle) { throw 'Cycle playback was replaced or stopped.' }
        if ($sample.transitionMode -eq 'unavailable') { throw $sample.transitionUnavailableReason }
        if ($sample.inCutscene -or $sample.whaleOverrideActive) { throw 'Game ownership interrupted the cycle.' }
        if ($timer.Elapsed.TotalSeconds -gt (86400 * $Days / $Speed + 20)) { throw 'Cycle timed out.' }
        if ($sample.transitionMode -eq 'scheduled' -and $sample.transitionProgress -gt 0 -and $sample.transitionProgress -lt 1) {
            $key = $sample.effectiveDateTime.Substring(0,10)+' '+$sample.transitionFrom+'->'+$sample.transitionTo
            if (-not $edges.ContainsKey($key)) {
                $edges[$key] = [ordered]@{samples=0;last=0.0;monotonic=$true}
                Write-Output "Blending: $key"
            }
            $edge = $edges[$key]
            if ($sample.transitionProgress -lt $edge.last) { $edge.monotonic = $false }
            $edge.samples++
            $edge.last = $sample.transitionProgress
        }
    } until ($sample.testComplete)
    $report.final = $sample
    $report.edges = $edges
    $report.pass = $edges.Count -eq 3*$Days -and @($edges.Values | Where-Object { -not $_.monotonic -or $_.samples -lt 2 }).Count -eq 0 -and $sample.matchesExpected -and $sample.transitionMode -eq 'settled'
    if (-not $report.pass) { throw 'Not all expected day/sunset/night boundaries completed.' }
} catch {
    $report.error = $_.Exception.Message
    throw
} finally {
    & $helper -Action test_stop | Out-Null
    Start-Sleep -Seconds 3
    $report.after = Read-Resources
    $triggerAfter = if (Test-Path -LiteralPath $triggerPath) { (Get-FileHash -LiteralPath $triggerPath).Hash } else { $null }
    $report.dailyStatePreserved = $triggerBefore -eq $triggerAfter
    $report.pass = $report.pass -and $report.dailyStatePreserved
    $report.samples = $samples
    $report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    [pscustomobject]@{pass=$report.pass;dailyStatePreserved=$report.dailyStatePreserved;boundaries=$edges.Count;before=$report.before;after=$report.after;final=$report.final;error=$report.error} | ConvertTo-Json -Depth 4 -Compress
}
if (-not $report.pass) { throw 'Cycle acceptance checks failed; see the saved report.' }
