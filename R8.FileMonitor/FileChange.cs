namespace R8.FileMonitor;

public record FileChange
{
    public DateTime LastModified { get; set; }
    public string? Checksum { get; set; }
}