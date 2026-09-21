[CmdletBinding()]
param(
    [ValidateSet('snapshot', 'test_start', 'test_cycle', 'test_pause', 'test_resume', 'test_speed', 'test_seek', 'test_stop')]
    [string]$Action = 'snapshot',
    [ValidateSet('dawn', 'sunset', 'night')]
    [string]$Boundary = 'sunset',
    [int]$WeatherCode = 1,
    [ValidateRange(1, 7)]
    [int]$Days = 2,
    [ValidateRange(0, 600)]
    [double]$Speed = 20,
    [ValidateRange(0, 1)]
    [double]$Progress = 0.5,
    [switch]$Discrete
)

$ErrorActionPreference = 'Stop'
$arguments = @{ action = $Action }
switch ($Action) {
    'test_start' {
        $arguments.boundary = $Boundary
        $arguments.weatherCode = $WeatherCode
        $arguments.speed = $Speed
        $arguments.smoothTransitions = -not $Discrete.IsPresent
    }
    'test_speed' { $arguments.speed = $Speed }
    'test_cycle' {
        $arguments.days = $Days
        $arguments.weatherCode = $WeatherCode
        $arguments.speed = $Speed
        $arguments.smoothTransitions = -not $Discrete.IsPresent
    }
    'test_seek' { $arguments.progress = $Progress }
}
$request = @{
    jsonrpc = '2.0'
    id = 1
    method = 'tools/call'
    params = @{ name = 'envsync_debug'; arguments = $arguments }
} | ConvertTo-Json -Depth 8 -Compress
$response = Invoke-RestMethod -Uri 'http://localhost:8972/mcp' -Method Post `
    -ContentType 'application/json' -Headers @{
        Accept = 'application/json, text/event-stream'
        'MCP-Protocol-Version' = '2025-11-25'
    } -Body $request -TimeoutSec 15
if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
$content = $response.result.content | Where-Object { $_.type -eq 'text' } | Select-Object -First 1
if (-not $content) { throw 'The MCP server returned no debug snapshot.' }
$snapshot = $content.text | ConvertFrom-Json
if ($content.isError -or -not $snapshot.success) { throw $content.text }
$snapshot
