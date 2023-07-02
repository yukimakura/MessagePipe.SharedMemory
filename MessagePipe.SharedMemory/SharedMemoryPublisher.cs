using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using SharedMemory;
using System.Linq;
using System;
using System.Text;

namespace MessagePipe.SharedMemory
{

    public sealed class SharedMemoryPublisher<TKey, TMessage> : IDistributedPublisher<TKey, TMessage>
    {
        private readonly MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions;
        private readonly ISharedMemorySerializer sharedMemorySerializer;

        public SharedMemoryPublisher(MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions, ISharedMemorySerializer sharedMemorySerializer)
        {
            this.messagePipeSharedMemoryOptions = messagePipeSharedMemoryOptions;
            this.sharedMemorySerializer = sharedMemorySerializer;
        }

        public async ValueTask PublishAsync(TKey key, TMessage message, CancellationToken cancellationToken = default)
        {
            var keyBin = sharedMemorySerializer.Serialize(key);
            var messageBin = sharedMemorySerializer.Serialize(message);
            var messageAndLengthBin = BitConverter.GetBytes((int)messageBin.Length).Concat(messageBin).ToArray();
            var messageAndLengthHash = Crc32.Hash(messageAndLengthBin);
            var sendbody = messageAndLengthHash.Concat(messageAndLengthBin).ToArray();

            using (var theServer = new CircularBuffer(UTF8Encoding.UTF8.GetString(keyBin), messagePipeSharedMemoryOptions.PublishNodeCount, sizeof(byte) * sendbody.Length))
            {
                theServer.Write(sendbody, messagePipeSharedMemoryOptions.PublishTimeOutMs);
            }

        }
    }

}

