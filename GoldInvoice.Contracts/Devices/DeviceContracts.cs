namespace GoldInvoice.Contracts.Devices;

public sealed record DeviceSnapshotRequest(
    string Identifier,
    string DisplayName,
    string? Model,
    string Type);

public sealed record DeviceSynchronizationResult(
    int Added,
    int Updated,
    int MarkedOffline,
    int SkippedOwnedByAnotherUser);
