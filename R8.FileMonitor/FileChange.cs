using System;

namespace R8.FileMonitor
{
    public struct FileChange
    {
        public DateTime LastModified { get; set; }
        public string? Checksum { get; set; }
    }
}