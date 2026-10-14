using System.Text.RegularExpressions;

namespace DATA_EXCHANGE_HUB.Services;

internal static partial class SqlName
{
    public static string NormalizeTableName(string configuredName, string fallback)
    {
        return NormalizeObjectName(configuredName, fallback, maxParts: 3);
    }

    public static string NormalizeObjectName(
        string configuredName,
        string fallback,
        int maxParts = 4)
    {
        var rawName = string.IsNullOrWhiteSpace(configuredName) ? fallback : configuredName;
        var parts = rawName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 1 || parts.Length > maxParts)
        {
            throw new InvalidOperationException($"Netinkamas SQL lenteles pavadinimas: {rawName}");
        }

        return string.Join(".", parts.Select(NormalizePart));
    }

    public static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new InvalidOperationException("SQL identifikatorius tuscias.");
        }

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string NormalizePart(string part)
    {
        var cleanPart = part.Trim('[', ']');

        if (!SqlIdentifierPattern().IsMatch(cleanPart))
        {
            throw new InvalidOperationException($"Netinkamas SQL identifikatorius: {part}");
        }

        return $"[{cleanPart}]";
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SqlIdentifierPattern();
}
