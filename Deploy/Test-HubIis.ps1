param(
    [Parameter(Mandatory = $true)][string]$SiteName,
    [string]$ApplicationPath = "/"
)

$ErrorActionPreference = "Stop"
$issues = New-Object 'System.Collections.Generic.List[string]'
Import-Module WebAdministration -ErrorAction Stop
$site = Get-Website -Name $SiteName
if (-not $site) { throw "IIS site not found: $SiteName" }
$location = $SiteName
$sitePath = $site.physicalPath
$poolName = $site.applicationPool
if ($ApplicationPath -ne "/") {
    $application = Get-WebApplication -Site $SiteName | Where-Object { $_.path -eq $ApplicationPath }
    if (-not $application) { throw "IIS application not found: $ApplicationPath" }
    $location += $ApplicationPath
    $sitePath = $application.physicalPath
    $poolName = $application.applicationPool
}
$sitePath = [Environment]::ExpandEnvironmentVariables($sitePath)
$sitePath = (Resolve-Path -LiteralPath $sitePath).Path
$pool = Get-Item -LiteralPath "IIS:\AppPools\$poolName"
$windows = (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled).Value
$anonymous = (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled).Value
if (-not $windows) { $issues.Add('Windows Authentication must be enabled in IIS.') }
if ($anonymous) { Write-Host 'Public landing is enabled. Protected HUB pages still require the application identity and module permission checks.' }
else { Write-Host 'IIS requires Windows authentication before opening the landing page.' }
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2)) { $issues.Add('ASP.NET Core Hosting Bundle / AspNetCoreModuleV2 is missing.') }
[pscustomobject]@{
    Site = $location; PhysicalPath = $sitePath; AppPool = $poolName
    PoolState = (Get-WebAppPoolState -Name $poolName).Value
    IdentityType = $pool.processModel.identityType
    WindowsAuthentication = $windows; AnonymousAuthentication = $anonymous
    ManagedRuntime = $pool.managedRuntimeVersion
} | Format-List

$webPath = Join-Path $sitePath 'web.config'
if (Test-Path -LiteralPath $webPath) {
    $web = [xml](Get-Content -LiteralPath $webPath -Raw)
    $core = $web.SelectSingleNode('//aspNetCore')
    $environment = $web.SelectSingleNode('//environmentVariable[@name="ASPNETCORE_ENVIRONMENT"]')
    if (-not $core -or $core.hostingModel -ne 'inprocess') { $issues.Add('Expected IIS inprocess hosting.') }
    if (-not $environment -or $environment.value -ne 'Production') { $issues.Add('web.config must select Production.') }
} else { $issues.Add('web.config is missing from the IIS directory.') }

$basePath = Join-Path $sitePath 'appsettings.json'
$productionPath = Join-Path $sitePath 'appsettings.Production.json'
if (-not (Test-Path -LiteralPath $basePath)) { throw 'appsettings.json is missing.' }
$settings = Get-Content -LiteralPath $basePath -Raw | ConvertFrom-Json
$connections = @{}
foreach ($property in $settings.ConnectionStrings.PSObject.Properties) { $connections[$property.Name] = $property.Value }
if (Test-Path -LiteralPath $productionPath) {
    $production = Get-Content -LiteralPath $productionPath -Raw | ConvertFrom-Json
    foreach ($property in $production.ConnectionStrings.PSObject.Properties) { $connections[$property.Name] = $property.Value }
    if ($production.HubApplications) {
        $issues.Add('Production overrides HubApplications. Review/remove an old catalogue override; JSON arrays merge by index and can hide or duplicate DIKE/IRIS.')
    }
    if ($production.DataExchange.TestMode) { $issues.Add('DataExchange TestMode is enabled in Production.') }
} else { $issues.Add('appsettings.Production.json is missing. Verify server-specific SQL configuration and environment overrides.') }
foreach ($name in @('DataExchangeSql', 'STAT3')) {
    $configured = -not [string]::IsNullOrWhiteSpace([string]$connections[$name])
    $placeholder = [string]$connections[$name] -match 'CHANGE_ME|YOUR_|<password>'
    [pscustomobject]@{ ConnectionName = $name; Configured = $configured; ContainsPlaceholder = $placeholder } | Format-List
    if (-not $configured -or $placeholder) { $issues.Add("SQL profile needs configuration: $name") }
}
$dll = Join-Path $sitePath 'DATA_EXCHANGE_HUB.dll'
if (Test-Path -LiteralPath $dll) { Get-Item -LiteralPath $dll | Select-Object Name, LastWriteTime, Length | Format-List }
else { $issues.Add('Application DLL is missing.') }
foreach ($asset in @('wwwroot\js\iris-source-preview.js', 'wwwroot\js\date-picker.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sitePath $asset))) { $issues.Add("Published asset is missing: $asset") }
}
Write-Host 'This check reads IIS and JSON only. It does not print passwords, connect to SQL, or change IIS. Environment overrides and the actual AppPool SQL/file permissions require a live user test.'
if ($issues.Count) {
    foreach ($issue in $issues) { Write-Warning $issue }
    exit 1
}
Write-Host 'IIS and file configuration checks passed. Verify a real user with assigned permissions and a second user without permissions.'
