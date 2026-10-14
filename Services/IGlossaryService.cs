using System.Globalization;
using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IGlossaryService
{
    Task<IReadOnlyList<GlossaryTermView>> ListAsync(
        CultureInfo culture,
        CancellationToken cancellationToken = default);
}
