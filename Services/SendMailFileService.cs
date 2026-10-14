using System.Data;
using System.Security.Claims;
using DATA_EXCHANGE_HUB.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class SendMailFileService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IOptions<SendMailFileOptions> options,
    IUserNameResolver userNameResolver) : ISendMailFileService
{
    private const string DefaultAccessProcedureName = "BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess";
    private readonly SendMailFileOptions _options = options.Value;

    public async Task<IReadOnlyList<SendMailFileCountryAccess>> GetAccessibleCountriesAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<SendMailFileCountryAccess>();
        foreach (var country in GetCountries())
        {
            if (!await HasCountryAccessAsync(user, country, cancellationToken))
            {
                continue;
            }

            var directoryPath = GetDirectoryPath(country);
            var directoryExists = Directory.Exists(directoryPath);
            rows.Add(new SendMailFileCountryAccess(
                country,
                directoryPath,
                directoryExists,
                directoryExists ? LoadFiles(country, directoryPath) : []));
        }

        return rows;
    }

    public async Task<bool> HasAnyAccessAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        foreach (var country in GetCountries())
        {
            if (await HasCountryAccessAsync(user, country, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<FileInfo?> GetDownloadFileAsync(
        ClaimsPrincipal user,
        string country,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var normalizedCountry = NormalizeCountry(country);
        if (normalizedCountry is null ||
            !await HasCountryAccessAsync(user, normalizedCountry, cancellationToken))
        {
            return null;
        }

        var safeFileName = BuildSafeFileName(fileName);
        if (!safeFileName.Equals(fileName, StringComparison.Ordinal))
        {
            return null;
        }

        var directoryPath = GetDirectoryPath(normalizedCountry);
        var fullPath = Path.Combine(directoryPath, safeFileName);
        if (!IsPathInsideDirectory(directoryPath, fullPath) || !File.Exists(fullPath))
        {
            return null;
        }

        return new FileInfo(fullPath);
    }

    private async Task<bool> HasCountryAccessAsync(
        ClaimsPrincipal user,
        string country,
        CancellationToken cancellationToken)
    {
        var userName = userNameResolver.ResolveUserName(user);
        var connectionString = GetConnectionString(_options.AccessConnectionStringName);
        var procedureName = SqlName.NormalizeTableName(
            _options.AccessProcedureName,
            DefaultAccessProcedureName);
        var appName = GetCountryAppName(country);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXEC {procedureName} @AppName, @UserName";
        command.Parameters.Add("@AppName", SqlDbType.NVarChar, 100).Value = appName;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 256).Value = userName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private IReadOnlyList<SendMailDirectoryFile> LoadFiles(string country, string directoryPath)
    {
        return Directory
            .EnumerateFiles(directoryPath)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new SendMailDirectoryFile(
                country,
                file.Name,
                file.Length,
                file.LastWriteTime))
            .ToArray();
    }

    private IEnumerable<string> GetCountries()
    {
        return GetCountryPaths()
            .Keys
            .Where(country => NormalizeCountry(country) is not null)
            .Select(country => country.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order();
    }

    private string GetDirectoryPath(string country)
    {
        var paths = GetCountryPaths();
        return paths.TryGetValue(country, out var path)
            ? path
            : string.Empty;
    }

private string GetCountryAppName(string country)
    {
        return _options.CountryAppNames.TryGetValue(country, out var appName) &&
               !string.IsNullOrWhiteSpace(appName)
            ? appName
            : $"{_options.AppName}_{country}";
    }

    private IReadOnlyDictionary<string, string> GetCountryPaths()
    {
        return environment.IsDevelopment()
            ? _options.LocalCountryPaths
            : _options.IisCountryPaths;
    }
    private string GetConnectionString(string connectionStringName)
    {
        return configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"SQL Server prisijungimo eilute '{connectionStringName}' nesukonfiguruota.");
    }
    private static string? NormalizeCountry(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        var normalized = country.Trim().ToUpperInvariant();
        return normalized is "LV" or "LT" or "EE" ? normalized : null;
    }

    private static string BuildSafeFileName(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return string.Empty;
        }

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            safeFileName = safeFileName.Replace(invalidCharacter, '_');
        }

        return safeFileName.Trim();
    }

    private static bool IsPathInsideDirectory(string directoryPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var directoryRoot = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(filePath);
        return candidate.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase);
    }
}
