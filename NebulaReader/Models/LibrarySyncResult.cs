namespace NebulaReader.Models;

public sealed record LibrarySyncResult(int Added, int Removed, int Unchanged, int Failed,
    int UnavailableFolders, bool Cancelled);
