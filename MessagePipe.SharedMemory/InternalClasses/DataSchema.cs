using System.Runtime.InteropServices;

namespace Namespace;
[StructLayout(LayoutKind.Explicit)]
public struct DataSchema
{
    [FieldOffset(0)]
    public long InsertDataTimeTick;
    [FieldOffset(8)]
    public long BeforeDataTimeArrayIndex;
    [FieldOffset(16)]
    public long DataByteSize;
    [FieldOffset(24)]
    public byte[] DataBody;
}
