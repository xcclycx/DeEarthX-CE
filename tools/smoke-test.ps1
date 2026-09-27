# DeEarthX backend API smoke test.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools/smoke-test.ps1
#   powershell -ExecutionPolicy Bypass -File tools/smoke-test.ps1 -BaseUrl http://localhost:37019
#
# Without -BaseUrl the script builds nothing: it launches the already-built backend
# (dotnet run --no-build) and stops it again when done.
#
# Covers the read-only endpoints plus the full plugin lifecycle
# (create -> configure -> validate -> files -> page -> export -> install -> uninstall),
# including the path-traversal guard and the encrypted export/import round trip.
# Works on Windows PowerShell 5.1 (no -Form / -SkipHttpErrorCheck available there).

[CmdletBinding()]
param(
    [string]$BaseUrl = "",
    [string]$WebProject = "",
    [int]$StartupTimeoutSeconds = 90
)

# $PSScriptRoot is not usable inside param() defaults on Windows PowerShell 5.1.
if (-not $WebProject) {
    $WebProject = Join-Path $PSScriptRoot '..\backend-net\src\DeEarthX.Web'
}

$ErrorActionPreference = 'Stop'
$script:Passed = 0
$script:Failed = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Check {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:Passed++
        Write-Host ("  PASS  {0}" -f $Name) -ForegroundColor Green
    }
    catch {
        $script:Failed++
        $script:Failures.Add("$Name :: $($_.Exception.Message)")
        Write-Host ("  FAIL  {0}" -f $Name) -ForegroundColor Red
        Write-Host ("        {0}" -f $_.Exception.Message) -ForegroundColor DarkRed
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message (expected '$Expected', got '$Actual')" }
}

# PS 5.1 friendly HTTP helper: never throws on 4xx/5xx, supports multipart upload.
function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [string]$ContentType = 'application/json',
        [string]$FilePath = $null,
        [hashtable]$Fields = $null,
        [int]$TimeoutSec = 60
    )

    $params = @{
        Uri                = "$script:Base$Path"
        Method             = $Method
        UseBasicParsing    = $true
        TimeoutSec         = $TimeoutSec
    }

    if ($FilePath) {
        $boundary = [Guid]::NewGuid().ToString('N')
        $enc = [System.Text.Encoding]::UTF8
        $ms = New-Object System.IO.MemoryStream
        if ($Fields) {
            foreach ($key in $Fields.Keys) {
                $part = "--$boundary`r`nContent-Disposition: form-data; name=`"$key`"`r`n`r`n$($Fields[$key])`r`n"
                $bytes = $enc.GetBytes($part)
                $ms.Write($bytes, 0, $bytes.Length)
            }
        }
        $fileName = [System.IO.Path]::GetFileName($FilePath)
        $head = "--$boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"$fileName`"`r`nContent-Type: application/octet-stream`r`n`r`n"
        $bytes = $enc.GetBytes($head)
        $ms.Write($bytes, 0, $bytes.Length)
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $ms.Write($fileBytes, 0, $fileBytes.Length)
        $tail = $enc.GetBytes("`r`n--$boundary--`r`n")
        $ms.Write($tail, 0, $tail.Length)
        $params['Body'] = $ms.ToArray()
        $params['ContentType'] = "multipart/form-data; boundary=$boundary"
    }
    elseif ($null -ne $Body) {
        $params['Body'] = $Body
        $params['ContentType'] = $ContentType
    }

    try {
        return Invoke-WebRequest @params
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        $content = $reader.ReadToEnd()
        $reader.Dispose()
        return New-Object PSObject -Property @{
            StatusCode = [int]$response.StatusCode
            Content    = $content
            Headers    = $response.Headers
        }
    }
}

function Get-Json {
    param([string]$Path)
    $r = Invoke-Api -Method GET -Path $Path
    if ($r.StatusCode -ne 200) { throw "GET $Path returned HTTP $($r.StatusCode)" }
    return ($r.Content | ConvertFrom-Json)
}

# ---------------------------------------------------------------- server boot

if (-not $BaseUrl) { $BaseUrl = 'http://localhost:37019' }
$script:Base = $BaseUrl.TrimEnd('/')

$started = $null
$alreadyUp = $false
try {
    $probe = Invoke-WebRequest -Uri "$script:Base/" -UseBasicParsing -TimeoutSec 3
    $alreadyUp = ($probe.StatusCode -eq 200)
}
catch { $alreadyUp = $false }

if (-not $alreadyUp) {
    Write-Host "Starting backend from $WebProject ..." -ForegroundColor Cyan
    $started = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', '.', '--no-build') `
        -WorkingDirectory (Resolve-Path $WebProject).Path `
        -PassThru -WindowStyle Hidden

    $ready = $false
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    while (-not $ready -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        try {
            $probe = Invoke-WebRequest -Uri "$script:Base/" -UseBasicParsing -TimeoutSec 3
            $ready = ($probe.StatusCode -eq 200)
        }
        catch { $ready = $false }
    }
    if (-not $ready) {
        if ($started) { Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue }
        throw "Backend did not become ready at $script:Base within $StartupTimeoutSeconds s"
    }
    Write-Host "Backend is up at $script:Base" -ForegroundColor Cyan
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("dex-smoke-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

$pluginId = $null
try {
    Write-Host "`n[1/6] Core endpoints" -ForegroundColor Cyan
    Check 'GET / returns 200 + status 200' {
        $r = Invoke-WebRequest -Uri "$script:Base/" -UseBasicParsing -TimeoutSec 15
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-Equal 200 (($r.Content | ConvertFrom-Json).status) 'payload status'
    }
    Check 'GET /version exposes a version' {
        $j = Get-Json '/version'
        Assert-Equal 200 $j.status 'status'
        Assert-True (-not [string]::IsNullOrWhiteSpace($j.version)) 'version must not be empty'
    }
    Check 'GET /config/get returns port' {
        $j = Get-Json '/config/get'
        Assert-True ($null -ne $j.port) 'port missing'
    }
    Check 'GET /java/check returns a result' {
        $j = Get-Json '/java/check'
        Assert-Equal 200 $j.status 'status'
    }
    Check 'GET /modcheck without a path reports status 400' {
        # This route answers HTTP 200 with a {status,message} envelope (frontend convention).
        $r = Invoke-Api -Method GET -Path '/modcheck'
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-Equal 400 (($r.Content | ConvertFrom-Json).status) 'envelope status'
    }

    Write-Host "`n[2/6] Plugin listing endpoints" -ForegroundColor Cyan
    Check 'GET /plugins returns an array' {
        $j = Get-Json '/plugins'
        Assert-Equal 200 $j.status 'status'
        Assert-True ($j.data -is [System.Array]) 'data must be an array'
    }
    Check 'GET /plugins/diagnostics returns counters' {
        $j = Get-Json '/plugins/diagnostics'
        Assert-True ($null -ne $j.data.total) 'total missing'
    }
    Check 'GET /plugins/injections returns an array' {
        $j = Get-Json '/plugins/injections'
        Assert-Equal 200 $j.status 'status'
    }
    Check 'GET /templates returns an array' {
        $j = Get-Json '/templates'
        Assert-Equal 200 $j.status 'status'
    }

    Write-Host "`n[3/6] Plugin lifecycle" -ForegroundColor Cyan
    Check 'POST /plugins/create scaffolds a plugin' {
        $r = Invoke-Api -Method POST -Path '/plugins/create' -Body '{"name":"smoke-test-plugin","author":"smoke-test"}'
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        $created = ($r.Content | ConvertFrom-Json).data.manifest.id
        Assert-True (-not [string]::IsNullOrWhiteSpace($created)) 'plugin id missing'
        $script:PluginId = $created
    }
    $pluginId = $script:PluginId
    Assert-True (-not [string]::IsNullOrWhiteSpace($pluginId)) 'no plugin id, later checks are meaningless'

    Check 'GET /plugins/{id} returns the manifest' {
        $j = Get-Json "/plugins/$pluginId"
        Assert-Equal $pluginId $j.data.manifest.id 'id mismatch'
    }
    Check 'GET /plugins/{id}/validate reports valid' {
        $j = Get-Json "/plugins/$pluginId/validate"
        Assert-True $j.data.isValid 'plugin should be valid'
    }
    Check 'POST /plugins/{id}/config then GET reflects it' {
        $r = Invoke-Api -Method POST -Path "/plugins/$pluginId/config" -Body '{"greeting":"hi"}'
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        $j = Get-Json "/plugins/$pluginId/config"
        Assert-Equal 'hi' $j.data.settings.greeting 'setting not persisted'
    }
    Check 'POST /plugins/{id}/config/reset clears settings' {
        $r = Invoke-Api -Method POST -Path "/plugins/$pluginId/config/reset"
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        $j = Get-Json "/plugins/$pluginId/config"
        Assert-True ($null -eq $j.data.settings.greeting) 'setting still present after reset'
    }
    Check 'POST /plugins/{id}/disable then enable' {
        Assert-Equal 200 (Invoke-Api -Method POST -Path "/plugins/$pluginId/disable").StatusCode 'disable'
        $j = Get-Json "/plugins/$pluginId"
        Assert-True (-not $j.data.enabled) 'plugin should be disabled'
        Assert-Equal 200 (Invoke-Api -Method POST -Path "/plugins/$pluginId/enable").StatusCode 'enable'
        $j = Get-Json "/plugins/$pluginId"
        Assert-True $j.data.enabled 'plugin should be enabled'
    }
    Check 'GET /plugins/{id}/files lists scaffold files' {
        $j = Get-Json "/plugins/$pluginId/files"
        Assert-True ($j.data.Count -gt 0) 'no files listed'
    }

    Write-Host "`n[4/6] Plugin file serving + traversal guard" -ForegroundColor Cyan
    Check 'GET /plugins/{id}/files/manifest.json serves the file' {
        $r = Invoke-Api -Method GET -Path "/plugins/$pluginId/files/manifest.json"
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-True ($r.Content -match [regex]::Escape($pluginId)) 'manifest content mismatch'
    }
    Check 'traversal via backslashes is rejected' {
        $r = Invoke-Api -Method GET -Path "/plugins/$pluginId/files/..%5C..%5C..%5Cappsettings.json"
        Assert-True ($r.StatusCode -ne 200) "traversal returned HTTP $($r.StatusCode)"
        Assert-True ($r.Content -notmatch 'Galaxy') 'leaked appsettings.json'
    }
    Check 'traversal via slashes is rejected' {
        $r = Invoke-Api -Method GET -Path "/plugins/$pluginId/files/..%2f..%2f..%2fappsettings.json"
        Assert-True ($r.StatusCode -ne 200) "traversal returned HTTP $($r.StatusCode)"
    }
    Check 'GET /plugin-page/{id}/index serves html' {
        $r = Invoke-Api -Method GET -Path "/plugin-page/$pluginId/index"
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-True ($r.Headers['Content-Type'] -match 'text/html') 'expected text/html'
    }
    Check 'GET /plugins/{id}/sidebar returns sidebar items' {
        $j = Get-Json "/plugins/$pluginId/sidebar"
        Assert-True $j.data.hasSidebar 'hasSidebar false'
    }

    Write-Host "`n[5/6] Plugin export / encrypted round trip" -ForegroundColor Cyan
    Check 'GET /plugins/{id}/export returns a zip' {
        $r = Invoke-Api -Method GET -Path "/plugins/$pluginId/export"
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-Equal 'PK' ([System.Text.Encoding]::ASCII.GetString($r.Content[0..1])) 'not a zip'
        $script:ZipPath = Join-Path $tempDir 'plugin.zip'
        [System.IO.File]::WriteAllBytes($script:ZipPath, $r.Content)
    }
    Check 'private encrypted export rejects a non-alphanumeric password' {
        $r = Invoke-Api -Method POST -Path "/plugins/$pluginId/export-encrypted" -Body '{"mode":"private","password":"bad-pass!"}'
        Assert-Equal 400 $r.StatusCode 'HTTP status'
    }
    Check 'private encrypted export produces a DEXP container' {
        $r = Invoke-Api -Method POST -Path "/plugins/$pluginId/export-encrypted" -Body '{"mode":"private","password":"TestPass123"}'
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-Equal 'DEXP' ([System.Text.Encoding]::ASCII.GetString($r.Content[0..3])) 'missing DEXP header'
        $script:DxpPath = Join-Path $tempDir 'plugin.dxp'
        [System.IO.File]::WriteAllBytes($script:DxpPath, $r.Content)
    }

    Write-Host "`n[6/6] Uninstall + encrypted install" -ForegroundColor Cyan
    Check 'DELETE /plugins/{id}?keepConfig=false removes the plugin' {
        $r = Invoke-Api -Method DELETE -Path "/plugins/${pluginId}?keepConfig=false"
        Assert-Equal 200 $r.StatusCode 'HTTP status'
        Assert-Equal 200 (($r.Content | ConvertFrom-Json).status) 'envelope status'
    }
    Check 'uninstalled plugin disappears from GET /plugins immediately' {
        $r = Invoke-Api -Method GET -Path '/plugins'
        Assert-True ($r.Content -notmatch [regex]::Escape($pluginId)) 'uninstalled plugin still listed (stale snapshot)'
    }
    Check 'GET /plugins/{id} on an uninstalled plugin returns 404' {
        $r = Invoke-Api -Method GET -Path "/plugins/$pluginId"
        Assert-Equal 404 $r.StatusCode 'HTTP status'
    }
    Check 'install-encrypted rejects a wrong password' {
        $r = Invoke-Api -Method POST -Path '/plugins/install-encrypted' -FilePath $script:DxpPath -Fields @{ password = 'WrongPass999' }
        Assert-Equal 400 $r.StatusCode 'HTTP status'
    }
    Check 'install-encrypted restores the plugin with the right password' {
        $r = Invoke-Api -Method POST -Path '/plugins/install-encrypted' -FilePath $script:DxpPath -Fields @{ password = 'TestPass123' }
        Assert-Equal 200 $r.StatusCode "HTTP status ($($r.Content))"
        $j = Get-Json "/plugins/$pluginId"
        Assert-Equal $pluginId $j.data.manifest.id 'id mismatch after reinstall'
    }
    Check 'POST /plugins/reload rebuilds the registry' {
        $r = Invoke-Api -Method POST -Path '/plugins/reload'
        Assert-Equal 200 $r.StatusCode 'HTTP status'
    }
}
finally {
    if ($pluginId) {
        $null = Invoke-Api -Method DELETE -Path "/plugins/${pluginId}?keepConfig=false" -TimeoutSec 20
    }
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($started -and -not $started.HasExited) {
        Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($script:Failed -eq 0) {
    Write-Host ("ALL {0} CHECKS PASSED" -f $script:Passed) -ForegroundColor Green
    exit 0
}

Write-Host ("{0} passed, {1} FAILED" -f $script:Passed, $script:Failed) -ForegroundColor Red
$script:Failures | ForEach-Object { Write-Host ("  - " + $_) -ForegroundColor Red }
exit 1
