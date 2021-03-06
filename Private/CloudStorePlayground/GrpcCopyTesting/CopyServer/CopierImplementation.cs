﻿using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

using Google.Protobuf;
using Grpc.Core;

using Helloworld;

namespace CopyServer
{
    [Flags]
    public enum CopierImplementationMode
    {
        Normal = 0,
        SlowResponse = 1,
        SlowStreaming = 2,
        ThrowResponse = 4,
        ThrowOpening = 8,
        ThrowStreaming = 16,
        Throttled = 32
    }

    // Override server base generated by protogen to implement
    public class CopierImplementation : Copier.CopierBase
    {
        public CopierImplementation() : this(CopierImplementationMode.Normal)
        {

        }

        public CopierImplementation(CopierImplementationMode mode)
        {
            this.mode = mode;
            if ((mode & CopierImplementationMode.Throttled) != 0) this.MaxCount = 0;
        }

        private int currentCount = 0;

        public int CurrentCount { get => currentCount; }

        public int MaxCount { get; set; } = 255;

        private readonly CopierImplementationMode mode = CopierImplementationMode.Normal;

        public override async Task<WriteReply> Write(IAsyncStreamReader<Chunk> requestStream, ServerCallContext context)
        {
            Console.WriteLine("received request to write");

            Interlocked.Increment(ref currentCount);
            try
            {
                if (currentCount > MaxCount)
                {
                    throw new ThrottledException();
                }

                context.CancellationToken.ThrowIfCancellationRequested();

                Metadata requestHeaders = context.RequestHeaders;
                WriteRequest request = WriteRequest.FromMetadata(requestHeaders);

                string path = request.FileName ?? Path.GetRandomFileName();

                //Metadata responseHeaders = new Metadata();
                //await context.WriteResponseHeadersAsync(responseHeaders);

                long chunks, bytes;
                using (Stream stream = FileUtilities.OpenFileForWriting(path))
                {
                    byte[] buffer = new byte[CopyConstants.BufferSize];
                    (chunks, bytes) = await WriteContent(requestStream, buffer, stream, context.CancellationToken);
                }

                Console.WriteLine($"received {bytes} bytes in {chunks} chunks");
                Console.WriteLine($"completed write to {path}");

                return new WriteReply()
                {
                    FileName = path
                };

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref currentCount);
            }
        }

        private async Task<(long chunks, long bytes)> WriteContent(IAsyncStreamReader<Chunk> reader, byte[] buffer, Stream writer, CancellationToken ct = default(CancellationToken))
        {
            Debug.Assert(reader is object);
            Debug.Assert(buffer is object);
            Debug.Assert(writer is object);
            Debug.Assert(writer.CanWrite);

            long bytes = 0L, chunks = 0L;
            while (await reader.MoveNext(ct).ConfigureAwait(false))
            {
                Chunk chunk = reader.Current;
                int chunkSize = chunk.Content.Length;

                Debug.Assert(buffer.Length >= chunkSize);
                chunk.Content.CopyTo(buffer, 0);

                await writer.WriteAsync(buffer, 0, chunkSize, ct).ConfigureAwait(false);

                chunks++;
                bytes += chunkSize;
            }
            return (chunks, bytes);
        }

        public override async Task Read(ReadRequest request, IServerStreamWriter<Chunk> responseStream, ServerCallContext context)
        {
            Debug.Assert(!(request is null));
            Debug.Assert(!(responseStream is null));
            Debug.Assert(!(context is null));

            Interlocked.Increment(ref currentCount);
            try
            {
                Console.WriteLine($"received request for {request.FileName}");
                Console.WriteLine($"from {context.Peer} with {context.Deadline} deadline");
                Console.WriteLine($"currently serving {currentCount} requests");

                context.CancellationToken.ThrowIfCancellationRequested();

                if ((mode & CopierImplementationMode.SlowResponse) == CopierImplementationMode.SlowResponse)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15.0));
                }

                if ((mode & CopierImplementationMode.ThrowResponse) == CopierImplementationMode.ThrowResponse)
                {
                    throw new ApplicationException("Thrown while constructing response.");
                }

                (ReadResponse response, Stream stream) = GetFileStreamAndHeaders(request);
                using (stream)
                {
                    // This call is placed inside using block so that stream is disposed even if it fails.
                    await context.WriteResponseHeadersAsync(response.ToHeaders()).ConfigureAwait(false);

                    if (response.ErrorType is null)
                    {
                        Debug.Assert(stream is object);
                        Debug.Assert(response.ChunkSize > 0);
                        byte[] buffer = new byte[response.ChunkSize];
                        long chunks, bytes;
                        switch (response.Compression)
                        {
                            case CopyCompression.None:
                                (chunks, bytes) = await ReadContent(stream, buffer, responseStream, context.CancellationToken).ConfigureAwait(false);
                                break;
                            case CopyCompression.Gzip:
                                (chunks, bytes) = await StreamContentWithCompression(stream, buffer, responseStream, context.CancellationToken).ConfigureAwait(false);
                                break;
                            default:
                                throw new NotSupportedException();
                        }
                        Console.WriteLine($"wrote {bytes} bytes in {chunks} chunks");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"canceled = {context.CancellationToken.IsCancellationRequested}");
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref currentCount);
            }
        }

        private (ReadResponse response, Stream stream) GetFileStreamAndHeaders (ReadRequest request)
        {
            ReadResponse response = new ReadResponse();
            Stream stream = null;

            try
            {
                response.FileName = request.FileName;

                response.Offset = request.Offset;

                if (currentCount > MaxCount)
                {
                    throw new ThrottledException($"Copy request throttled because active count {currentCount} exceeds maximum count {MaxCount}.");
                }

                if ((mode & CopierImplementationMode.ThrowOpening) == CopierImplementationMode.ThrowOpening)
                {
                    throw new ApplicationException("Exception thrown while opening file");
                }

                stream = FileUtilities.OpenFileForReading(request.FileName);

                response.FileSize = stream.Length;

                Debug.Assert(stream.Position == 0L);
                stream.Seek(request.Offset, SeekOrigin.Begin);
                Debug.Assert(stream.Position == request.Offset);

                response.ChunkSize = CopyConstants.BufferSize;

                // Decide whether to use compression.
                if (request.Compression == CopyCompression.Gzip && response.FileSize > CopyConstants.BigSize)
                {
                     response.Compression = CopyCompression.Gzip;
                }
            }
            catch (Exception e)
            {
                response.ErrorType = e.GetType().Name;
                response.ErrorMessage = e.Message;
            }

            return (response, stream);

        }

        private async Task<(long, long)> StreamContentWithCompression (Stream reader, byte[] buffer, IServerStreamWriter<Chunk> responseStream, CancellationToken cts = default(CancellationToken))
        {
            Debug.Assert(!(reader is null));
            Debug.Assert(!(responseStream is null));

            long bytes = 0L;
            long chunks = 0L;            
            using (Stream grpcStream = new BufferedWriteStream(
                buffer,
                async (byte[] bf, int offset, int count) =>
                {
                    ByteString content = ByteString.CopyFrom(bf, offset, count);
                    Chunk reply = new Chunk() { Content = content, Index = chunks };
                    await responseStream.WriteAsync(reply);
                    bytes += count;
                    chunks++;
                }
            ))
                
            {
                using (Stream compressionStream = new GZipStream(grpcStream, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    await reader.CopyToAsync(compressionStream, buffer.Length, cts).ConfigureAwait(false);
                    await compressionStream.FlushAsync().ConfigureAwait(false);
                }
                await grpcStream.FlushAsync().ConfigureAwait(false);
            }

            return (chunks, bytes);

        }

        private async Task CopyContent(string file, IServerStreamWriter<Chunk> responseStream)
        {
            byte[] buffer = File.ReadAllBytes(file);
            ByteString content = ByteString.CopyFrom(buffer, 0, buffer.Length);
            Chunk reply = new Chunk() { Content = content, Index = 0 };
            await responseStream.WriteAsync(reply).ConfigureAwait(false);
        }

        // It's quite easy to stream without compression using the same
        // logic as with compression; just CopyToAsync(grpcStream) directly.
        // But that adds an extra buffer (inside the CopyToAsync method) and
        // a corresponding extra copy. So we keep the direct stream without
        // compression logic seperate.
        private async Task<(long chunks, long bytes)> ReadContent (Stream reader, byte[] buffer, IServerStreamWriter<Chunk> writer, CancellationToken cts = default(CancellationToken))
        {
            Debug.Assert(reader is object);
            Debug.Assert(reader.CanRead);
            Debug.Assert(writer is object);

            long bytes = 0L, chunks = 0L;
            int chunkSize = await reader.ReadAsync(buffer, 0, buffer.Length, cts).ConfigureAwait(false);
            while (true)
            {
                if (chunkSize == 0) break;

                if ((mode & CopierImplementationMode.SlowStreaming) == CopierImplementationMode.SlowStreaming)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15.0));
                }

                if ((mode & CopierImplementationMode.ThrowStreaming) == CopierImplementationMode.ThrowStreaming)
                {
                    throw new ApplicationException("Error while streaming");
                }

                ByteString content = ByteString.CopyFrom(buffer, 0, chunkSize);
                Chunk reply = new Chunk() { Content = content, Index = chunks };

                // Send the current buffer and fetch the next buffer simultaneously
                Task writeTask = writer.WriteAsync(reply);
                Task<int> readTask = reader.ReadAsync(buffer, 0, buffer.Length, cts);
                await Task.WhenAll(writeTask, readTask).ConfigureAwait(false);

                bytes += chunkSize;
                chunks++;
                Console.WriteLine(chunks);

                chunkSize = await readTask.ConfigureAwait(false);

            }

            return (chunks, bytes);
        } 
    }
}
