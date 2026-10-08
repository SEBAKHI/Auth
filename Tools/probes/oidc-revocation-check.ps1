# OI-65 owner check: an application's tokens die when they are revoked, on the SANDBOX.
#
# Before you run it (the script cannot do these for you):
#   1. In the console, Applications > New application ("التطبيقات" > "تطبيق جديد"):
#        Name  OI65 check        Code ("الرمز")  OI65-CHECK
#        Redirect URIs ("عناوين إعادة التوجيه (Redirect URIs)")  https://localhost/oi65-check
#        Who can sign in ("من يستطيع الدخول")  Everyone ("الجميع")
#      Save ("حفظ").
#   2. Keep that application's page open in the console: round 3 asks you to switch it off ("متاح")
#      and back on.
#
# Run (Windows PowerShell 5.1), from the repository root of the merged main:
#   powershell -ExecutionPolicy Bypass -File Tools\probes\oidc-revocation-check.ps1
#
# Three rounds, each a fresh sign-in through the real authorize -> token flow with PKCE. Your browser
# opens; paste the address it lands on. The script never prints or saves a token.
#   Round 1: revoke the ACCESS token          -> UserInfo answers 401 Http.TokenRevoked
#   Round 2: revoke the REFRESH token         -> UserInfo answers 401 Http.SessionRevoked; the refresh is refused
#   Round 3: switch the application off       -> UserInfo answers 401 Http.SessionRevoked
# Every step prints PASS or FAIL; the exit code is 1 when any step failed.
# Another server needs -Base <https://host> AND -AllowNonSandbox, on purpose.

param(
    [string]$Base = "https://auth-sandbox.sebakhi.com",
    [string]$ClientId = "OI65-CHECK",
    [string]$RedirectUri = "https://localhost/oi65-check",
    [switch]$AllowNonSandbox
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Sandbox = "https://auth-sandbox.sebakhi.com"
if ($Base.TrimEnd('/') -ne $Sandbox -and -not $AllowNonSandbox) {
    throw "This check targets the sandbox ($Sandbox). To run it against $Base, add -AllowNonSandbox."
}
$Base = $Base.TrimEnd('/')

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-RandomValue {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    ConvertTo-Base64Url $bytes
}

# The S256 code challenge of a PKCE verifier (RFC 7636 section 4.2).
function Get-S256Challenge([string]$Verifier) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    ConvertTo-Base64Url ($sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($Verifier)))
}

# The PKCE pair (RFC 7636): a random verifier and its S256 challenge.
function New-PkcePair {
    $verifier = New-RandomValue
    [pscustomobject]@{ Verifier = $verifier; Challenge = (Get-S256Challenge $verifier) }
}

# Status and body of a response, even for a 4xx (Windows PowerShell 5.1 throws on those).
function Read-ErrorResponse($ErrorRecord) {
    $resp = $ErrorRecord.Exception.Response
    if ($null -eq $resp) { throw $ErrorRecord }
    $body = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd()
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $body }
}

# A bearer call (UserInfo).
function Invoke-Call([string]$Method, [string]$Url, [string]$Token) {
    try {
        $r = Invoke-WebRequest -Method $Method -Uri $Url -Headers @{ Authorization = "Bearer $Token" } -UseBasicParsing
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    }
    catch [System.Net.WebException] { return Read-ErrorResponse $_ }
}

# An anonymous form post (the revocation and token endpoints).
function Invoke-Form([string]$Url, [hashtable]$Fields) {
    try {
        $r = Invoke-WebRequest -Method Post -Uri $Url -ContentType "application/x-www-form-urlencoded" -Body $Fields -UseBasicParsing
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    }
    catch [System.Net.WebException] { return Read-ErrorResponse $_ }
}

function Get-ProblemCode([string]$Body) {
    try { return ($Body | ConvertFrom-Json).code } catch { return "(no problem body)" }
}

$script:Failures = 0

# One PASS or FAIL line. Details are statuses and codes only, never a token.
function Write-Step([string]$Name, [bool]$Passed, [string]$Detail) {
    if ($Passed) {
        Write-Host ("PASS  {0}  ({1})" -f $Name, $Detail) -ForegroundColor Green
    }
    else {
        $script:Failures++
        Write-Host ("FAIL  {0}  ({1})" -f $Name, $Detail) -ForegroundColor Red
    }
}

function Test-Answer([string]$Name, $Call, [int]$Status, [string]$Code) {
    $actual = if ($Call.Status -eq 200) { "" } else { Get-ProblemCode $Call.Body }
    $passed = $Call.Status -eq $Status -and (-not $Code -or $actual -eq $Code)
    $expected = if ($Code) { "$Status $Code" } else { "$Status" }
    Write-Step $Name $passed ("expected {0}; got {1} {2}" -f $expected, $Call.Status, $actual)
}

# Sign in through the real authorize -> sign-in -> token flow with PKCE; returns the token response.
function Get-ApplicationToken([string]$Round) {
    $pkce = New-PkcePair
    $state = New-RandomValue

    $authorize = "$Base/api/v1/auth/authorize?response_type=code" +
        "&client_id=" + [Uri]::EscapeDataString($ClientId) +
        "&redirect_uri=" + [Uri]::EscapeDataString($RedirectUri) +
        "&code_challenge=$($pkce.Challenge)&code_challenge_method=S256&state=$state&scope=openid"

    Write-Host ""
    Write-Host "== $Round" -ForegroundColor Yellow
    Write-Host "Opening the browser. Sign in if asked (after the first round it returns at once)." -ForegroundColor Cyan
    Write-Host "The browser then shows an error page (nothing runs on localhost). That is expected."
    Start-Process $authorize
    $landed = Read-Host "Paste the FULL address from the browser's address bar (it starts with $RedirectUri), then press Enter within 60 seconds"

    if ($landed -match '[?&]error=([^&]+)') { throw "The server refused the sign-in: error=$($Matches[1])" }
    if ($landed -notmatch '[?&]code=([^&]+)') { throw "No code in the pasted address." }
    $code = [Uri]::UnescapeDataString($Matches[1])
    if ($landed -notmatch '[?&]state=([^&]+)' -or [Uri]::UnescapeDataString($Matches[1]) -ne $state) {
        throw "The state in the pasted address does not match this run. Start again."
    }

    Invoke-RestMethod -Method Post -Uri "$Base/api/v1/auth/token" `
        -ContentType "application/x-www-form-urlencoded" `
        -Body @{
            grant_type    = "authorization_code"
            code          = $code
            redirect_uri  = $RedirectUri
            client_id     = $ClientId
            code_verifier = $pkce.Verifier
        }
}

# Everything below runs only when the file is executed, not when a test dot-sources the helpers.
if ($MyInvocation.InvocationName -eq '.') { return }

$userInfo = "$Base/api/v1/auth/userinfo"
$revoke = "$Base/api/v1/auth/revoke"

# Round 1: the application revokes its ACCESS token; that token stops at once.
$one = Get-ApplicationToken "Round 1: revoke the access token"
Test-Answer "1a UserInfo with the new access token" (Invoke-Call "GET" $userInfo $one.access_token) 200 ""
Test-Answer "1b revoke the access token" (Invoke-Form $revoke @{ token = $one.access_token; token_type_hint = "access_token" }) 200 ""
Test-Answer "1c UserInfo with the revoked access token" (Invoke-Call "GET" $userInfo $one.access_token) 401 "Http.TokenRevoked"
# Not a step: end round 1's session too, so the check leaves nothing open.
Invoke-Form $revoke @{ token = $one.refresh_token; token_type_hint = "refresh_token" } | Out-Null

# Round 2: the application revokes its REFRESH token; the whole session ends with it.
$two = Get-ApplicationToken "Round 2: revoke the refresh token"
Test-Answer "2a UserInfo with the new access token" (Invoke-Call "GET" $userInfo $two.access_token) 200 ""
Test-Answer "2b revoke the refresh token" (Invoke-Form $revoke @{ token = $two.refresh_token; token_type_hint = "refresh_token" }) 200 ""
Test-Answer "2c UserInfo with that session's access token" (Invoke-Call "GET" $userInfo $two.access_token) 401 "Http.SessionRevoked"
$refreshed = Invoke-Form "$Base/api/v1/auth/token" @{ grant_type = "refresh_token"; refresh_token = $two.refresh_token; client_id = $ClientId }
Test-Answer "2d refresh with the revoked refresh token is refused" $refreshed 403 "Auth.RefreshTokenRevoked"

# Round 3: an administrator switches the application off; its tokens already out stop.
$three = Get-ApplicationToken "Round 3: switch the application off"
Test-Answer "3a UserInfo with the new access token" (Invoke-Call "GET" $userInfo $three.access_token) 200 ""
Write-Host ""
Write-Host "In the console: 'التطبيقات' > 'OI65 check'. Turn the 'متاح' switch off," -ForegroundColor Cyan
Write-Host "then press 'تأكيد' in the dialog 'إيقاف هذا التطبيق؟'." -ForegroundColor Cyan
Read-Host "Press Enter when the application is switched off" | Out-Null
Test-Answer "3b UserInfo after the switch-off" (Invoke-Call "GET" $userInfo $three.access_token) 401 "Http.SessionRevoked"
Write-Host ""
Write-Host "Now turn the 'متاح' switch back on for 'OI65 check'." -ForegroundColor Cyan
Read-Host "Press Enter when it is on again" | Out-Null

Write-Host ""
Write-Host "Clean-up: delete the test application in the console:" -ForegroundColor Green
Write-Host "  'التطبيقات' > 'OI65 check' > 'حذف التطبيق'."
if ($script:Failures -gt 0) {
    Write-Host ("{0} step(s) FAILED. Copy the FAIL lines into the PR or the manager's chat." -f $script:Failures) -ForegroundColor Red
    exit 1
}
Write-Host "All steps PASS." -ForegroundColor Green
