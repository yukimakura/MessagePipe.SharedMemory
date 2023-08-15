using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Text;
using MessagePipe.SharedMemory.InternalClasses.Interfaces;

namespace MessagePipe.SharedMemory
{

    public sealed class SharedMemoryPublisher<TKey, TMessage> : IDistributedPublisher<TKey, TMessage>
    {
        private readonly MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions;
        private readonly ISharedMemorySerializer sharedMemorySerializer;
        private ICircularBuffer circularBuffer;

        public SharedMemoryPublisher(MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions, ISharedMemorySerializer sharedMemorySerializer,ICircularBuffer circularBuffer)
        {
            this.messagePipeSharedMemoryOptions = messagePipeSharedMemoryOptions;
            this.sharedMemorySerializer = sharedMemorySerializer;
            this.circularBuffer = circularBuffer;
        }

        public async ValueTask PublishAsync(TKey key, TMessage message, CancellationToken cancellationToken = default)
        {
            var keyBin = sharedMemorySerializer.Serialize(key);
            var keyLength = keyBin.Length;
            var messageBin = sharedMemorySerializer.Serialize(message);
            var keyLengthAndKeyAndMsgLengthAndMsgBin = BitConverter.GetBytes(keyLength)
                                                        .Concat(keyBin)
                                                        .Concat(BitConverter.GetBytes(messageBin.Length))
                                                        .Concat(messageBin)
                                                        .ToArray();
            var messageAndLengthHash = Crc32.Hash(keyLengthAndKeyAndMsgLengthAndMsgBin);
            var sendbody = messageAndLengthHash.Concat(keyLengthAndKeyAndMsgLengthAndMsgBin).ToArray();

            circularBuffer.InsertNewData(sendbody);
        }
    }

}

