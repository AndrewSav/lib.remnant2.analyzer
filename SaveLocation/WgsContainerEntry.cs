// Taken from https://github.com/13xforever/wgs-exporter
namespace lib.remnant2.analyzer.SaveLocation;

public sealed record WgsContainerEntry(
    string Filename,
    string Revision,
    byte ContainerId,
    Guid ContainerFolder,
    ulong Timestamp,
    int FileSize);
