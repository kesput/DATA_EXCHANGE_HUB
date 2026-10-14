param(
    [string]$SitePath = "C:\inetpub\DATA_EXCHANGE_HUB",
    [string]$AppPoolName = "DATA_EXCHANGE_HUB",
    [string]$StoragePath = "",
    [string]$Identity = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Identity)) {
    $Identity = "IIS AppPool\$AppPoolName"
}

$storagePath = if ([string]::IsNullOrWhiteSpace($StoragePath)) {
    Join-Path $SitePath "App_Data"
} elseif ([System.IO.Path]::IsPathRooted($StoragePath)) {
    $StoragePath
} else {
    Join-Path $SitePath $StoragePath
}
$storagePath = [System.IO.Path]::GetFullPath($storagePath)
$dataExchangeImportPreviewPath = Join-Path $storagePath "DataExchange\ImportPreviews"
$themisImportPreviewPath = Join-Path $storagePath "Themis\ImportPreviews"

Write-Host "Ensuring storage directories exist..."
New-Item -ItemType Directory -Force -Path $dataExchangeImportPreviewPath | Out-Null
New-Item -ItemType Directory -Force -Path $themisImportPreviewPath | Out-Null

Write-Host "Granting Modify rights to $Identity on $storagePath"
& icacls $storagePath /grant "${Identity}:(OI)(CI)(M)" /T
if ($LASTEXITCODE -ne 0) {
    throw "Failed to grant Modify rights on $storagePath"
}

Write-Host "Done."
