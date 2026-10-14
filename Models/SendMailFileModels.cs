namespace DATA_EXCHANGE_HUB.Models;

public sealed record SendMailFileCountryAccess(
    string Country,
    string DirectoryPath,
    bool DirectoryExists,
    IReadOnlyList<SendMailDirectoryFile> Files);

public sealed record SendMailDirectoryFile(
    string Country,
    string FileName,
    long SizeBytes,
    DateTime LastWriteTime);
