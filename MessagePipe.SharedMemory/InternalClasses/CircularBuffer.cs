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
    /// MemoryMappedFileにて循環バッファ機能を提供する
    /// 1フレームのレイアウト
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
            //memoryMappedFileはここでDisposeしない
            //(DIされていることから、他への影響を考慮している)
        }

        /// <summary>
        /// 指定したTickより新しいデータのリストを返す
        /// </summary>
        /// <param name="tick">指定するTick</param>
        /// <returns>指定したTickより新しいデータのリスト</returns>
        public IEnumerable<(long tick, byte[] body)> GetBodyAfterTick(long tick)
            => getAllBodyAndTick().OrderBy(x => x.tick).Where(x => x.tick > tick).ToList();

        /// <summary>
        /// 最新のTickを循環バッファから取得する
        /// 最新のTickがない(循環バッファが空)のときはNullを返す
        /// </summary>
        /// <returns>最新のTick(ない場合はNull)</returns>
        public long? GetLatestTickOrNull()
        {
            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //bodyが0 → 1つもバッファにデータが存在しないとする(すべてのbyteが0と仮定)
            if (latestFrame.bodyLength == 0)
                return null;

            return latestFrame.tick;
        }

        /// <summary>
        /// 新しいデータを循環バッファに挿入する
        /// </summary>
        /// <param name="data"></param>
        public void InsertNewData(byte[] data)
        {
            var startIndex = makeHeaderAndPutBuffer(memoryMappedFile, data, bufferSize);
            putLatestTickIndex(memoryMappedFile,startIndex,bufferSize);
        }
        /// <summary>
        /// 循環バッファから最新の1フレーム分のデータを取得する
        /// 循環バッファが空の場合はNullを返す
        /// </summary>
        /// <returns>最新の1フレーム分のデータ(ない場合はNull)</returns>
        private (int frameLength, int tickIndex, long tick, byte[] body)? getLatestFrameDataOrNull()
        {
            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //bodyが0 → 1つもバッファにデータが存在しないとする(すべてのbyteが0と仮定)
            if (latestFrame.bodyLength == 0)
                return null;

            return (latestFrame.bodyLength + headerSize, latestTickIndex, latestFrame.tick, latestFrame.body);
        }
        /// <summary>
        /// 循環バッファに存在するすべてのフレームのリストを返す
        /// </summary>
        /// <returns>循環バッファに存在するすべてのフレームのリスト</returns>
        private IEnumerable<(long tick, byte[] body)> getAllBodyAndTick()
        {
            var retData = new List<(long tick, byte[] body)>();

            var latestTickIndex = getLatestTickIndex(memoryMappedFile, bufferSize);
            var latestFrameRaw = takeOneFrameFromBuffer(memoryMappedFile, latestTickIndex, bufferSize);

            var latestFrame = deserializeFrame(latestFrameRaw);
            //bodyが0 → 1つもバッファにデータが存在しないとする(すべてのbyteが0と仮定)
            if (latestFrame.bodyLength == 0)
                return new List<(long tick, byte[] body)>();

            retData.Add((latestFrame.tick, latestFrame.body));

            //latestTickIndexと前のTickのIndexが0の場合はバッファに1つしかデータがないとする
            if (latestTickIndex == 0 && latestFrame.beforeTickIndex == 0)
                return retData;

            var beforeTickIndex = latestFrame.beforeTickIndex;
            while (true)
            {
                var frameRaw = takeOneFrameFromBuffer(memoryMappedFile, beforeTickIndex, bufferSize);
                var frame = deserializeFrame(frameRaw);

                //バッファを一周したことを考慮する→Tickが一致したらBreakすれば良い
                if (retData.Any(x => x.tick == frame.tick))
                    break;

                beforeTickIndex = frame.beforeTickIndex;
                retData.Add((frame.tick, frame.body));
            }

            return retData;
        }

        /// <summary>
        /// フレームのヘッダ部分を作成し、循環バッファにそれを挿入する
        /// 返り値に新規挿入データの配列の先頭インデックスが返る
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="bodyData">挿入するデータ本体(ヘッダは不要)</param>
        /// <param name="bufferSize">MemoryMappedFileのバッファサイズ</param>
        /// <returns>新規挿入データの配列の先頭インデックス</returns>
        private int makeHeaderAndPutBuffer(MemoryMappedFile mmf, byte[] bodyData, int bufferSize)
        {
            var tick = DateTime.Now.Ticks;
            var bodyLength = bodyData.Length;
            var latestFrame = getLatestFrameDataOrNull();
            var startIndex = 0;
            //バッファにデータが一つも存在しない場合
            if (latestFrame != null)
                startIndex = latestFrame.Value.tickIndex + latestFrame.Value.frameLength + 1;

            putBuffer(mmf, serializeFrame(tick, startIndex, bodyLength, bodyData), startIndex, bufferSize);

            return startIndex;
        }

        /// <summary>
        /// 循環バッファにデータを挿入する
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="putData">挿入したいバイト列</param>
        /// <param name="startIndex">循環バッファに設置する際の先頭インデックス</param>
        /// <param name="bufferSize">MemoryMappedFileのバッファサイズ</param>
        /// <exception cref="OverflowException">bufferSize以上のデータが挿入されたとき</exception>
        private void putBuffer(MemoryMappedFile mmf, byte[] putData, int startIndex, int bufferSize)
        {
            //bufferSize-4は最後の4byteに最新のtickが格納されているインデックスが格納されているため
            var maxEndIndex = bufferSize - 4;

            var length = putData.Length;

            //bufferから溢れた長さ
            var overflowLength = (startIndex + length) - maxEndIndex;

            //溢れたバイト数がバッファーサイズよりも大きい場合はException
            if (overflowLength >= maxEndIndex)
                throw new OverflowException("Data greater than the buffer size was specified. Either the buffer size must be increased or the data must be reduced.");

            //overflowLengthが0以上なら頭出ししてそこからのデータを結合する
            if (overflowLength > 0)
                //循環バッファによる溢れ処理
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
        /// 循環バッファから1フレーム分のByte列を取得する
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="startIndex">読み取りたいフレームの先頭インデックス</param>
        /// <param name="bufferSize">MemoryMappedFileのバッファサイズ</param>
        /// <returns>1フレーム分のByte列</returns>
        /// <exception cref="OverflowException">bufferSize以上のデータが挿入されたとき</exception>
        private byte[] takeOneFrameFromBuffer(MemoryMappedFile mmf, int startIndex, int bufferSize)
        {
            return memoryMappedFileWrapper(mmf, accessor =>
            {
                var rawbuffer = new byte[bufferSize];
                accessor.ReadArray(0, rawbuffer, 0, bufferSize);

                //bufferSize-4は最後の4byteに最新のtickが格納されているインデックスが格納されているため
                var maxEndIndex = bufferSize - 4;

                var headerData = new byte[headerSize];
                //1フレームあたりのデータ長を求める
                //ヘッダーを解析
                //ヘッダーが循環バッファにて分割された場合
                if ((startIndex + headerSize) > maxEndIndex)
                {
                    var overflowHeaderLength = (startIndex + headerSize) - maxEndIndex;
                    headerData = rawbuffer[startIndex..(headerSize - overflowHeaderLength)].ToArray()
                                            .Concat(rawbuffer[..overflowHeaderLength]).ToArray();
                }
                //循環バッファにて分割されない場合
                else
                    headerData = rawbuffer[startIndex..(startIndex + headerSize)];

                var length = BitConverter.ToInt32(headerData[12..16]) + headerSize;

                //bufferから溢れた長さ
                var overflowLength = (startIndex + length) - maxEndIndex;

                //溢れたバイト数がバッファーサイズよりも大きい場合はException
                if (overflowLength >= maxEndIndex)
                    throw new OverflowException("Data greater than the buffer size was specified. Either the buffer size must be increased or the data must be reduced.");

                //overflowLengthが0以上なら頭出ししてそこからのデータを結合する
                if (overflowLength > 0)
                    //循環バッファによる溢れ処理
                    return rawbuffer[startIndex..maxEndIndex].ToArray().Concat(rawbuffer[..overflowLength].ToArray()).ToArray();
                else
                    return rawbuffer[startIndex..(startIndex + length)].ToArray();

            });
        }

        /// <summary>
        /// 1フレーム分のバイト列からデシリアライズする
        /// </summary>
        /// <param name="rawBytes">デシリアライズする生Byte列</param>
        /// <returns>デシリアライズされたデータ</returns>
        private (long tick, int beforeTickIndex, int bodyLength, byte[] body) deserializeFrame(ReadOnlySpan<byte> rawBytes)
            => (BitConverter.ToInt64(rawBytes[..8]), BitConverter.ToInt32(rawBytes[8..12]), BitConverter.ToInt32(rawBytes[12..16]), rawBytes[16..].ToArray());

        /// <summary>
        /// 1フレーム分のデータをbyte列にシリアライズする
        /// </summary>
        /// <param name="tick">Tick</param>
        /// <param name="beforeTickIndex">前のデータのバイト列</param>
        /// <param name="bodyLength">bodyの長さ</param>
        /// <param name="body">データ本体</param>
        /// <returns>生Byte列</returns>
        private byte[] serializeFrame(long tick, int beforeTickIndex, int bodyLength, byte[] body)
           => BitConverter.GetBytes(tick).Concat(BitConverter.GetBytes(beforeTickIndex)).Concat(BitConverter.GetBytes(bodyLength)).Concat(body).ToArray();

        /// <summary>
        /// MemoryMappedFileにアクセスするためのラッパー
        /// </summary>
        /// <typeparam name="T">返り値の型</typeparam>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="mmfAccessAction">MMFにアクセスする任意の処理</param>
        /// <returns>MMFの任意の処理の結果</returns>
        private T memoryMappedFileWrapper<T>(MemoryMappedFile mmf, Func<MemoryMappedViewAccessor, T> mmfAccessAction)
        {
            using (var accessor = mmf.CreateViewAccessor())
            {
                return mmfAccessAction(accessor);
            }
        }

        /// <summary>
        /// MemoryMappedFileにアクセスするためのラッパー
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="mmfAccessAction">MMFにアクセスする任意の処理</param>
        private void memoryMappedFileWrapper(MemoryMappedFile mmf, Action<MemoryMappedViewAccessor> mmfAccessAction)
        {
            using (var accessor = mmf.CreateViewAccessor())
            {
                mmfAccessAction(accessor);
            }
        }

        /// <summary>
        /// 循環バッファから最新のTickを取得する
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="bufferSize">MemoryMappedFileのバッファサイズ</param>
        /// <returns>循環バッファから取得した最新のTick</returns>
        private int getLatestTickIndex(MemoryMappedFile mmf, int bufferSize)
            => memoryMappedFileWrapper(mmf, accessor =>
            {
                var rawbuffer = new byte[4];
                accessor.ReadArray(bufferSize - 4, rawbuffer, 0, 4);

                return BitConverter.ToInt32(rawbuffer);
            });

        /// <summary>
        /// 循環バッファに最新のTickを書き込む
        /// </summary>
        /// <param name="mmf">MemoryMappedFile</param>
        /// <param name="latestTickIndex">書き込む最新のTick</param>
        /// <param name="bufferSize">循環バッファから最新のTick</param>
        private void putLatestTickIndex(MemoryMappedFile mmf, int latestTickIndex, int bufferSize)
        {
            memoryMappedFileWrapper(mmf, accessor =>
            {
                accessor.WriteArray(bufferSize - 4, BitConverter.GetBytes(latestTickIndex), 0, 4);
            });
        }
    }
}
