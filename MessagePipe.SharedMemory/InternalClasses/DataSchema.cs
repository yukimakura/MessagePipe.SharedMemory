using System.Runtime.InteropServices;

namespace MessagePipe.SharedMemory.InternalClasses
{

    [StructLayout(LayoutKind.Explicit)]
    public struct DataSchema
    {
        [FieldOffset(0)]
        public long InsertDataTimeTick;
        [FieldOffset(8)]
        public int BeforeDataTimeArrayIndex;
        [FieldOffset(12)]
        public int DataByteSize;
        [FieldOffset(16)]
        public byte[] DataBody;
    }
}