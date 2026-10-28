# Run from PowerShell after Go is available in PATH. Leaves the acceptance issue
# in data/issues.json, and always stops only the processes started by this script.
param([int]$Port = 8080)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$verifyDir = Join-Path $PSScriptRoot '.verify'
New-Item -ItemType Directory -Path $verifyDir -Force | Out-Null
$exe = Join-Path $verifyDir 'backend.exe'
go build -o $exe .
if ($LASTEXITCODE -ne 0) { throw 'go build failed' }
$base = "http://127.0.0.1:$Port"
$events = [System.Collections.Generic.List[object]]::new()
$serverProcess = $null

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Start-TestServer([string]$Phase) {
    $script:serverProcess = Start-Process -FilePath $exe -ArgumentList @('-addr', "0.0.0.0:$Port", '-data', 'data/issues.json') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $verifyDir "$Phase.stdout.log") -RedirectStandardError (Join-Path $verifyDir "$Phase.stderr.log")
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if ($script:serverProcess.HasExited) { throw "Server exited: see .verify/$Phase.stderr.log" }
        try {
            $health = Invoke-WebRequest "$base/api/health" -TimeoutSec 2 -UseBasicParsing
            if ($health.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 100 }
    }
    Assert-True $ready 'Server did not become ready'
    $events.Add(@{ step = "$Phase health"; status = $health.StatusCode; body = ($health.Content | ConvertFrom-Json) })
}

try {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $Port is already in use; no existing process was stopped"
    }
    Start-TestServer 'first'
    $initial = Invoke-RestMethod "$base/api/issues"
    $events.Add(@{ step = 'initial list'; count = $initial.Count })
    $body = @{
        title = '后端验收：入口墙面破损'
        description = '通过真实 HTTP 创建并验证重启持久化；保留作业验收数据'
        priority = 'high'
        position = @{ x = 0.42; y = 0.03; z = 1.26 }
    } | ConvertTo-Json -Compress
    $response = Invoke-WebRequest "$base/api/issues" -Method Post -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -UseBasicParsing
    Assert-True ($response.StatusCode -eq 201) 'Create did not return 201'
    $issue = $response.Content | ConvertFrom-Json
    Assert-True ($issue.id -and $issue.status -eq 'open') 'Unexpected created issue'
    $events.Add(@{ step = 'create'; status = $response.StatusCode; issue = $issue })
    $listed = Invoke-RestMethod "$base/api/issues"
    Assert-True (@($listed | Where-Object id -eq $issue.id).Count -eq 1) 'Created issue not in list'
    $events.Add(@{ step = 'list after create'; count = $listed.Count; id = $issue.id })

    $response = Invoke-WebRequest "$base/api/issues/$($issue.id)/status" -Method Patch -ContentType 'application/json' -Body '{"status":"in_progress"}' -UseBasicParsing
    Assert-True ($response.StatusCode -eq 200) 'PATCH did not return 200'
    $updated = $response.Content | ConvertFrom-Json
    Assert-True ($updated.status -eq 'in_progress' -and $updated.createdAt -eq $issue.createdAt -and $updated.updatedAt -ne $issue.updatedAt) 'Unexpected PATCH result'
    $events.Add(@{ step = 'update'; status = $response.StatusCode; issue = $updated })
    $listed = Invoke-RestMethod "$base/api/issues"
    $found = @($listed | Where-Object id -eq $issue.id)
    Assert-True ($found.Count -eq 1 -and $found[0].status -eq 'in_progress') 'Updated status not in list'
    $events.Add(@{ step = 'list after update'; id = $issue.id; status = $found[0].status })

    $preflight = Invoke-WebRequest "$base/api/issues/$($issue.id)/status" -Method Options -Headers @{ Origin = 'http://localhost:5173'; 'Access-Control-Request-Method' = 'PATCH'; 'Access-Control-Request-Headers' = 'content-type' } -UseBasicParsing
    Assert-True ($preflight.StatusCode -eq 204 -and $preflight.Headers['Access-Control-Allow-Origin'] -eq '*') 'CORS preflight failed'
    $events.Add(@{ step = 'CORS preflight'; status = $preflight.StatusCode })
    $beforeHash = (Get-FileHash -LiteralPath 'data/issues.json').Hash
    $firstProcessID = $serverProcess.Id
    Stop-Process -Id $serverProcess.Id
    $serverProcess.WaitForExit()
    $serverProcess = $null
    $events.Add(@{ step = 'stop first process'; processID = $firstProcessID })

    Start-TestServer 'restart'
    $listed = Invoke-RestMethod "$base/api/issues"
    $restored = @($listed | Where-Object id -eq $issue.id)
    Assert-True ($restored.Count -eq 1 -and $restored[0].status -eq 'in_progress') 'Issue was not restored after process restart'
    Assert-True ($restored[0].createdAt -eq $issue.createdAt -and $restored[0].updatedAt -eq $updated.updatedAt) 'Restart changed timestamps'
    Assert-True ((Get-FileHash -LiteralPath 'data/issues.json').Hash -eq $beforeHash) 'Restart changed persisted file'
    $events.Add(@{ step = 'restart persistence'; processID = $serverProcess.Id; id = $issue.id; status = $restored[0].status; fileSHA256 = $beforeHash; passed = $true })
} finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id
        $serverProcess.WaitForExit()
    }
    $events | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $verifyDir 'http-results.json') -Encoding utf8
}
$events | ConvertTo-Json -Depth 10
