using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using SharedMemory;
using System.Linq;
using System;
using System.Text;

namespace MessagePipe.SharedMemory
{

    public sealed class MessagePipeSharedMemoryOptions
    {

        public ISharedMemorySerializer SharedMemorySerializer { get; set; } = new MessagePackSharedMemorySerializer();
        public int PublishTimeOutMs { get; set; } = 100;
        public int PublishNodeCount { get; set; } = 50;
        public int SubscribeTimeOutMs {get; set;} = 100;
        public int SharedMemoryPoolingIntervalTimeMs { get; set; } = 1;
    }

}

