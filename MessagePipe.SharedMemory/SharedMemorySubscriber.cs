using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using SharedMemory;
using System.Linq;
using System;
using System.Text;

namespace MessagePipe.SharedMemory
{

    public sealed class SharedMemorySubscriber<TKey, TMessage> : IDistributedSubscriber<TKey, TMessage>
    {
        private readonly FilterAttachedAsyncMessageHandlerFactory asyncMessageHandlerFactory;
        private readonly FilterAttachedMessageHandlerFactory messageHandlerFactory;
        private readonly MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions;
        private readonly ISharedMemorySerializer sharedMemorySerializer;

        public SharedMemorySubscriber(FilterAttachedAsyncMessageHandlerFactory asyncMessageHandlerFactory,
            FilterAttachedMessageHandlerFactory messageHandlerFactory,
            MessagePipeSharedMemoryOptions messagePipeSharedMemoryOptions,
            ISharedMemorySerializer sharedMemorySerializer)
        {
            this.asyncMessageHandlerFactory = asyncMessageHandlerFactory;
            this.messageHandlerFactory = messageHandlerFactory;
            this.messagePipeSharedMemoryOptions = messagePipeSharedMemoryOptions;
            this.sharedMemorySerializer = sharedMemorySerializer;
        }

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, CancellationToken cancellationToken = default)
            => await SubscribeAsync(key, handler, Array.Empty<MessageHandlerFilter<TMessage>>(), cancellationToken);
        
        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IMessageHandler<TMessage> handler, MessageHandlerFilter<TMessage>[] filters, CancellationToken cancellationToken = default)
            => await subscribeAsyncBase(key,() => messageHandlerFactory.CreateMessageHandler(handler, filters));

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, CancellationToken cancellationToken = default)
            => await SubscribeAsync(key, handler, Array.Empty<AsyncMessageHandlerFilter<TMessage>>(), cancellationToken);

        public async ValueTask<IAsyncDisposable> SubscribeAsync(TKey key, IAsyncMessageHandler<TMessage> handler, AsyncMessageHandlerFilter<TMessage>[] filters, CancellationToken cancellationToken = default)
            => await subscribeAsyncBase(key,() => asyncMessageHandlerFactory.CreateAsyncMessageHandler(handler, filters),cancellationToken);

        private async ValueTask<IAsyncDisposable> subscribeAsyncBase<MESSGAGEHANDLERTYPE>(TKey key, Func<MESSGAGEHANDLERTYPE> messagehandlerInjector, CancellationToken cancellationToken = default)
        {
            var handler = messagehandlerInjector();
            var keyBin = sharedMemorySerializer.Serialize(key);
            var theClient = new CircularBuffer(UTF8Encoding.UTF8.GetString(keyBin));
            var cancelTokenSrcFormDisposable = new CancellationTokenSource();
            if(cancellationToken != null)
                cancellationToken.Register(() => cancelTokenSrcFormDisposable.Cancel());

            var buffer = new byte[theClient.NodeBufferSize];
            var latestHash = new byte[4];

            while (!cancelTokenSrcFormDisposable.IsCancellationRequested)
            {
                theClient.Read(buffer, messagePipeSharedMemoryOptions.SubscribeTimeOutMs);
                byte[] currentHash = getHash(buffer);
                try
                {
                    if (currentHash != latestHash)
                    {
                        if (tryDeserialize<TMessage>(buffer, out var deserializedData))
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
                }
                finally
                {
                    latestHash = currentHash;
                    await Task.Delay(messagePipeSharedMemoryOptions.SharedMemoryPoolingIntervalTimeMs);
                }
            }

            return new subscription(theClient,cancelTokenSrcFormDisposable);
        }
        private bool tryDeserialize<TYPE>(Span<byte> rawData, out TYPE deserializedData)
        {
            if (!checkHash(rawData))
            {
                deserializedData = default;
                return false;
            }

            var length = BitConverter.ToInt32(rawData[4..8]);
            deserializedData = sharedMemorySerializer.Deserialize<TYPE>(rawData[8..length].ToArray());
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
            private readonly CircularBuffer circularBuffer;
            private readonly CancellationTokenSource cancellationTokenSource;

            public subscription(CircularBuffer circularBuffer, CancellationTokenSource cancellationTokenSource)
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

