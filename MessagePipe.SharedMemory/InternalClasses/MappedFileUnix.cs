using MessagePipe.SharedMemory.InternalClasses.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace MessagePipe.SharedMemory.InternalClasses
{


    internal sealed class MappedFileUnix : IMappedFile
    {
        private const FileAccess FileAccessOption = FileAccess.ReadWrite;
        private const FileShare FileShareOption = FileShare.ReadWrite | FileShare.Delete;
        private const string Folder = ".cloudtoid/interprocess/mmf";
        private const string FileExtension = ".qu";
        private const int BufferSize = 0x1000;
        private readonly string file;
        private readonly ILogger<MappedFileUnix> logger;

        internal MappedFileUnix(QueueOptions options)
        {
            file = Path.Combine(options.Path, Folder);
            Directory.CreateDirectory(file);
            file = Path.Combine(file, options.QueueName + FileExtension);

            FileStream stream;

            if (IsFileInUse(file))
            {
                // just open the file

                stream = new FileStream(
                    file,
                    FileMode.Open, // just open it
                    FileAccessOption,
                    FileShareOption,
                    BufferSize);
            }
            else
            {
                // override (or create if no longer exist) as it is not being used

                stream = new FileStream(
                    file,
                    FileMode.Create,
                    FileAccessOption,
                    FileShareOption,
                    BufferSize);
            }

            try
            {
                MappedFile = MemoryMappedFile.CreateFromFile(
                    stream,
                    mapName: null, // do not set this or it will not work on Linux/Unix/MacOS
                    options.GetQueueStorageSize(),
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    false);
            }
            catch
            {
                // do not leave any resources hanging

                try
                {
                    stream.Dispose();
                }
                catch
                {
                    ResetBackingFile();
                }

                throw;
            }
        }

        ~MappedFileUnix()
           => Dispose(false);

        public MemoryMappedFile MappedFile { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                    MappedFile.Dispose();
            }
            finally
            {
                ResetBackingFile();
            }
        }

        private void ResetBackingFile()
        {
            // Deletes the backing file if it is not used by any other process

            if (IsFileInUse(file))
                return;

            if (!tryDeleteFile(file))
                logger.LogError("Failed to delete queue's shared memory backing file even though it is not in use by any process.");
        }

        private static bool IsFileInUse(string file)
        {
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private bool tryDeleteFile(string file)
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch (Exception ex) when (!isFatal(ex))
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if <paramref name="exception"/> is considered a fatal exception such as <see cref="ThreadAbortException"/>,
        /// <see cref="AccessViolationException"/>, <see cref="SEHException"/>, <see cref="StackOverflowException"/>,
        /// <see cref="TypeInitializationException"/>, or <see cref="OutOfMemoryException"/> but not
        /// <see cref="InsufficientMemoryException"/>
        /// from (https://github.com/cloudtoid/framework/blob/master/src/Cloudtoid.Framework/Extensions/ExceptionExtensions.cs)
        /// </summary>
        public bool isFatal(Exception exception)
        {

            var ex = (Exception?)exception;
            while (ex != null)
            {
                // Unlike OutOfMemoryException, InsufficientMemoryException is thrown before starting an operation, and
                // thus does not imply state corruption. An application can catch this exception, throttle back its
                // memory usage, and avoid actual out of memory conditions and their potential for corrupting program state.
                if (ex is OutOfMemoryException && !(ex is InsufficientMemoryException))
                    return true;

                if (ex is ThreadAbortException
                    || ex is AccessViolationException
                    || ex is SEHException
                    || ex is StackOverflowException
                    || ex is TypeInitializationException)
                {
                    return true;
                }

                // These exceptions aren't fatal in themselves, but the CLR uses them
                // to wrap other exceptions, so we want to look deeper
                if (ex is TargetInvocationException tie)
                {
                    ex = tie.InnerException;
                }
                else if (ex is AggregateException aex)
                {
                    // AggregateException can contain other AggregateExceptions in its InnerExceptions list so we
                    // flatten it first. That will essentially create a list of exceptions from the AggregateException's
                    // InnerExceptions property in such a way that any exception other than AggregateException is put
                    // into this list. If there is an AggregateException then exceptions from it's InnerExceptions list are
                    // put into this new list etc. Then a new instance of AggregateException with this flattened list is returned.
                    //
                    // AggregateException InnerExceptions list is immutable after creation and the walk happens only for
                    // the InnerExceptions property of AggregateException and not InnerException of the specific exceptions.
                    // This means that the only way to have a circular referencing here is through reflection and forward-
                    // reference assignment which would be insane. In such case we would also run into stack overflow
                    // when tracing out the exception since AggregateException's ToString does not have any protection there.
                    //
                    // On that note that's another reason why we want to flatten here as opposed to just let recursion do its magic
                    // since in an unlikely case there is a circle we'll get OutOfMemory here instead of StackOverflow which is
                    // a lesser of the two evils.
                    var faex = aex.Flatten();
                    var iexs = faex.InnerExceptions;
                    if (iexs != null && iexs.Any(isFatal))
                        return true;

                    ex = ex.InnerException;
                }
                else
                {
                    break;
                }
            }

            return false;
        }


    }

}