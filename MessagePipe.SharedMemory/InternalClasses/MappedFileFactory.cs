using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Namespace.Interfaces;
namespace Namespace;
public class MappedFileFactory
{
    public MemoryMappedFile Create(QueueOptions options)
    {
        IMappedFile mappedFileGenerator = default(IMappedFile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            mappedFileGenerator = new MappedFileWindows(options);
        else
            mappedFileGenerator = new MappedFileUnix(options);

        return mappedFileGenerator.MappedFile;
    }

}

