/*
 * Copyright 2002-2015 Drew Noakes
 *
 *    Modified by Yakov Danilov <yakodani@gmail.com> for Imazen LLC (Ported from Java to C#)
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * More information about this project is available at:
 *
 *    https://drewnoakes.com/code/exif/
 *    https://github.com/drewnoakes/metadata-extractor
 */

using System;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using Sharpen;

namespace Com.Drew.Lang
{
    /// <author>Drew Noakes https://drewnoakes.com</author>
    public sealed class IndexedCapturingReader : IndexedReader
    {
        private const int DefaultChunkLength = 2 * 1024;

        [NotNull]
        private readonly Stream _stream;

        private readonly int _chunkLength;

        private readonly AList<byte[]> _chunks = new AList<byte[]>();

        private bool _isStreamFinished;

        private int _streamLength;

        public IndexedCapturingReader([NotNull] Stream stream, int chunkLength = DefaultChunkLength)
        {
            if (stream == null)
            {
                throw new ArgumentNullException();
            }
            if (chunkLength <= 0)
            {
                throw new ArgumentException("chunkLength must be greater than zero");
            }
            _chunkLength = chunkLength;
            _stream = stream;
        }

        /// <summary>Reads to the end of the stream, in order to determine the total number of bytes.</summary>
        /// <remarks>
        /// Reads to the end of the stream, in order to determine the total number of bytes.
        /// In general, this is not a good idea for this implementation of
        /// <see cref="IndexedReader"/>.
        /// </remarks>
        /// <returns>the length of the data source, in bytes.</returns>
        /// <exception cref="System.IO.IOException"/>
        public override long GetLength()
        {
            IsValidIndex(int.MaxValue, 1);
            Debug.Assert((_isStreamFinished));
            return _streamLength;
        }

        /// <summary>Ensures that the buffered bytes extend to cover the specified index.</summary>
        /// <remarks>
        /// Ensures that the buffered bytes extend to cover the specified index. If not, an attempt is made
        /// to read to that point.
        /// <p/>
        /// If the stream ends before the point is reached, a
        /// <see cref="BufferBoundsException"/>
        /// is raised.
        /// </remarks>
        /// <param name="index">the index from which the required bytes start</param>
        /// <param name="bytesRequested">the number of bytes which are required</param>
        /// <exception cref="BufferBoundsException">if the stream ends before the required number of bytes are acquired</exception>
        /// <exception cref="System.IO.IOException"/>
        protected override void ValidateIndex(int index, int bytesRequested)
        {
            if (index < 0)
            {
                throw new BufferBoundsException(string.Format("Attempt to read from buffer using a negative index ({0})", index));
            }
            if (bytesRequested < 0)
            {
                throw new BufferBoundsException("Number of requested bytes must be zero or greater");
            }
            if ((long)index + bytesRequested - 1 > int.MaxValue)
            {
                throw new BufferBoundsException(string.Format("Number of requested bytes summed with starting index exceed maximum range of signed 32 bit integers (requested index: {0}, requested count: {1})", index, bytesRequested));
            }
            if (!IsValidIndex(index, bytesRequested))
            {
                Debug.Assert((_isStreamFinished));
                // TODO test that can continue using an instance of this type after this exception
                throw new BufferBoundsException(index, bytesRequested, _streamLength);
            }
        }

        /// <exception cref="System.IO.IOException"/>
        protected override bool IsValidIndex(int index, int bytesRequested)
        {
            if (index < 0 || bytesRequested < 0)
            {
                return false;
            }
            long endIndexLong = (long)index + bytesRequested - 1;
            if (endIndexLong > int.MaxValue)
            {
                return false;
            }
            int endIndex = (int)endIndexLong;
            if (_isStreamFinished)
            {
                return endIndex < _streamLength;
            }
            int chunkIndex = endIndex / _chunkLength;
            // TODO test loading several chunks for a single request
            while (chunkIndex >= _chunks.Count)
            {
                Debug.Assert((!_isStreamFinished));
                byte[] chunk = new byte[_chunkLength];
                int totalBytesRead = 0;
                while (!_isStreamFinished && totalBytesRead != _chunkLength)
                {
                    int bytesRead = _stream.Read(chunk, totalBytesRead, _chunkLength - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        // the stream has ended, which may be ok
                        _isStreamFinished = true;
                        _streamLength = _chunks.Count * _chunkLength + totalBytesRead;
                        // check we have enough bytes for the requested index
                        if (endIndex >= _streamLength)
                        {
                            _chunks.Add(chunk);
                            return false;
                        }
                    }
                    else
                    {
                        totalBytesRead += bytesRead;
                    }
                }
                _chunks.Add(chunk);
            }
            return true;
        }

        /// <exception cref="System.IO.IOException"/>
        protected override byte GetByte(int index)
        {
            Debug.Assert((index >= 0));
            int chunkIndex = index / _chunkLength;
            int innerIndex = index % _chunkLength;
            byte[] chunk = _chunks[chunkIndex];
            return chunk[innerIndex];
        }

        /// <exception cref="System.IO.IOException"/>
        public override byte[] GetBytes(int index, int count)
        {
            ValidateIndex(index, count);
            byte[] bytes = new byte[count];
            int remaining = count;
            int fromIndex = index;
            int toIndex = 0;
            while (remaining != 0)
            {
                int fromChunkIndex = fromIndex / _chunkLength;
                int fromInnerIndex = fromIndex % _chunkLength;
                int length = Math.Min(remaining, _chunkLength - fromInnerIndex);
                byte[] chunk = _chunks[fromChunkIndex];
                Array.Copy(chunk, fromInnerIndex, bytes, toIndex, length);
                remaining -= length;
                fromIndex += length;
                toIndex += length;
            }
            return bytes;
        }
    }
}