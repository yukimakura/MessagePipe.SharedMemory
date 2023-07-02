namespace MessagePipe.SharedMemory
{

    public interface ISharedMemorySerializer
    {
        byte[] Serialize<T>(T value);
        T Deserialize<T>(byte[] value);
    }
}
