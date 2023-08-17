using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DeepEqual.Syntax;
using MessagePipe.SharedMemory.InternalClasses.Interfaces;

namespace MessagePipe.SharedMemory.InternalClasses
{
    /// <summary>
    /// MemoryMappedFile�ɂďz�o�b�t�@�@�\��񋟂���
    /// 1�t���[���̃��C�A�E�g
    /// | tick 8byte | before tick index 4byte | body size 4byte | body |
    /// </summary>
    public class CircularBuffer : ICircularBuffer
    {

        private MemoryMappedFile memoryMappedFile;
        private int bufferSize;
        private const int headerSize = 16; //tick(8byte)+beforeTickIndex(4byte)+bodySize(4byte)=16byte


        public CircularBuffer(MemoryMappedFile memoryMappedFile, int bufferSize)
        {
            this.memoryMappedFile = memoryMappedFile;
            this.bufferSize = bufferSize;
        }

        public void Dispose()
        {
            //memoryMappedFile�͂�����Dispose���Ȃ�
            //(DI����Ă��邱�Ƃ���A���ւ̉e�����l�����Ă���)
        }

        /// <summary>
        /// �w�肵��Tick���V�����f�[�^�̃��X�g��Ԃ�
        /// </summary>
        /// <param name="tick">�w�肷��Tick</param>
        /// <returns>�w�肵��Tick���V�����f�[�^�̃��X�g</returns>
        public IEnumerable<(long tick, byte[] body)> GetBodyAfterTick(long tick)
            => getAllBodyAndTick().OrderBy(x => x.tick).Where(x => x.tick > tick).ToList();

        /// <summary>
        /// �ŐV��Tick���z�o�b�t�@����擾����
        /// �ŐV��Tick���Ȃ�(�z�o�b�t�@����)�̂Ƃ���Null��Ԃ�
        /// </summary>
        /// <returns>�ŐV��Tick(�Ȃ��ꍇ��Null)</returns>
        public long? GetLatestTickOrNull()
        {
            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //body��0 �� 1���o�b�t�@�Ƀf�[�^�����݂��Ȃ��Ƃ���(���ׂĂ�byte��0�Ɖ���)
            if (latestFrame.bodyLength == 0)
                return null;

            return latestFrame.tick;
        }

        /// <summary>
        /// �V�����f�[�^���z�o�b�t�@�ɑ}������
        /// </summary>
        /// <param name="data"></param>
        public void InsertNewData(byte[] data)
        {
            var startIndex = makeHeaderAndPutBuffer(memoryMappedFile, data, bufferSize);
            putLatestTickIndex(memoryMappedFile,startIndex,bufferSize);
        }
        /// <summary>
        /// �z�o�b�t�@����ŐV��1�t���[�����̃f�[�^���擾����
        /// �z�o�b�t�@����̏ꍇ��Null��Ԃ�
        /// </summary>
        /// <returns>�ŐV��1�t���[�����̃f�[�^(�Ȃ��ꍇ��Null)</returns>
        private (int frameLength, int tickIndex, long tick, byte[] body)? getLatestFrameDataOrNull()
        {
            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //body��0 �� 1���o�b�t�@�Ƀf�[�^�����݂��Ȃ��Ƃ���(���ׂĂ�byte��0�Ɖ���)
            if (latestFrame.bodyLength == 0)
                return null;

            return (latestFrame.bodyLength + headerSize, latestTickIndex, latestFrame.tick, latestFrame.body);
        }
        /// <summary>
        /// �z�o�b�t�@�ɑ��݂��邷�ׂẴt���[���̃��X�g��Ԃ�
        /// </summary>
        /// <returns>�z�o�b�t�@�ɑ��݂��邷�ׂẴt���[���̃��X�g</returns>
        private IEnumerable<(long tick, byte[] body)> getAllBodyAndTick()
        {
            var retData = new List<(long tick, byte[] body)>();

            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //body��0 �� 1���o�b�t�@�Ƀf�[�^�����݂��Ȃ��Ƃ���(���ׂĂ�byte��0�Ɖ���)
            if (latestFrame.bodyLength == 0)
                return new List<(long tick, byte[] body)>();

            retData.Add((latestFrame.tick, latestFrame.body));

            //latestTickIndex�ƑO��Tick��Index��0�̏ꍇ�̓o�b�t�@��1�����f�[�^���Ȃ��Ƃ���
            if (latestTickIndex == 0 && latestFrame.beforeTickIndex == 0)
                return retData;

            var beforeTickIndex = latestFrame.beforeTickIndex;
            while (true)
            {
                var frameRaw = takeOneFrameFromBuffer(memoryMappedFile, beforeTickIndex, bufferSize);
                var frame = deserializeFrame(frameRaw);

                //�o�b�t�@������������Ƃ��l�����遨Tick����v������Break����Ηǂ�
                if (retData.Any(x => x.tick == frame.tick))
                    break;

                beforeTickIndex = frame.beforeTickIndex;
                retData.Add((frame.tick, frame.body));
            }

            return retData;
        }

        /// <summary>
        /// �t���[���̃w�b�_�������쐬���A�z�o�b�t�@�ɂ����}������
        /// �Ԃ�l�ɐV�K�}���f�[�^�̔z��̐擪�C���f�b�N�X���Ԃ�
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="bodyData">�}������f�[�^�{��(�w�b�_�͕s�v)</param>
        /// <param name="bufferSize">MemoryMappedFile�̃o�b�t�@�T�C�Y</param>
        /// <returns>�V�K�}���f�[�^�̔z��̐擪�C���f�b�N�X</returns>
        private int makeHeaderAndPutBuffer(MemoryMappedFile mmf, byte[] bodyData, int bufferSize)
        {
            var tick = DateTime.Now.Ticks;
            var bodyLength = bodyData.Length;
            var latestFrame = getLatestFrameDataOrNull();
            var startIndex = 0;
            //�o�b�t�@�Ƀf�[�^��������݂��Ȃ��ꍇ
            if (latestFrame != null)
                startIndex = latestFrame.Value.tickIndex + latestFrame.Value.frameLength + 1;

            putBuffer(mmf, serializeFrame(tick, latestFrame.Value.tickIndex, bodyLength, bodyData), startIndex, bufferSize);

            return startIndex;
        }

        /// <summary>
        /// �z�o�b�t�@�Ƀf�[�^��}������
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="putData">�}���������o�C�g��</param>
        /// <param name="startIndex">�z�o�b�t�@�ɐݒu����ۂ̐擪�C���f�b�N�X</param>
        /// <param name="bufferSize">MemoryMappedFile�̃o�b�t�@�T�C�Y</param>
        /// <exception cref="OverflowException">bufferSize�ȏ�̃f�[�^���}�����ꂽ�Ƃ�</exception>
        private void putBuffer(MemoryMappedFile mmf, byte[] putData, int startIndex, int bufferSize)
        {
            //bufferSize-4�͍Ō��4byte�ɍŐV��tick���i�[����Ă���C���f�b�N�X���i�[����Ă��邽��
            var maxEndIndex = bufferSize - 4;

            var length = putData.Length;

            //buffer�����ꂽ����
            var overflowLength = (startIndex + length) - maxEndIndex;

            //��ꂽ�o�C�g�����o�b�t�@�[�T�C�Y�����傫���ꍇ��Exception
            if (overflowLength >= maxEndIndex)
                throw new OverflowException("Data greater than the buffer size was specified. Either the buffer size must be increased or the data must be reduced.");

            //overflowLength��0�ȏ�Ȃ瓪�o�����Ă�������̃f�[�^����������
            if (overflowLength > 0)
                //�z�o�b�t�@�ɂ���ꏈ��
                memoryMappedFileWrapper(mmf, accessor =>
                {
                    var beforeLength = length - overflowLength;
                    accessor.WriteArray(startIndex, putData[..beforeLength], 0, beforeLength);
                    accessor.WriteArray(0, putData[beforeLength..], beforeLength, overflowLength);
                });
            else
                memoryMappedFileWrapper(mmf, accessor =>
                {
                    accessor.WriteArray(startIndex, putData, 0, length);
                });


        }
        /// <summary>
        /// �z�o�b�t�@����1�t���[������Byte����擾����
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="startIndex">�ǂݎ�肽���t���[���̐擪�C���f�b�N�X</param>
        /// <param name="bufferSize">MemoryMappedFile�̃o�b�t�@�T�C�Y</param>
        /// <returns>1�t���[������Byte��</returns>
        /// <exception cref="OverflowException">bufferSize�ȏ�̃f�[�^���}�����ꂽ�Ƃ�</exception>
        private byte[] takeOneFrameFromBuffer(MemoryMappedFile mmf, int startIndex, int bufferSize)
        {
            return memoryMappedFileWrapper(mmf, accessor =>
            {
                var rawbuffer = new byte[bufferSize];
                accessor.ReadArray(0, rawbuffer, 0, bufferSize);

                //bufferSize-4�͍Ō��4byte�ɍŐV��tick���i�[����Ă���C���f�b�N�X���i�[����Ă��邽��
                var maxEndIndex = bufferSize - 4;

                var headerData = new byte[headerSize];
                //1�t���[��������̃f�[�^�������߂�
                //�w�b�_�[�����
                //�w�b�_�[���z�o�b�t�@�ɂĕ������ꂽ�ꍇ
                if ((startIndex + headerSize) > maxEndIndex)
                {
                    var overflowHeaderLength = (startIndex + headerSize) - maxEndIndex;
                    headerData = rawbuffer[startIndex..(headerSize - overflowHeaderLength)].ToArray()
                                            .Concat(rawbuffer[..overflowHeaderLength]).ToArray();
                }
                //�z�o�b�t�@�ɂĕ�������Ȃ��ꍇ
                else
                    headerData = rawbuffer[startIndex..(startIndex + headerSize)];

                var length = BitConverter.ToInt32(headerData[12..16]) + headerSize;

                //buffer�����ꂽ����
                var overflowLength = (startIndex + length) - maxEndIndex;

                //��ꂽ�o�C�g�����o�b�t�@�[�T�C�Y�����傫���ꍇ��Exception
                if (overflowLength >= maxEndIndex)
                    throw new OverflowException("Data greater than the buffer size was specified. Either the buffer size must be increased or the data must be reduced.");

                //overflowLength��0�ȏ�Ȃ瓪�o�����Ă�������̃f�[�^����������
                if (overflowLength > 0)
                    //�z�o�b�t�@�ɂ���ꏈ��
                    return rawbuffer[startIndex..maxEndIndex].ToArray().Concat(rawbuffer[..overflowLength].ToArray()).ToArray();
                else
                    return rawbuffer[startIndex..(startIndex + length)].ToArray();

            });
        }

        /// <summary>
        /// 1�t���[�����̃o�C�g�񂩂�f�V���A���C�Y����
        /// </summary>
        /// <param name="rawBytes">�f�V���A���C�Y���鐶Byte��</param>
        /// <returns>�f�V���A���C�Y���ꂽ�f�[�^</returns>
        private (long tick, int beforeTickIndex, int bodyLength, byte[] body) deserializeFrame(ReadOnlySpan<byte> rawBytes)
            => (BitConverter.ToInt64(rawBytes[..8]), BitConverter.ToInt32(rawBytes[8..12]), BitConverter.ToInt32(rawBytes[12..16]), rawBytes[16..].ToArray());

        /// <summary>
        /// 1�t���[�����̃f�[�^��byte��ɃV���A���C�Y����
        /// </summary>
        /// <param name="tick">Tick</param>
        /// <param name="beforeTickIndex">�O�̃f�[�^�̃o�C�g��</param>
        /// <param name="bodyLength">body�̒���</param>
        /// <param name="body">�f�[�^�{��</param>
        /// <returns>��Byte��</returns>
        private byte[] serializeFrame(long tick, int beforeTickIndex, int bodyLength, byte[] body)
           => BitConverter.GetBytes(tick).Concat(BitConverter.GetBytes(beforeTickIndex)).Concat(BitConverter.GetBytes(bodyLength)).Concat(body).ToArray();

        /// <summary>
        /// MemoryMappedFile�ɃA�N�Z�X���邽�߂̃��b�p�[
        /// </summary>
        /// <typeparam name="T">�Ԃ�l�̌^</typeparam>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="mmfAccessAction">MMF�ɃA�N�Z�X����C�ӂ̏���</param>
        /// <returns>MMF�̔C�ӂ̏����̌���</returns>
        private T memoryMappedFileWrapper<T>(MemoryMappedFile mmf, Func<MemoryMappedViewAccessor, T> mmfAccessAction)
        {
            using (var accessor = mmf.CreateViewAccessor())
            {
                return mmfAccessAction(accessor);
            }
        }

        /// <summary>
        /// MemoryMappedFile�ɃA�N�Z�X���邽�߂̃��b�p�[
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="mmfAccessAction">MMF�ɃA�N�Z�X����C�ӂ̏���</param>
        private void memoryMappedFileWrapper(MemoryMappedFile mmf, Action<MemoryMappedViewAccessor> mmfAccessAction)
        {
            using (var accessor = mmf.CreateViewAccessor())
            {
                mmfAccessAction(accessor);
            }
        }

        /// <summary>
        /// �z�o�b�t�@����ŐV��Tick���擾����
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="bufferSize">MemoryMappedFile�̃o�b�t�@�T�C�Y</param>
        /// <returns>�z�o�b�t�@����擾�����ŐV��Tick</returns>
        private int getLatestTickIndex(MemoryMappedFile mmf, int bufferSize)
            => memoryMappedFileWrapper(mmf, accessor =>
            {
                var rawbuffer = new byte[4];
                accessor.ReadArray(bufferSize - 4, rawbuffer, 0, 4);

                return BitConverter.ToInt32(rawbuffer);
            });

        /// <summary>
        /// �z�o�b�t�@�ɍŐV��Tick����������
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="latestTickIndex">�������ލŐV��Tick</param>
        /// <param name="bufferSize">�z�o�b�t�@����ŐV��Tick</param>
        private void putLatestTickIndex(MemoryMappedFile mmf, int latestTickIndex, int bufferSize)
        {
            memoryMappedFileWrapper(mmf, accessor =>
            {
                accessor.WriteArray(bufferSize - 4, BitConverter.GetBytes(latestTickIndex), 0, 4);
            });
        }
    }
}
