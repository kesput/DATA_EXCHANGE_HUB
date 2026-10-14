using DATA_EXCHANGE_HUB.Models;

namespace DATA_EXCHANGE_HUB.Services;

public interface IApisQvdPreviewService
{
    IReadOnlyList<ApisQvdRoot> ListRoots();

    IReadOnlyList<ApisQvdFile> ListFiles();

    ApisQvdPreview? ReadPreview(string? fileId, int maxRows);
}
