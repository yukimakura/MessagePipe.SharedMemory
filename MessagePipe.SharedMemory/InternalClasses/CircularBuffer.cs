using System.Collections.Generic;
using MessagePipe.SharedMemory.InternalClasses.Interfaces;

namespace MessagePipe.SharedMemory.InternalClasses
{
    public class CircularBuffer : ICircularBuffer
    {
        public void Dispose()
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<(long tick, byte[] body)> GetBodyAfterTick(long tick)
        {
            throw new System.NotImplementedException();
        }

        public long? GetLatestTick()
        {
            throw new System.NotImplementedException();
        }

        public void InsertNewData(byte[] data)
        {
            throw new System.NotImplementedException();
        }
    }
}
