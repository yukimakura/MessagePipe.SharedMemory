using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Text;
using MessagePipe.SharedMemory.InternalClasses.Interfaces;
using DeepEqual.Syntax;

namespace MessagePipe.SharedMemory
{

    public sealed class SharedMemorySubscriber<TKey, TMessage> : IDistributedSubscriber<TKey, TMessage>
    {
        private readonly FilterAttachedAsyncMessageHandlerFactory asyncMessageHandlerFactory;
        private readonly FilterAttachedMessageHandlerFactory messageHandlerFactory;
        private readonly MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions;
        private readonly ISharedMemorySerializer sharedMemorySerializer;
        private readonly ICircularBuffer circularBuffer;


        public SharedMemorySubscriber(FilterAttachedAsyncMessageHandlerFactory asyncMessageHandlerFactory,
            FilterAttachedMessageHandlerFactory messageHandlerFactory,
            MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions,
            ISharedMemorySerializer sharedMemorySerializer,
            ICircularBuffer circularBuffer
            )
        {
            this.asyncMessageHandlerFactory = asyncMessageHandlerFactory;
            this.messageHandlerFactory = messageHandlerFactory;
            this.messagePipeSharedMemoryOptions = messagePipeSharedMemoryOptions;
            this.sharedMemorySerializer = sharedMemorySerializer;
            this.circularBuffer = circularBuffer;
        }

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, CancellationToken cancellationToken = default)
            => await SubscribeAsync(key, handler, Array.Empty<MessageHandlerFilter<TMessage>>(), cancellationToken);

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, MessageHandlerFilter<TMessage>[] filters, CancellationToken cancellationToken = default)
            => await subscribeAsyncBase(key, () => messageHandlerFactory.CreateMessageHandler(handler, filters));

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, CancellationToken cancellationToken = default)
            => await SubscribeAsync(key, handler, Array.Empty<AsyncMessageHandlerFilter<TMessage>>(), cancellationToken);

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, AsyncMessageHandlerFilter<TMessage>[] filters, CancellationToken cancellationToken = default)
            => await subscribeAsyncBase(key, () => asyncMessageHandlerFactory.CreateAsyncMessageHandler(handler, filters), cancellationToken);

        private async ValueTask<IAsyncDisposable> subscribeAsyncBase<MESSGAGEHANDLERTYPE>(TKey key, Func<MESSGAGEHANDLERTYPE> messagehandlerInjector, CancellationToken cancellationToken = default)
        {
            var latestTick = DateTime.Now.Ticks;
            var handler = messagehandlerInjector();
            var keyBin = sharedMemorySerializer.Serialize(key);
            var cancelTokenSrcFormDisposable = new CancellationTokenSource();
            if (cancellationToken != null)
                cancellationToken.Register(() => cancelTokenSrcFormDisposable.Cancel());
            Task.Run(async() =>
            {
                while (!cancelTokenSrcFormDisposable.IsCancellationRequested)
                {
                    try
                    {
                        var tickAndBody = circularBuffer.GetBodyAfterTick(latestTick);
                        foreach (var (tick, body) in tickAndBody)
                        {
                            byte[] currentHash = getHash(body);
                            if (tryDeserialize<TKey, TMessage>(body, out var deserializedKey, out var deserializedData))
                            {
                                //Keyと一致するメッセージのみをコールバックさせる
                                if (key.IsDeepEqual(deserializedKey))
                                {
                                    switch (handler)
                                    {
                                        case IAsyncMessageHandler<TMessage> asynchandler:
                                            await asynchandler.HandleAsync(deserializedData, CancellationToken.None).ConfigureAwait(false);
                                            break;
                                        case IMessageHandler<TMessage> synchandler:
                                            synchandler.Handle(deserializedData);
                                            break;
                                    }
                                }
                            }
                            latestTick = tick;
                        }
                    }
                    finally
                    {
                        await Task.Delay(messagePipeSharedMemoryOptions.SharedMemoryPoolingIntervalTimeMs);
                    }
                }
            });
            return new subscription(circularBuffer, cancelTokenSrcFormDisposable);
        }
        private bool tryDeserialize<KEYTYPE, DATATYPE>(Span<byte> rawData, out KEYTYPE key, out DATATYPE deserializedData)
        {
            if (!checkHash(rawData))
            {
                deserializedData = default;
                key = default;
                return false;
            }
            var baseIndex = 0;
            var keyLength = BitConverter.ToInt32(rawData[4..8]);
            baseIndex = 8;
            key = sharedMemorySerializer.Deserialize<KEYTYPE>(rawData[baseIndex..(baseIndex + keyLength)].ToArray());
            baseIndex = baseIndex + keyLength;
            var msgLength = BitConverter.ToInt32(rawData[baseIndex..(baseIndex + 4)]);
            baseIndex = baseIndex + 4;
            deserializedData = sharedMemorySerializer.Deserialize<DATATYPE>(rawData[baseIndex..(baseIndex + msgLength)].ToArray());
            return true;
        }

        private bool checkHash(Span<byte> rawData)
        {
            var rawDataHeaderHash = getHash(rawData);
            var calcdHash = Crc32.Hash(rawData[4..]);
            return rawDataHeaderHash.SequenceEqual(calcdHash);
        }

        private byte[] getHash(Span<byte> rawData)
            => rawData[..4].ToArray();

        private sealed class subscription : IAsyncDisposable
        {
            private readonly ICircularBuffer circularBuffer;
            private readonly CancellationTokenSource cancellationTokenSource;

            public subscription(ICircularBuffer circularBuffer, CancellationTokenSource cancellationTokenSource)
            {
                this.circularBuffer = circularBuffer;
                this.cancellationTokenSource = cancellationTokenSource;
            }

            public async ValueTask DisposeAsync()
            {
                cancellationTokenSource.Cancel();
                circularBuffer.Dispose();
            }
        }
    }
}

