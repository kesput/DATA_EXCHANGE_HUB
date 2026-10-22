param(
    [Parameter(Mandatory = $true)][string]$SitePath,
    [string]$RequestId = "eb902ca6377ce9fff5901a240e08ee35",
    [int]$Hours = 24
)

$ErrorActionPreference = "Stop"
$site = (Resolve-Path -LiteralPath $SitePath).Path
$connections = @{}
$dike = @{}
foreach ($name in @("appsettings.json", "appsettings.Production.json")) {
    $path = Join-Path $site $name
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "$name is missing."
        continue
    }
    $settings = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    foreach ($property in $settings.ConnectionStrings.PSObject.Properties) {
        $connections[$property.Name] = $property.Value
    }
    foreach ($property in $settings.Dike.PSObject.Properties) {
        $dike[$property.Name] = $property.Value
    }
}
Write-Host "Configuration file check (values and passwords are not printed; environment overrides are not inspected):"
foreach ($key in @("AccessConnectionStringName", "Stat3ConnectionStringName")) {
    $connectionName = if ($dike[$key]) { $dike[$key] } elseif ($key -eq "AccessConnectionStringName") { "DataExchangeSql" } else { "STAT3" }
    $value = [string]$connections[$connectionName]
    [pscustomobject]@{
        Setting = "Dike:$key"
        ConnectionName = $connectionName
        Configured = -not [string]::IsNullOrWhiteSpace($value)
        ContainsPlaceholder = $value -match "CHANGE_ME|YOUR_|<password>"
    } | Format-List
}
$dll = Join-Path $site "DATA_EXCHANGE_HUB.dll"
Get-Item -LiteralPath $dll | Select-Object Name, LastWriteTime, Length | Format-List
Write-Host "Matching application events (inspect locally before sharing):"
try {
    Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = (Get-Date).AddHours(-$Hours) } -ErrorAction Stop |
        Where-Object { $_.Message -and $_.Message.Contains($RequestId) } |
        Select-Object -First 10 TimeCreated, ProviderName, Id, Message | Format-List
} catch {
    Write-Warning "Application events could not be read: $($_.Exception.Message)"
}
