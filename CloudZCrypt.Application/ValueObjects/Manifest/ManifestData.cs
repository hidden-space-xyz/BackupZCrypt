namespace CloudZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestData(ManifestHeader Header, Dictionary<string, string> FileMap);
