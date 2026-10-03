[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$VehicleClass,

    [double]$X = 523206,
    [double]$Y = -275936,
    [double]$Z = 619,

    [ValidateRange(5, 120)]
    [int]$PollSeconds = 30,

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

function Write-JsonResponse {
    param([object]$Value)

    if ($null -eq $Value) {
        Write-Host '<empty response>'
        return
    }

    Write-Host ($Value | ConvertTo-Json -Depth 10)
}

$base = $BaseUrl.Trim().TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($Password)) {
    $Password = Read-PlainTextPassword
}

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $headers['X-Password'] = $Password
}

$vehiclesUrl = $base + '/vehicles.json'
$vehicleTypesUrl = $base + '/vehicle-types.json'
$spawnUrl = $base + '/vehicles/spawn'
$requestedVehicleClass = $VehicleClass.Trim().Replace('\_', '_')

try {
    Write-Host "GET $vehicleTypesUrl" -ForegroundColor Cyan
    $typeResponse = Invoke-RestMethod -Uri $vehicleTypesUrl -Method Get -Headers $headers
    $availableTypes = @($typeResponse.items | ForEach-Object { [string]$_.i } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $resolvedVehicleClass = $availableTypes | Where-Object {
        $_.Equals($requestedVehicleClass, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($resolvedVehicleClass) -and
        $requestedVehicleClass.StartsWith('BP_', [StringComparison]::OrdinalIgnoreCase)) {
        $bpcCandidate = 'BPC_' + $requestedVehicleClass.Substring(3)
        $resolvedVehicleClass = $availableTypes | Where-Object {
            $_.Equals($bpcCandidate, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1

        if (-not [string]::IsNullOrWhiteSpace($resolvedVehicleClass)) {
            Write-Host "Vehicle class corrected from '$requestedVehicleClass' to catalog value '$resolvedVehicleClass'." -ForegroundColor Yellow
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedVehicleClass)) {
        $matches = @($availableTypes | Where-Object {
            $_ -like "*$requestedVehicleClass*" -or $_ -like "*$($requestedVehicleClass -replace '^BPC?_', '')*"
        } | Select-Object -First 10)
        $hint = if ($matches.Count -gt 0) { ' Possible matches: ' + ($matches -join ', ') } else { '' }
        throw "Vehicle class '$requestedVehicleClass' was not found in /vehicle-types.json.$hint"
    }
}
catch {
    Write-Host "Could not resolve the vehicle class: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$spawnBody = [ordered]@{
    class = $resolvedVehicleClass
    x     = $X
    y     = $Y
    z     = $Z
}
$spawnJson = $spawnBody | ConvertTo-Json -Compress

try {
    Write-Host "GET $vehiclesUrl" -ForegroundColor Cyan
    $beforeResponse = Invoke-RestMethod -Uri $vehiclesUrl -Method Get -Headers $headers
    $beforeVehicles = @($beforeResponse.vehicles)
    $knownIds = @{}
    foreach ($vehicle in $beforeVehicles) {
        if ($null -ne $vehicle.id) {
            $knownIds[[string]$vehicle.id] = $true
        }
    }
    Write-Host "Vehicles before spawn: $($beforeVehicles.Count)" -ForegroundColor DarkGray

    Write-Host "POST $spawnUrl" -ForegroundColor Cyan
    Write-Host "Body: $spawnJson" -ForegroundColor Cyan
    $spawnResponse = Invoke-RestMethod `
        -Uri $spawnUrl `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json; charset=utf-8' `
        -Body $spawnJson

    Write-Host 'Spawn response:' -ForegroundColor Green
    Write-JsonResponse $spawnResponse

    foreach ($propertyName in @('vehicleId', 'entityId', 'id')) {
        $property = $spawnResponse.PSObject.Properties[$propertyName]
        if ($null -ne $property -and $null -ne $property.Value) {
            Write-Host "Vehicle ID returned directly: $($property.Value)" -ForegroundColor Green
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($PollSeconds)
    $newVehicles = @()
    $latestResponse = $null

    do {
        Start-Sleep -Seconds 2
        $latestResponse = Invoke-RestMethod -Uri $vehiclesUrl -Method Get -Headers $headers
        $newVehicles = @($latestResponse.vehicles | Where-Object {
            $null -ne $_.id -and -not $knownIds.ContainsKey([string]$_.id)
        })
    }
    while ($newVehicles.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($newVehicles.Count -eq 0) {
        Write-Host "No new vehicle ID appeared in /vehicles.json within $PollSeconds seconds." -ForegroundColor Yellow
        Write-Host 'The spawn may have failed, or ggCON may expose the vehicle only after a later database refresh.' -ForegroundColor Yellow
        exit 2
    }

    Write-Host "New vehicle entries: $($newVehicles.Count)" -ForegroundColor Green
    $newVehicles |
        Select-Object id, class, name, rendered, spawnDate,
            @{Name = 'x'; Expression = { $_.location.x }},
            @{Name = 'y'; Expression = { $_.location.y }},
            @{Name = 'z'; Expression = { $_.location.z }} |
        Format-Table -AutoSize

    foreach ($vehicle in $newVehicles) {
        Write-Host "Cleanup endpoint (not executed): POST $base/vehicles/$($vehicle.id)/destroy" -ForegroundColor DarkGray
    }
}
catch {
    $responseBody = ''
    $errorResponse = $_.Exception.Response
    if ($null -ne $errorResponse) {
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

    Write-Host $_.Exception.Message -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
        Write-Host "ggCON response: $responseBody" -ForegroundColor Red
    }
    exit 1
}
finally {
    $Password = $null
}
