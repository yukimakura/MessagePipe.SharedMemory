using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using System.Linq;
using System;
using System.Text;

namespace MessagePipe.SharedMemory
{

    public sealed class MessagePipeSharedMemoryOptions
    {

        public ISharedMemorySerializer SharedMemorySerializer { get; set; } = new JsonSharedMemorySerializer();
        public int PublishTimeOutMs { get; set; } = 100;
        public int PublishNodeCount { get; set; } = 50;
        public int SubscribeTimeOutMs {get; set;} = 100;
        public int SharedMemoryPoolingIntervalTimeMs { get; set; } = 1;

        public string QueueName {get; set;} = "messagepipesharedmemory";
        public int QueueSize { get; set;} = 10_000_000;
    }

}

