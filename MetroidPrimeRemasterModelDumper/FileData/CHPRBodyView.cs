using System;
using System.IO;

namespace DKCTF
{
    /// <summary>
    /// Random-access view of a bounded CHPR body backed by the original stream.
    /// Uses a small cache to avoid allocating a body-sized byte array.
    /// </summary>
    internal sealed class CHPRBodyView
    {
        private const int CacheSize = 4096;

        private readonly Stream stream;
        private readonly long startOffset;
        private readonly byte[] cache = new byte[CacheSize];
        private int cachedStart = -1;
        private int cachedLength;

        public int Length { get; }

        public byte this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                    throw new ArgumentOutOfRangeException(nameof(index));

                EnsureCached(index);
                return cache[index - cachedStart];
            }
        }

        public CHPRBodyView(Stream stream, long startOffset, int length)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
                throw new ArgumentException("The CHPR source stream must be readable and seekable.", nameof(stream));
            if (startOffset < 0 || length < 0 || startOffset > stream.Length - length)
                throw new ArgumentOutOfRangeException(nameof(startOffset));

            this.startOffset = startOffset;
            Length = length;
        }

        public byte[] ReadBytes(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            byte[] result = new byte[count];
            int copied = 0;
            while (copied < count)
            {
                int position = offset + copied;
                EnsureCached(position);
                int cacheOffset = position - cachedStart;
                int chunkLength = Math.Min(count - copied, cachedLength - cacheOffset);
                Buffer.BlockCopy(cache, cacheOffset, result, copied, chunkLength);
                copied += chunkLength;
            }
            return result;
        }

        private void EnsureCached(int index)
        {
            int pageStart = (index / CacheSize) * CacheSize;
            if (pageStart == cachedStart)
                return;

            stream.Seek(checked(startOffset + pageStart), SeekOrigin.Begin);
            int requested = Math.Min(CacheSize, Length - pageStart);
            int read = 0;
            while (read < requested)
            {
                int count = stream.Read(cache, read, requested - read);
                if (count <= 0)
                    throw new EndOfStreamException("Unexpected end of CHPR body during table mapping.");
                read += count;
            }

            cachedStart = pageStart;
            cachedLength = read;
        }
    }
}
