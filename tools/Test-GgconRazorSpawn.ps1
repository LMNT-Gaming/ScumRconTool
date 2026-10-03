[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl,

    [double]$X = -28101,
    [double]$Y = -270202,
    [double]$Z = 19901,

    [string]$Password = 'yyj223ys'
)

$ErrorActionPreference = 'Stop'

function Read-PlainTextPassword {
    $securePassword = Read-Host 'ggCON HTTP password' -AsSecureString
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
}

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd('/')
if ($normalizedBaseUrl.EndsWith('/spawn-at', [StringComparison]::OrdinalIgnoreCase)) {
    $endpoint = $normalizedBaseUrl
}
else {
    $endpoint = $normalizedBaseUrl + '/spawn-at'
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = Read-PlainTextPassword
}

$body = [ordered]@{
    type = 'razor'
    x    = $X
    y    = $Y
    z    = $Z
}
$json = $body | ConvertTo-Json -Compress
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $headers['X-Password'] = $Password
}

Write-Host "POST $endpoint" -ForegroundColor Cyan
Write-Host "Body: $json" -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest `
        -Uri $endpoint `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $json `
        -UseBasicParsing

    Write-Host "HTTP $([int]$response.StatusCode) $($response.StatusDescription)" -ForegroundColor Green
    Write-Host $response.Content
}
catch {
    $statusLine = 'HTTP request failed'
    $responseBody = ''
    $errorResponse = $_.Exception.Response

    if ($null -ne $errorResponse) {
        try {
            $statusLine = "HTTP $([int]$errorResponse.StatusCode) $($errorResponse.StatusDescription)"
        }
        catch {
            $statusLine = 'HTTP request failed'
        }

        try {
            $stream = $errorResponse.GetResponseStream()
            if ($null -ne $stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                try {
                    $responseBody = $reader.ReadToEnd()
                }
                finally {
                    $reader.Dispose()
                    $stream.Dispose()
                }
            }
        }
        catch {
            $responseBody = ''
        }
    }

    Write-Host $statusLine -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
        Write-Host $responseBody -ForegroundColor Red
    }
    else {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
    exit 1
}
finally {
    $Password = $null
}
