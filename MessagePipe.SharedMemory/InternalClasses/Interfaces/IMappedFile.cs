using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace MessagePipe.SharedMemory.InternalClasses.Interfaces
{
    internal interface IMappedFile : IDisposable
    {
        MemoryMappedFile MappedFile { get; }
    }
}
