param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\publish\DATA_EXCHANGE_HUB",
    [string]$AppPoolName = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $projectRoot $OutputPath
}
$publishPath = [System.IO.Path]::GetFullPath($publishPath)
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupPath = "$publishPath.backup.$timestamp"

Write-Host "Running build checks..."
dotnet build $projectRoot --configuration $Configuration

if (Test-Path $publishPath) {
    Write-Host "Creating publish backup: $backupPath"
    Copy-Item -Path $publishPath -Destination $backupPath -Recurse -Force
}

dotnet publish $projectRoot `
    --configuration $Configuration `
    --output $publishPath `
    --self-contained false

$appDataPath = Join-Path $publishPath "App_Data"
$dataExchangeStoragePath = Join-Path $appDataPath "DataExchange"
$dataExchangeImportPreviewPath = Join-Path $dataExchangeStoragePath "ImportPreviews"
$themisStoragePath = Join-Path $appDataPath "Themis"
$themisImportPreviewPath = Join-Path $themisStoragePath "ImportPreviews"
$sendMailFilePaths = @(
    "Q:\QLIKDATA\DATA\SEND_MAIL\LV",
    "Q:\QLIKDATA\DATA\SEND_MAIL\LT",
    "Q:\QLIKDATA\DATA\SEND_MAIL\EE"
)
New-Item -ItemType Directory -Path $dataExchangeImportPreviewPath -Force | Out-Null
New-Item -ItemType Directory -Path $themisImportPreviewPath -Force | Out-Null
foreach ($sendMailFilePath in $sendMailFilePaths) {
    if (Test-Path (Split-Path $sendMailFilePath -Parent)) {
        New-Item -ItemType Directory -Path $sendMailFilePath -Force | Out-Null
    }
}

if (-not [string]::IsNullOrWhiteSpace($AppPoolName)) {
    $appPoolIdentity = "IIS AppPool\$AppPoolName"
    Write-Host "Granting Modify permission on $appDataPath to $appPoolIdentity"
    & icacls $appDataPath /grant "${appPoolIdentity}:(OI)(CI)(M)" /T
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to grant Modify rights on $appDataPath"
    }

    foreach ($sendMailFilePath in $sendMailFilePaths) {
        if (Test-Path $sendMailFilePath) {
            Write-Host "Granting Read/Execute permission on $sendMailFilePath to $appPoolIdentity"
            & icacls $sendMailFilePath /grant "${appPoolIdentity}:(OI)(CI)(RX)" /T
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to grant Read/Execute rights on $sendMailFilePath"
            }
        } else {
            Write-Warning "Send Mail File directory is not reachable from this session: $sendMailFilePath"
        }
    }
}

Write-Host "Published to: $publishPath"
Write-Host "Data Exchange import preview storage: $dataExchangeImportPreviewPath"
Write-Host "THEMIS import preview storage: $themisImportPreviewPath"
Write-Host "Send Mail File IIS directories:"
foreach ($sendMailFilePath in $sendMailFilePaths) {
    Write-Host " - $sendMailFilePath"
}
if ([string]::IsNullOrWhiteSpace($AppPoolName)) {
    Write-Host "If publishing to IIS, grant Modify permission to the app pool, e.g.:"
    Write-Host "icacls `"$appDataPath`" /grant `"IIS AppPool\YOUR_APP_POOL:(OI)(CI)(M)`" /T"
    Write-Host "icacls `"Q:\QLIKDATA\DATA\SEND_MAIL\LV`" /grant `"IIS AppPool\YOUR_APP_POOL:(OI)(CI)(RX)`" /T"
}
if (Test-Path $backupPath) {
    Write-Host "Previous publish backup: $backupPath"
}
Write-Host "Copy appsettings.Production.template.json to appsettings.Production.json on the server and fill SQL credentials there."
