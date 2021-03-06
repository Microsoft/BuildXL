﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Provides helper methods for constructing <see cref="ByteString"/> instances.
    /// </summary>
    public static class ByteStringExtensions
    {
        /// <summary>
        /// Creates <see cref="ByteString"/> without copying a given <paramref name="buffer"/>.
        /// </summary>
        /// <remarks>
        /// This method is unsafe and can be used only when the buffer's ownership can be "transferred" to <see cref="ByteString"/>, for instance,
        /// the method can be used when the <see cref="ByteString"/> is transient.
        /// </remarks>
        public static ByteString UnsafeCreateFromBytes(byte[] buffer)
        {
            return UnsafeByteOperations.UnsafeWrap(buffer);
        }

        /// <summary>
        /// Extracts the underlying byte[] from the <see cref="ByteString"/>.
        /// </summary>
        /// <remarks>
        /// This method is unsafe, as expectation from the ByteString is that it's the sole owner of the underlying
        /// buffer.
        /// </remarks>
        public static byte[] UnsafeExtractBytes(ByteString byteString)
        {
            Contract.RequiresNotNull(byteString);
            var unsafeByteString = new UnsafeByteString()
            {
                ByteString = byteString,
            };

            return unsafeByteString.ByteStringClone!.Bytes!;
        }

        private class UnsafeByteStringExtractionHelper
        {
#pragma warning disable CS0649 // The Bytes field is never null, so no need to warn about it
            public readonly byte[]? Bytes;
#pragma warning restore CS0649
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct UnsafeByteString
        {
            [FieldOffset(0)]
            public ByteString ByteString;

            [FieldOffset(0)]
            public UnsafeByteStringExtractionHelper ByteStringClone;
        }
    }
}
