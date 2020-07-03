﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Diagnostics.ContractsLight;
using System;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Stream that is guaranteed to have a length.
    /// </summary>
    public struct StreamWithLength : IDisposable
    {
        /// <summary>
        /// MemoryStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength(MemoryStream s) => s.HasLength();

        /// <summary>
        /// FileStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength(FileStream s) => s.HasLength();

#nullable enable annotations
        /// <summary>
        /// MemoryStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength?(MemoryStream? s) => s?.HasLength();

        /// <summary>
        /// FileStream always has a length so it can be automatically wrapped.
        /// </summary>
        public static implicit operator StreamWithLength?(FileStream? s) => s?.HasLength();
#nullable restore annotations

        /// <summary>
        /// Implicitly expose stream for all operations on it.
        /// </summary>
        public static implicit operator Stream(StreamWithLength s) => s.Stream;

        /// <summary>
        /// Underlying stream.
        /// </summary>
        public Stream Stream { get; }

        /// <summary>
        /// Length of underlying stream.
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Called by extension methods.
        /// </summary>
        internal StreamWithLength(Stream stream, long length)
        {
            Contract.AssertNotNull(stream);
            Contract.Assert(length >= 0);
            Contract.Assert(!stream.CanSeek || stream.Length == length);
            Stream = stream;
            Length = length;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stream.Dispose();
        }
    }

    /// <summary>
    /// Helpers for creating a StreamWithLength
    /// </summary>
    public static class StreamWithLengthExtensions
    {
        /// <summary>
        /// Verify at runtime that stream has a Length.
        /// </summary>
        public static StreamWithLength AssertHasLength(this Stream s)
        {
            Contract.Assert(s.CanSeek);
            return new StreamWithLength(s, s.Length);
        }

        /// <summary>
        /// With an explicit length.
        /// </summary>
        public static StreamWithLength WithLength(this Stream s, long length)
        {
            Contract.AssertNotNull(s);
            return new StreamWithLength(s, length);
        }

        /// <summary>
        /// Helper for safely wrapping MemoryStream.
        /// </summary>
        public static StreamWithLength HasLength(this MemoryStream s)
        {
            return new StreamWithLength(s, s.Length);
        }

        /// <summary>
        /// Helper for safely wrapping FileStream.
        /// </summary>
        public static StreamWithLength HasLength(this FileStream s)
        {
            return new StreamWithLength(s, s.Length);
        }
    }
}
