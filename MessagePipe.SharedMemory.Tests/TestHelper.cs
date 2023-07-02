using MessagePipe;
using MessagePipe.SharedMemory;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace MessagePipe.SharedMemory.Tests;
public static class TestHelper
{
    public static IServiceProvider BuildSharedMemoryServiceProvider()
    {
        var sc = new ServiceCollection();
        sc.AddMessagePipe();
        sc.AddMessagePipeSharedMemory();
        return sc.BuildServiceProvider();
    }
    public static IServiceProvider BuildSharedMemoryServiceProvider(MessagePipeSharedMemoryOptions options)
    {
        var sc = new ServiceCollection();
        sc.AddMessagePipe();
        sc.AddMessagePipeSharedMemory(opt => { opt = options; });
        return sc.BuildServiceProvider();
    }


}
