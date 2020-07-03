// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Optimized table of ordinally-compared case-sensitive strings.
    /// </summary>
    /// <remarks>
    /// Individual strings are inserted into the table and the caller receives a StringId as a handle
    /// to the string. Later, the StringId can be turned back into a string.
    /// Text is stored in buffers of character data. The upper eleven bits of a StringId value indicate the
    /// specific buffer the string is stored in, while the bottom 21 bits provide the index within the
    /// buffer where the string starts. Strings are stored with a variable-sized length field, followed by
    /// either 8-bit ASCII characters or by a 0 byte marker followed by 16-bit UTF-16 characters.
    /// This data structure only ever grows, strings are never removed. If the caller has got a StringId in hand,
    /// string lookup is thread safe. Inserting new strings however is not thread safe, the caller needs to
    /// ensure only one thread at a time attempts to do an insertion.
    /// When all insertions have been done, the table can be frozen, which discards some transient state and
    /// cuts heap consumption to a minimum. Trying to add a new string once the table has been frozen will crash.
    /// 
    /// String table is basically a two dimensional byte array, called byte buffers):
    ///
    ///         [------------------------- 2^m ------------------------------] where m = BytesPerBufferBits
    ///               1                              42
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  -
    ///         |   | f | o | o |   |   |      |   |   |   |   |     |   |   |  |
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         |   |   |   |   |   |   |      |   |   |   |   |     |   |   |  |
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         :   :   :   :   :   :   :      :   :   :   :   :     :   :   :  2^n - 1, where n = NumByteBuffers
    ///         :   :   :   :   :   :   :      :   :   :   :   :     :   :   :  |
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         |   |   |   |   |   |   |      |   | b | a | r |     |   |   |  |
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         |   |   |   |   |   |   |      |   |   |   |   |     |   |   |  |
    ///         +---+---+---+---+---+---+ .... +---+---+---+---+ ... +---+---+  -
    /// 
    /// StringId is 32 bits: 11 bits (n) for byte buffer number, and 21 bits (m) for the buffer offset.
    /// For example, 
    /// - foo (0-th byte buffer number, 1-st buffer offset), 
    /// - bar (2045-th byte buffer number, 42-th buffer offset)
    /// 
    /// To handle a large string (length > 2^12), the string table has another table called LargeStringBuffer, which is also
    /// a 2-dimensional byte array, but each buffer in that array has variable size depending on the size of the stored string.
    /// 
    ///         +---+     +---+---+---+ .... +---+---+                        -
    ///         |   | ->  |   |   |   |      |   |   |                        |
    ///         +---+     +---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         |   | ->  | z | o | o |      |   |   |   |   |     |   |   |  |
    ///         +---+     +---+---+---+ .... +---+---+---+---+ ... +---+---+  |
    ///         :   :     :   :   :   :      :   :                            2^k, where k = BytesPerBufferBits
    ///         :   :     :   :   :   :      :   :                            |
    ///         +---+     +---+---+---+ .... +---+                            |
    ///         |   | ->  |   |   |   |      |   |                            |
    ///         +---+     +---+---+---+ .... +---+---+---+---+                |
    ///         |   | ->  |   |   |   |      |   |   |   |   |                |
    ///         +---+     +---+---+---+ .... +---+---+---+---+                -
    /// 
    /// Let zoo*** be a very large string. Then the string id of zoo*** has the following composition: (2047-th byte buffer number, 1-st buffer offset).
    /// Note that the byte buffer number of zoo*** exceeds NumByteBuffers, and now its buffer offset becomes the byte buffer number of LargeStringBuffer.
    /// </remarks>
    public class StringTable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "StringTable", version: 0);

        // a StringId has the upper 11 bits as a buffer selector and the lower 21 bits as byte selector within the buffer
        private const int BytesPerBufferBits = 21;
        internal const int BytesPerBuffer = 1 << BytesPerBufferBits; // internal for use by the unit test
        private const int BytesPerBufferMask = BytesPerBuffer - 1;
        private const int NumByteBuffersBits = 32 - BytesPerBufferBits;
        private const int NumByteBuffers = (1 << NumByteBuffersBits) - 1;
        private const int NumByteBuffersMask = (1 << NumByteBuffersBits) - 1;

        // marker to indicate the length field is 4 bytes instead of 1 byte
        private const byte LongStringMarker = 255;

        // marker to indicate the character data is stored as UTF-16 instead of 8-bit ASCII.
        private const byte Utf16Marker = 0;

        private const int NullArrayMarker = int.MaxValue - 1;

        // character payload for the table, new buffers are added as needed
        private byte[][] m_byteBuffers = new byte[NumByteBuffers][];

        private readonly LargeStringBuffer m_largeStringBuffer;
        private const int LargeStringBufferNum = NumByteBuffers;

        /// <summary>
        /// Threshold (1K bytes) at which strings will be stored in <see cref="LargeStringBuffer"/>.
        /// </summary>
        private const int LargeStringBufferThreshold = 1 << 10;

        // the number of items in the table
        private int m_count;

        // Cache string expansions to avoid redundant allocations.
        private readonly ObjectCache<StringId, string> m_expansionCache = new ObjectCache<StringId, string>(HashCodeHelper.GetGreaterOrEqualPrime(4000));

        // this block of fields is used as the table is built up and then ignored once the table is frozen
        private ConcurrentBigSet<StringId> m_stringSet;
        private int m_nextId;

        /// <summary>
        /// Gets the string id for the empty string
        /// </summary>
        public readonly StringId Empty;

        /// <summary>
        /// Comparer for making case insensitive comparisons
        /// </summary>
        public readonly IEqualityComparer<StringId> CaseInsensitiveEqualityComparer;

        /// <summary>
        /// Comparer for making case insensitive comparisons
        /// </summary>
        public readonly CaseInsensitiveStringIdComparer CaseInsensitiveComparer;

        /// <summary>
        /// Comparer for making case-sensitive comparisons
        /// </summary>
        public readonly IComparer<StringId> OrdinalComparer;

        /// <summary>
        /// Captures the size in bytes of the table after invalidation
        /// </summary>
        private long m_sizeInBytes;

        /// <summary>
        /// Whether StringTable is being serialized
        /// </summary>
        private volatile bool m_isSerializationInProgress;

#if DebugStringTable
        /// <summary>
        /// The current debugId for this stringTable.
        /// </summary>
        /// <remarks>
        /// This id is used as an index in s_DebugInstances for printing string values in the debugger in debug builds.
        /// 255 is marked as not having an entry in the table.
        /// </remarks>
        private byte m_debugIndex = UnallocatedDebugId;

        /// <summary>
        /// Lock to manage concurrency of s_currentDebugIndex and s_debugInstances
        /// </summary>
        private static readonly object s_debugTableLock = new object();

        /// <summary>
        /// Comparer for making case insensitive comparisons
        /// </summary>
        private static byte s_lastDebugIndex;

        /// <summary>
        /// Debug instances of string tables.
        /// </summary>
        private static readonly WeakReference<StringTable>[] s_debugInstances = new WeakReference<StringTable>[byte.MaxValue];

        internal const byte UnallocatedDebugId = byte.MaxValue;
#endif

        /// <summary>
        /// Invalidates the StringTable to ensure it is not reused.
        /// NOTE: Properties representing statistics about the table can still be accessed
        /// </summary>
        public void Invalidate()
        {
            Contract.Requires(IsValid());
            Contract.Ensures(!IsValid());

            Freeze();
            m_sizeInBytes = SizeInBytes;
            m_byteBuffers = null;
            m_largeStringBuffer.Invalidate();
        }

        /// <summary>
        /// Checks if the table is valid
        /// </summary>
        [Pure]
        public bool IsValid()
        {
            return m_byteBuffers != null;
        }

        /// <nodoc />
        internal StringTable(int initialCapacity = 0)
        {
            Contract.Requires(initialCapacity >= 0);
#if DebugStringTable
            DebugRegisterStringTable(this);
#endif

            CaseInsensitiveEqualityComparer = new CaseInsensitiveStringIdEqualityComparer(this);
            CaseInsensitiveComparer = new CaseInsensitiveStringIdComparer(this);
            OrdinalComparer = new OrdinalStringIdComparer(this);
            m_stringSet = new ConcurrentBigSet<StringId>(capacity: initialCapacity);

            // set up the initial buffer and consume the first byte so that StringId.Invalid's slot is consumed
            m_byteBuffers[0] = new byte[BytesPerBuffer];
            m_largeStringBuffer = new LargeStringBuffer();
            m_nextId = 1;
            Empty = AddString(string.Empty);
        }

        /// <summary>
        /// Constructor used for deserialized tables
        /// </summary>
        protected StringTable(SerializedState state)
        {
            Contract.RequiresNotNull(state);

#if DebugStringTable
            DebugRegisterStringTable(this);
#endif

            CaseInsensitiveEqualityComparer = new CaseInsensitiveStringIdEqualityComparer(this);
            CaseInsensitiveComparer = new CaseInsensitiveStringIdComparer(this);
            OrdinalComparer = new OrdinalStringIdComparer(this);

            m_largeStringBuffer = state.LargeStringBuffer;
            m_byteBuffers = state.ByteBuffers;
            m_count = state.Count;
            m_nextId = state.NextId;
            m_stringSet = state.StringSet;
            Empty = AddString(string.Empty);
        }

        /// <summary>
        /// Determines whether a string matches an entry in the table in an ordinally case-insensitive way.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        internal bool CaseInsensitiveEquals(StringId id1, StringId id2)
        {
            Contract.Requires(id1.IsValid);
            Contract.Requires(id2.IsValid);
            Contract.Requires(IsValid());

            if (id1 == id2)
            {
                return true;
            }

            GetBytesCore(id1, out var buffer1, out var index1, out var length1);
            GetBytesCore(id2, out var buffer2, out var index2, out var length2);

            if (length1 != length2)
            {
                // different lengths
                return false;
            }

            if (buffer1[index1] != Utf16Marker)
            {
                Contract.Assume((index1 + length1) <= buffer1.Length);
                Contract.Assume((index2 + length2) <= buffer2.Length);

                // characters are 8 bits
                for (int i = 0; i < length1; i++)
                {
                    var ch1 = (char)buffer1[index1 + i];
                    var ch2 = (char)buffer2[index2 + i];
                    if (ch1.ToUpperInvariantFast() != ch2.ToUpperInvariantFast())
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (buffer2[index1] != Utf16Marker)
                {
                    // different marker bytes
                    return false;
                }

                Contract.Assume((index1 + (length1 * 2) + 1) <= buffer1.Length);
                Contract.Assume((index2 + (length2 * 2) + 1) <= buffer2.Length);

                // characters are 16 bits
                index1++; // skip the marker
                index2++; // skip the marker
                for (int i = 0; i < length1; i++)
                {
                    var ch1 = (char)((buffer1[index1 + (2 * i)] << 8) | buffer1[index1 + (2 * i) + 1]);
                    var ch2 = (char)((buffer2[index2 + (2 * i)] << 8) | buffer2[index2 + (2 * i) + 1]);
                    if (ch1.ToUpperInvariantFast() != ch2.ToUpperInvariantFast())
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determines which key comes before which key
        /// </summary>
        internal int CompareOrdinal(StringId id1, StringId id2)
        {
            Contract.Requires(id1.IsValid);
            Contract.Requires(id2.IsValid);
            Contract.Requires(IsValid());

            if (id1 == id2)
            {
                return 0;
            }

            // For now we use a very naive slow implementation.
            // We can do better here just like we do for ordinal compare.
            return string.CompareOrdinal(GetString(id1), GetString(id2));
        }

        /// <summary>
        /// Determines which key comes before which key, ignoring case.
        /// </summary>
        internal int CompareCaseInsensitive(StringId id1, StringId id2)
        {
            Contract.Requires(id1.IsValid);
            Contract.Requires(id2.IsValid);
            Contract.Requires(IsValid());

            if (id1 == id2)
            {
                return 0;
            }

            // For now we use a very naive slow implementation.
            // We can do better here just like we do for ordinal compare.
            return string.Compare(GetString(id1), GetString(id2), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether a string matches an entry in the table.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        internal bool Equals(string str, StringId id)
        {
            Contract.RequiresNotNull(str);
            Contract.Requires(id.IsValid);
            Contract.Requires(IsValid());

            return Equals(new StringSegment(str, 0, str.Length), id);
        }

        /// <summary>
        /// Determines whether a char array matches an entry in the table.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        private bool Equals<T>(T seg, StringId id) where T : struct, ICharSpan<T>
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(IsValid());

            GetBytesCore(id, out var buffer, out var index, out var length);

            if (length != seg.Length)
            {
                return false;
            }

            if (buffer[index] != Utf16Marker)
            {
                Contract.Assume(index + length <= buffer.Length);
                return seg.Equals8Bit(buffer, index);
            }
            else
            {
                // skip the marker
                index++;
                Contract.Assume(index + (length * 2) <= buffer.Length);
                return seg.Equals16Bit(buffer, index);
            }
        }

        internal static int GetHashCode(StringId id)
        {
            return id.Value;
        }

        internal int CaseInsensitiveGetHashCode(StringId id)
        {
            Contract.Requires(id.IsValid);

            GetBytesCore(id, out var buffer, out var index, out var length);

            uint hash = 5381;

            if (buffer[index] != Utf16Marker)
            {
                Contract.Assume((length + index) <= buffer.Length);

                // characters are 8 bits
                while (length-- > 0)
                {
                    var ch = (char)buffer[index++];
                    ch = ch.ToUpperInvariantFast();
                    unchecked
                    {
                        hash = ((hash << 5) + hash) ^ ch;
                    }
                }
            }
            else
            {
                Contract.Assume(((length * 2) + index + 1) <= buffer.Length);

                // characters are 16 bits
                index++; // skip the marker
                while (length-- > 0)
                {
                    var ch = (char)((buffer[index++] << 8) | buffer[index++]);
                    ch = ch.ToUpperInvariantFast();
                    unchecked
                    {
                        hash = ((hash << 5) + hash) ^ ch;
                    }
                }
            }

            return unchecked((int)hash);
        }

        /// <summary>
        /// Returns the length in characters of an entry in the table.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe.
        /// </remarks>
        internal int GetLength(StringId id)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(IsValid());
            Contract.Ensures(Contract.Result<int>() >= 0);

            GetBytesCore(id, out _, out _, length: out int length);
            Contract.Assume(length >= 0);
            return length;
        }

        /// <summary>
        /// Adds the specified string to the string table.
        /// </summary>
        /// <param name="value">A non-null string.</param>
        /// <returns>The ID of the string within the table.</returns>
        public StringId AddString(string value)
        {
            Contract.RequiresNotNull(value);
            Contract.Requires(IsValid());
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            return AddString((StringSegment)value);
        }

        internal StringId AddString<T>(T value) where T : struct, ICharSpan<T>
        {
            Contract.Requires(IsValid());
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            var getOrAddResult = m_stringSet.GetOrAddItem(new SoughtSetString<T>(this, value));
            Contract.Assert(getOrAddResult.IsFound || !m_isSerializationInProgress, "StringTable is being serialized. No new entry can be added.");

            return getOrAddResult.Item;
        }

        [ContractVerification(false)]
        private StringId AddStringToBuffer<T>(T seg) where T : struct, ICharSpan<T>
        {
            Contract.Requires(IsValid());
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            // Not in the set, so get some space to hold the string
            //
            // Note that this call is invoked in a racy setting. It's possible for multiple calls to execute concurrently and
            // try to insert the same string N times. We allow this and end up just wasting space in the table when this happens.
            // In this context, m_count thus accounts for the total number of strings inserted into the table, and not the potential
            // smaller of logical strings known to the table.
            bool longString = seg.Length >= 255;

            int space = 0;

            // see if the string is pure ASCII
            bool isAscii = seg.OnlyContains8BitChars;

            if (isAscii)
            {
                // stored as bytes
                space += seg.Length;
            }
            else
            {
                // stored as UTF-16
                space += (seg.Length * 2) + 1; // *2 for UTF-16, +1 for the 'switch to unicode' marker byte
            }

            // count how many strings are in the buffer
            Interlocked.Increment(ref m_count);

            if (space > LargeStringBufferThreshold && m_largeStringBuffer.TryReserveSlot(out var largeStringIndex))
            {
                var buffer = new byte[space];
                WriteBytes(seg, isAscii, buffer, 0);
                m_largeStringBuffer[largeStringIndex] = buffer;
                return ComputeStringId(LargeStringBufferNum, offset: largeStringIndex);
            }

            // if the length >= 255 then we store it as a marker byte followed by a 4-byte length value
            space += longString ? 5 : 1;

            int byteIndex;
            int bufferNum;

            // loop until we find a suitable location to copy the string to
            while (true)
            {
                // get the next possible location for the string
                int current = Volatile.Read(ref m_nextId);
                bufferNum = (current >> BytesPerBufferBits) & NumByteBuffersMask;
                byteIndex = current & BytesPerBufferMask;

                // is the available space big enough?
                if (space < BytesPerBuffer - byteIndex)
                {
                    // there's room in the buffer so try to claim it
                    int next = (bufferNum << BytesPerBufferBits) | (byteIndex + space);
                    if (Interlocked.CompareExchange(ref m_nextId, next, current) == current)
                    {
                        // got some room, now go fill it in
                        break;
                    }

                    // go try again...
                    continue;
                }

                // the string doesn't fit in the current buffer, we need a new buffer
                int newBufferSize = BytesPerBuffer;
                if (space > BytesPerBuffer)
                {
                    newBufferSize = space;
                }

                bufferNum++;

                // Make sure we don't overflow the buffer we're indexing into
                if (bufferNum >= NumByteBuffers)
                {
                    Contract.Assert(false, $"Exceeded the number of ByteBuffers allowed in this StringTable: {bufferNum} >= {NumByteBuffers}");
                }

                lock (m_byteBuffers)
                {
                    if (m_byteBuffers[bufferNum] != null)
                    {
                        // somebody racily beat us and allocated this buffer, so just retry the whole thing from scratch
                        continue;
                    }

                    // allocate a new buffer
                    m_byteBuffers[bufferNum] = new byte[newBufferSize];

                    if (space >= BytesPerBuffer)
                    {
                        // force writing into a fresh buffer
                        Volatile.Write(ref m_nextId, (bufferNum << BytesPerBufferBits) | BytesPerBufferMask);
                    }
                    else
                    {
                        // force writing into this new buffer
                        Volatile.Write(ref m_nextId, (bufferNum << BytesPerBufferBits) | space);
                    }

                    // go write the string at the base of the new buffer
                    byteIndex = 0;
                    break;
                }
            }

            var stringId = ComputeStringId(bufferNum, offset: byteIndex);

            // now copy the string data into the buffer
            byte[] currentBuffer = m_byteBuffers[bufferNum];
            if (longString)
            {
                currentBuffer[byteIndex++] = LongStringMarker;
                Bits.WriteInt32(currentBuffer, ref byteIndex, seg.Length);
            }
            else
            {
                currentBuffer[byteIndex++] = (byte)seg.Length;
            }

            WriteBytes(seg, isAscii, currentBuffer, byteIndex);
            return stringId;
        }

        private StringId ComputeStringId(int bufferNum, int offset)
        {
#if DebugStringTable
            var stringId = new StringId((bufferNum << BytesPerBufferBits) + offset, m_debugIndex);
#else
            var stringId = new StringId((bufferNum << BytesPerBufferBits) + offset);
#endif

            Contract.Assert(stringId.IsValid);
            return stringId;
        }

        private static void WriteBytes<T>(T seg, bool isAscii, byte[] buffer, int byteIndex) where T : struct, ICharSpan<T>
        {
            if (isAscii)
            {
                seg.CopyAs8Bit(buffer, byteIndex);
            }
            else
            {
                buffer[byteIndex++] = Utf16Marker;
                seg.CopyAs16Bit(buffer, byteIndex);
            }
        }

        /// <summary>
        /// Copies a string from this table into a char[].
        /// </summary>
        [ContractVerification(false)]
        internal int CopyString(StringId id, char[] destination, int destinationIndex, bool isEndIndex = false)
        {
            return CopyString(id, ref destination, destinationIndex, isEndIndex: isEndIndex, allowResizeBuffer: false);
        }

        /// <summary>
        /// Copies a string from this table into a char[].
        /// </summary>
        [ContractVerification(false)]
        internal int CopyString(StringId id, ref char[] destination, int destinationIndex, bool isEndIndex = false, bool allowResizeBuffer = true)
        {
            Contract.Requires(IsValid());
            Contract.Requires(id.IsValid);
            Contract.RequiresNotNull(destination);
            Contract.Requires(destinationIndex >= 0);
            Contract.Requires(
                (!isEndIndex && (destinationIndex < destination.Length)) || (isEndIndex && (destinationIndex <= destination.Length)) ||
                (destination.Length == 0 && destinationIndex == 0));

            int index, length;
            byte[] buffer;
            GetBytesCore(id, out buffer, out index, out length);

            if (allowResizeBuffer && length > buffer.Length)
            {
                Array.Resize(ref destination, length);
            }

            int resultLength = length;

            if (isEndIndex)
            {
                destinationIndex -= length;
            }

            if (buffer[index] != Utf16Marker)
            {
                Contract.Assume((length + index) <= buffer.Length);
                Contract.Assume((length + destinationIndex) <= destination.Length);

                // characters are 8 bits
                while (length-- > 0)
                {
                    destination[destinationIndex++] = (char)buffer[index++];
                }
            }
            else
            {
                Contract.Assume(((length * 2) + index + 1) <= buffer.Length);
                Contract.Assume((length + destinationIndex) <= destination.Length);

                // characters are 16 bits
                index++; // skip the marker
                while (length-- > 0)
                {
                    destination[destinationIndex++] = (char)((buffer[index++] << 8) | buffer[index++]);
                }
            }

            return resultLength;
        }

        /// <summary>
        /// Copies a string from this table into a StringBuilder.
        /// </summary>
        public void CopyString(StringId id, StringBuilder destination)
        {
            Contract.Requires(IsValid());
            Contract.Requires(id.IsValid);
            Contract.RequiresNotNull(destination);

            GetBytesCore(id, out byte[] buffer, out int index, out int length);

            if (buffer[index] != Utf16Marker)
            {
                Contract.Assume((length + index) <= buffer.Length);

                // characters are 8 bits
                while (length-- > 0)
                {
                    destination.Append((char)buffer[index++]);
                }
            }
            else
            {
                Contract.Assume(((length * 2) + index + 1) <= buffer.Length);

                // characters are 16 bits
                index++; // skip the marker
                while (length-- > 0)
                {
                    destination.Append((char)((buffer[index++] << 8) | buffer[index++]));
                }
            }
        }

        private void GetBytesCore(StringId id, out byte[] buffer, out int index, out int length)
        {
            index = id.Value & BytesPerBufferMask;
            var bufferNum = (id.Value >> BytesPerBufferBits) & NumByteBuffersMask;
            if (bufferNum == LargeStringBufferNum)
            {
                buffer = m_largeStringBuffer[index];
                index = 0;
                length = buffer.Length;
                if (buffer[0] == Utf16Marker)
                {
                    length = (length - 1) / 2;
                }
            }
            else
            {
                buffer = m_byteBuffers[bufferNum];
                length = buffer[index++];
                if (length == LongStringMarker)
                {
                    length = Bits.ReadInt32(buffer, ref index);
                }
            }
        }

        /// <summary>
        /// Gets a string from this table.
        /// </summary>
        public string GetString(StringId id)
        {
            Contract.Requires(id.IsValid);
            Contract.Requires(IsValid());

            if (!m_expansionCache.TryGetValue(id, out string result))
            {
                int len = GetLength(id);
                using (var wrapper = Pools.GetCharArray(len))
                {
                    char[] buffer = wrapper.Instance;
                    CopyString(id, buffer, 0);
                    result = new string(buffer, 0, len);
                    m_expansionCache.AddItem(id, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets a binary string from this table.
        /// </summary>
        public BinaryStringSegment GetBinaryString(StringId id)
        {
            GetBytesCore(id, out byte[] buffer, out int index, out int length);

            bool isAscii = true;
            if (buffer[index] == Utf16Marker)
            {
                isAscii = false;
                index++; // skip the marker
                length *= 2; // utf16 uses 2 bytes per character
            }

            return new BinaryStringSegment(buffer, index, length, isAscii);
        }

        /// <summary>
        /// Gets bytes for a string from this table.
        /// </summary>
        public ArrayView<byte> GetBytes(StringId id)
        {
            GetBytesCore(id, out byte[] buffer, out int index, out int length);

            if (buffer[index] == Utf16Marker)
            {
                index++; // skip the marker
                length *= 2; // utf16 uses 2 bytes per character
            }

            return ArrayView.Create(buffer, index, length);
        }

        /// <summary>
        /// Changes the extension of a string.
        /// </summary>
        /// <param name="id">The string to affect.</param>
        /// <param name="extension">The new extension with a leading period.</param>
        /// <returns>The id of a string with the new extension.</returns>
        public StringId ChangeExtension(StringId id, StringId extension)
        {
            Contract.Requires(IsValid());
            Contract.Requires(id.IsValid);
            Contract.Requires(extension.IsValid);
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            int originalLength = GetLength(id);
            int extLength = GetLength(extension);

            using (var wrapper = Pools.GetCharArray(originalLength + extLength))
            {
                char[] name = wrapper.Instance;
                CopyString(id, name, 0);

                int newLength = originalLength;
                for (int i = originalLength; --i >= 0;)
                {
                    char ch = name[i];
                    if (ch == '.')
                    {
                        newLength = i;
                        break;
                    }
                }

                CopyString(extension, name, newLength);

                var s = new CharArraySegment(name, 0, newLength + extLength);
                return AddString(s);
            }
        }

        /// <summary>
        /// Gets the extension of a string.
        /// </summary>
        /// <param name="id">The string of which to extract the extension.</param>
        /// <returns>The extension.</returns>
        public StringId GetExtension(StringId id)
        {
            Contract.Requires(id.IsValid);

            int length = GetLength(id);
            using (var wrapper = Pools.GetCharArray(length))
            {
                char[] name = wrapper.Instance;
                CopyString(id, name, 0);

                for (int i = length; --i >= 0;)
                {
                    char ch = name[i];
                    if (ch == '.')
                    {
                        var s = new CharArraySegment(name, i, length - i);
                        return AddString(s);
                    }
                }

                return StringId.Invalid;
            }
        }

        /// <summary>
        /// Remove the extension of a path.
        /// </summary>
        /// <param name="id">The string affect.</param>
        /// <returns>The id of a path without its last extension.</returns>
        public StringId RemoveExtension(StringId id)
        {
            Contract.Requires(IsValid());
            Contract.Requires(id.IsValid);
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            int originalLength = GetLength(id);
            using (var wrapper = Pools.GetCharArray(originalLength))
            {
                char[] name = wrapper.Instance;
                CopyString(id, name, 0);

                int newLength = originalLength;
                for (int i = originalLength; --i >= 0;)
                {
                    char ch = name[i];
                    if (ch == '.')
                    {
                        newLength = i;
                        break;
                    }
                }

                if (newLength == 0)
                {
                    // if we'd end up with an empty string just return the original
                    return id;
                }

                if (newLength == originalLength)
                {
                    // no . found, return original string
                    return id;
                }

                var s = new CharArraySegment(name, 0, newLength);
                return AddString(s);
            }
        }

        /// <summary>
        /// Concatenates two strings together.
        /// </summary>
        public StringId Concat(StringId id1, StringId id2)
        {
            Contract.Requires(id1.IsValid);
            Contract.Requires(id2.IsValid);
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            int len1 = GetLength(id1);
            int len2 = GetLength(id2);

            using (var wrapper = Pools.GetCharArray(len1 + len2))
            {
                char[] buffer = wrapper.Instance;
                CopyString(id1, buffer, 0);
                CopyString(id2, buffer, len1);

                var s = new CharArraySegment(buffer, 0, len1 + len2);
                return AddString(s);
            }
        }

        /// <summary>
        /// Releases temporary resources and prevents the table from mutating from this point forward.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread-safe.
        /// </remarks>
        internal void Freeze()
        {
            Contract.Requires(IsValid());

            // release this set to free memory
            m_stringSet = null;
        }

        /// <summary>
        /// Gets how much memory this abstraction is consuming.
        /// </summary>
        /// <remarks>
        /// This assumes the table has been frozen as the data that gets freed when freezing is not counted here.
        /// </remarks>
        public long SizeInBytes
        {
            get
            {
                if (IsValid())
                {
                    long size = m_byteBuffers.Length * 8L; // pointers to the individual buffers
                    size += 12; // array overhead
                    foreach (var t in m_byteBuffers)
                    {
                        if (t != null)
                        {
                            size += 12; // array overhead
                            size += t.Length; // actual char data
                        }
                    }

                    size += m_largeStringBuffer.Size;
                    return size;
                }

                return m_sizeInBytes;
            }
        }

        /// <summary>
        /// Gets approximately how much memory (in bytes) is used by large strings
        /// </summary>
        public long LargeStringSize => m_largeStringBuffer.Size;

        /// <summary>
        /// Gets the number of large strings
        /// </summary>
        public int LargeStringCount => m_largeStringBuffer.Count;

        /// <summary>
        /// Gets how many strings are in this table.
        /// </summary>
        public int Count => m_count;

        /// <summary>
        /// Gets the number of cache misses on the name expansion cache.
        /// </summary>
        public long CacheHits => m_expansionCache.Hits;

        /// <summary>
        /// Gets the number of cache hits on the name expansion cache.
        /// </summary>
        public long CacheMisses => m_expansionCache.Misses;

        private readonly struct SoughtSetString<T> : IPendingSetItem<StringId>
            where T : struct, ICharSpan<T>
        {
            private readonly StringTable m_stringTable;
            private readonly T m_sought;

            public SoughtSetString(StringTable stringTable, T sought)
            {
                m_stringTable = stringTable;
                m_sought = sought;
                HashCode = sought.GetHashCode();
            }

            public int HashCode { get; }

            public bool Equals(StringId other)
            {
                return m_stringTable.Equals(m_sought, other);
            }

            public StringId CreateOrUpdateItem(StringId oldItem, bool hasOldItem, out bool remove)
            {
                remove = false;
                return m_stringTable.AddStringToBuffer(m_sought);
            }
        }

        #region Helpers

        /// <summary>
        /// Defines a buffer for large string over <see cref="LargeStringBufferThreshold"/> in size. Strings are stored as independent byte buffers
        /// rather than sharing byte buffers
        /// </summary>
        protected class LargeStringBuffer
        {
            private byte[][] m_byteArrays;
            private int m_indexCursor;
            private long m_size;

            /// <summary>
            /// Total size of strings (in bytes)
            /// </summary>
            internal long Size => m_size;

            /// <summary>
            /// Number of strings stored
            /// </summary>
            internal int Count => Math.Min(m_indexCursor + 1, m_byteArrays.Length);

            internal LargeStringBuffer()
            {
                m_indexCursor = -1;
                m_byteArrays = new byte[BytesPerBuffer][];
            }

            private LargeStringBuffer(BuildXLReader reader)
            {
                m_indexCursor = reader.ReadInt32();
                m_size = reader.ReadInt64();
                m_byteArrays = reader.ReadArray(r =>
                {
                    var length = r.ReadInt32Compact();
                    return r.ReadBytes(length);
                },
                minimumLength: BytesPerBuffer);
            }

            internal static LargeStringBuffer Deserialize(BuildXLReader reader)
            {
                return new LargeStringBuffer(reader);
            }

            internal void Serialize(BuildXLWriter writer)
            {
                writer.Write(m_indexCursor);
                writer.Write(m_size);

                var serializedByteArrays = new ArrayView<byte[]>(m_byteArrays, 0, Count);

                writer.WriteReadOnlyList(serializedByteArrays, (w, b) =>
                {
                    w.WriteCompact(b.Length);
                    w.Write(b);
                });
            }

            internal bool TryReserveSlot(out int index)
            {
                index = Interlocked.Increment(ref m_indexCursor);
                return index < m_byteArrays.Length;
            }

            internal void Invalidate()
            {
                m_byteArrays = null;
            }

            internal byte[] this[int index]
            {
                get
                {
                    return m_byteArrays[index];
                }
                set
                {
                    var priorValue = Interlocked.CompareExchange(ref m_byteArrays[index], value, null);
                    Contract.Assert(priorValue == null);
                    Interlocked.Add(ref m_size, value.Length);
                }
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Deserializes a string table
        /// </summary>
        public static Task<StringTable> DeserializeAsync(BuildXLReader reader)
        {
            Contract.RequiresNotNull(reader);

            var state = ReadSerializationState(reader);
            return Task.FromResult(new StringTable(state));
        }

        /// <summary>
        /// State that gets serialized
        /// </summary>
        protected sealed class SerializedState
        {
            /// <summary>
            /// Backing data
            /// </summary>
            public byte[][] ByteBuffers;

            /// <summary>
            /// The backing data for large strings
            /// </summary>
            public LargeStringBuffer LargeStringBuffer;

            /// <summary>
            /// Count of items the table contains
            /// </summary>
            public int Count;

            /// <summary>
            /// Next Id to be used
            /// </summary>
            public int NextId;

            /// <summary>
            /// All StringIds created by the table
            /// </summary>
            public ConcurrentBigSet<StringId> StringSet;
        }

        /// <summary>
        /// Reads serialization state
        /// </summary>
        protected static SerializedState ReadSerializationState(BuildXLReader reader)
        {
            Contract.RequiresNotNull(reader);

            SerializedState result = new SerializedState();
            result.NextId = reader.ReadInt32();
            result.ByteBuffers = new byte[NumByteBuffers][];

            int indexOfPartiallyFilledBuffer, lengthInPartiallyFilledBuffer;
            GetBufferIndex(result.NextId, out indexOfPartiallyFilledBuffer, out lengthInPartiallyFilledBuffer);

            for (int i = 0; i < NumByteBuffers; i++)
            {
                int arrayLength = reader.ReadInt32();

                if (arrayLength == NullArrayMarker)
                {
                    continue;
                }

                var buffer = new byte[arrayLength];
                reader.Read(buffer, 0, i == indexOfPartiallyFilledBuffer ? lengthInPartiallyFilledBuffer : arrayLength);
                result.ByteBuffers[i] = buffer;
            }

            result.LargeStringBuffer = LargeStringBuffer.Deserialize(reader);

            result.StringSet = ConcurrentBigSet<StringId>.Deserialize(reader, () => reader.ReadStringId());
            result.Count = reader.ReadInt32();

            return result;
        }

        /// <summary>
        /// Serializes the StringTable
        /// </summary>
        /// <remarks>
        /// Not thread safe. The caller should ensure there are no concurrent accesses to the structure while serializing.
        /// </remarks>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.RequiresNotNull(writer);

            m_isSerializationInProgress = true;
            // Call a static method to do the actual serialization to ensure not state outside of SerializedState is used
            try
            {
                Serialize(
                    writer,
                    new SerializedState()
                    {
                        Count = Count,
                        NextId = m_nextId,
                        StringSet = m_stringSet,
                        ByteBuffers = m_byteBuffers,
                        LargeStringBuffer = m_largeStringBuffer
                    });
            }
            finally
            {
                m_isSerializationInProgress = false;
            }         
        }

        private static void Serialize(BuildXLWriter writer, SerializedState state)
        {
            Contract.RequiresNotNull(writer);

            writer.Write(state.NextId);

            GetBufferIndex(state.NextId, out int indexOfPartiallyFilledBuffer, out int lengthInPartiallyFilledBuffer);
            
            for (int i = 0; i < NumByteBuffers; i++)
            {
                if (state.ByteBuffers[i] != null)
                {
                    writer.Write(state.ByteBuffers[i].Length);
                    writer.Write(state.ByteBuffers[i], 0, i == indexOfPartiallyFilledBuffer ? lengthInPartiallyFilledBuffer : state.ByteBuffers[i].Length);
                }
                else
                {
                    writer.Write(NullArrayMarker);
                }
            }

            state.LargeStringBuffer.Serialize(writer);

            state.StringSet.Serialize(writer, id => writer.Write(id));

            writer.Write(state.Count);
        }

        internal static void GetBufferIndex(int nextId, out int indexOfPartiallyFilledBuffer, out int lengthInPartiallyFilledBuffer)
        {
            unchecked
            {
                // Need to cast nextId to a unit to prevent the right shift from bringing in ones if the leftmost bit is a 1
                indexOfPartiallyFilledBuffer = (int)((uint)nextId >> BytesPerBufferBits);
                lengthInPartiallyFilledBuffer = (int)((uint)nextId & BytesPerBufferMask);
            }
        }
#endregion

#if DebugStringTable
        /// <summary>
        /// Registers the string table
        /// </summary>
        private static void DebugRegisterStringTable(StringTable table)
        {
            Contract.Assume(table.m_debugIndex == UnallocatedDebugId, "StringTable has been registered multiple times.");
            lock (s_debugTableLock)
            {
                var debugIndex = s_lastDebugIndex;
                s_lastDebugIndex++;
                if (s_lastDebugIndex == byte.MaxValue)
                {
                    s_lastDebugIndex = 0;
                }

                table.m_debugIndex = debugIndex;

                WeakReference<StringTable> existingReference = s_debugInstances[debugIndex];
                if (existingReference != null && existingReference.TryGetTarget(out StringTable existingTable))
                {
                    // We have cycled and are taking over an existing spot, mark it as not having an entry in the table anymore.
                    existingTable.m_debugIndex = UnallocatedDebugId;
                }

                s_debugInstances[debugIndex] = new WeakReference<StringTable>(table);
            }
        }

        internal static StringTable DebugTryGetTableByDebugId(byte debugId)
        {
            if (debugId == UnallocatedDebugId)
            {
                return null;
            }

            WeakReference<StringTable> existingReference = Volatile.Read(ref s_debugInstances[debugId]);
            if (existingReference != null && existingReference.TryGetTarget(out StringTable existingTable))
            {
                return existingTable;
            }

            return null;
        }
#endif
    }
}
