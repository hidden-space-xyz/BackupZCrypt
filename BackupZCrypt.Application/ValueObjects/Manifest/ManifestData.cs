namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestData(ManifestHeader Header, Dictionary<string, string> FileMap);
