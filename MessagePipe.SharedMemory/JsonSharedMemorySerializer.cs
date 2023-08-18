using MessagePack;
using MessagePack.Resolvers;

namespace MessagePipe.SharedMemory
{

     public sealed class JsonSharedMemorySerializer : ISharedMemorySerializer
    {

        public JsonSharedMemorySerializer()
        {
        }


        public byte[] Serialize<T>(T value)
            => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);

        public T Deserialize<T>(byte[] value)
            => System.Text.Json.JsonSerializer.Deserialize<T>(value);
    }

}

