param(
    [string]$AppPoolName = "DATA_EXCHANGE_HUB",
    [string]$Identity = "",
    [string[]]$CountryPaths = @(
        "Q:\QLIKDATA\DATA\SEND_MAIL\LV",
        "Q:\QLIKDATA\DATA\SEND_MAIL\LT",
        "Q:\QLIKDATA\DATA\SEND_MAIL\EE"
    )
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Identity)) {
    $Identity = "IIS AppPool\$AppPoolName"
}

foreach ($countryPath in $CountryPaths) {
    if (-not (Test-Path $countryPath)) {
        Write-Warning "Directory is not reachable from this session: $countryPath"
        continue
    }

    Write-Host "Granting Read/Execute permission on $countryPath to $Identity"
    & icacls $countryPath /grant "${Identity}:(OI)(CI)(RX)" /T
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to grant Read/Execute rights on $countryPath"
    }
}
