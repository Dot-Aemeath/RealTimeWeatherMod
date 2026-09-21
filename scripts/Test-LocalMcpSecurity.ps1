[CmdletBinding()]
param([int]$Port = 8972)
$ErrorActionPreference = 'Stop'
function Send-Request([string]$Headers, [string]$Body = '{}') {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $Port)
        $client.ReceiveTimeout = 3000
        $stream = $client.GetStream()
        $bytes = [Text.Encoding]::UTF8.GetBytes("POST /mcp HTTP/1.1`r`n$Headers`r`n`r`n$Body")
        $stream.Write($bytes, 0, $bytes.Length)
        $reader = [IO.StreamReader]::new($stream)
        $reader.ReadLine()
    } finally { $client.Dispose() }
}
$normal = "Host: 127.0.0.1:$Port`r`nContent-Type: application/json`r`nAccept: application/json, text/event-stream`r`nContent-Length: 2"
$cases = @(
    @{Name='Remote host'; Headers=$normal.Replace("127.0.0.1:$Port", "untrusted.invalid:$Port"); Status=403},
    @{Name='Browser origin'; Headers="$normal`r`nOrigin: https://untrusted.invalid"; Status=403},
    @{Name='Null origin'; Headers="$normal`r`nOrigin: null"; Status=403},
    @{Name='Browser fetch metadata'; Headers="$normal`r`nSec-Fetch-Site: same-origin"; Status=403},
    @{Name='Oversized request'; Headers=$normal.Replace('Content-Length: 2','Content-Length: 1048577'); Status=400},
    @{Name='Duplicate host'; Headers="$normal`r`nHost: untrusted.invalid"; Status=403}
)
foreach ($case in $cases) {
    $status = Send-Request $case.Headers
    if ($status -notmatch "^HTTP/1.1 $($case.Status) ") { throw "$($case.Name): $status" }
    [pscustomobject]@{check=$case.Name;pass=$true;status=$case.Status}
}
# Occupy every slot with incomplete requests, then check the read deadline and recovery.
$held = @()
try {
    for ($i=0; $i -lt 8; $i++) {
        $client = [Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
        $held += $client
        $bytes = [Text.Encoding]::ASCII.GetBytes('POST /mcp HTTP/1.1')
        $client.GetStream().Write($bytes, 0, $bytes.Length)
    }
    $extra = [Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
    try {
        $extra.ReceiveTimeout = 3000
        if ($extra.GetStream().ReadByte() -ne -1) { throw 'Excess connection was not closed.' }
    } finally { $extra.Dispose() }
    Start-Sleep -Seconds 16
    foreach ($client in $held) {
        $client.ReceiveTimeout = 1000
        if ($client.GetStream().ReadByte() -ne -1) { throw 'Read deadline did not close connection.' }
    }
    $status = Send-Request "$normal`r`nOrigin: null"
    if ($status -notmatch '^HTTP/1.1 403 ') { throw "Server did not recover: $status" }
    [pscustomobject]@{check='Connection cap, read deadline and recovery';pass=$true;status=403}
} finally { foreach ($client in $held) { $client.Dispose() } }
