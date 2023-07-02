using MessagePack;
using MessagePack.Resolvers;

namespace MessagePipe.SharedMemory
{

     public sealed class MessagePackSharedMemorySerializer : ISharedMemorySerializer
    {
        readonly MessagePackSerializerOptions options;

        public MessagePackSharedMemorySerializer()
        {
            options = ContractlessStandardResolver.Options;
        }

        public MessagePackSharedMemorySerializer(MessagePackSerializerOptions options)
        {
            this.options = options;
        }

        public byte[] Serialize<T>(T value)
        {
            if (value is byte[] xs) return xs;
            return MessagePackSerializer.Serialize<T>(value, options);
        }

        public T Deserialize<T>(byte[] value)
        {
            return MessagePackSerializer.Deserialize<T>(value);
        }
    }

}

