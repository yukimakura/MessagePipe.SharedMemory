using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace MessagePipe.SharedMemory.InternalClasses.Interfaces
{

    public interface ICircularBuffer : IDisposable
    {
        /// <summary>
        /// CircularBufferに新たにデータを挿入する
        /// </summary>
        /// <param name="data"></param>
        void InsertNewData(byte[] data);
        /// <summary>
        /// tick以降のデータを取得する
        /// </summary>
        /// <param name="tick"></param>
        /// <returns></returns>
        IEnumerable<(long tick, byte[] body)> GetBodyAfterTick(long tick);

        /// <summary>
        /// 最新のtickを取得する
        /// mappedFileに何も情報がない場合はNullを返す
        /// </summary>
        /// <returns></returns>
        long? GetLatestTick();


    }
}
