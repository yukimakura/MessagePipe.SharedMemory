using System.IO.MemoryMappedFiles;
using Cloudtoid;
using Microsoft.Extensions.Logging;
using Namespace.Interfaces;
namespace Namespace;
internal sealed class MappedFileWindows : IMappedFile
{
    private const string MapNamePrefix = "CT_IP_";

    internal MappedFileWindows(QueueOptions options)
    {
#if NET5_0_OR_GREATER
        if (!System.OperatingSystem.IsWindows())
            throw new System.PlatformNotSupportedException();
#endif
        MappedFile = MemoryMappedFile.CreateOrOpen(
            mapName: MapNamePrefix + options.QueueName,
            options.GetQueueStorageSize(),
            MemoryMappedFileAccess.ReadWrite,
            MemoryMappedFileOptions.None,
            HandleInheritability.None);
    }

    public MemoryMappedFile MappedFile { get; }

    public void Dispose()
        => MappedFile.Dispose();
}