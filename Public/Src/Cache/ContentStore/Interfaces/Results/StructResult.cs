// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <nodoc />
    public static class StructResult
    {
        /// <nodoc />
        public static StructResult<T> Create<T>(T data) where T : struct => new StructResult<T>(data);

        /// <nodoc />
        public static StructResult<T> FromResult<T>(Result<T> result) where T : struct
        {
            if (result)
            {
                return new StructResult<T>(result.Value);
            }

            return new StructResult<T>(result);
        }

        /// <nodoc />
        public static StructResult<T> Success<T>(T data) where T : struct
            => new StructResult<T>(data);
    }

    /// <summary>
    ///     A boolean operation that returns a struct on success.
    ///     The type is obsolete in favor of <see cref="Result{T}"/>, please don't use it for new API.
    /// </summary>
    /// <remarks>
    /// Wrapper for value types over object types and created separately.
    /// this is because IEquatable doesn't support struct/value types.
    /// </remarks>
    public class StructResult<T> : BoolResult
        where T : struct
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult()
            : base(errorMessage: "The operation was unsuccessful")
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(T obj)
        {
            Data = obj;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Contract.RequiresNotNull(exception);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="StructResult{T}"/> class.
        /// </summary>
        public StructResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Contract.RequiresNotNull(other);
        }

        /// <summary>
        ///     Gets the result data.
        /// </summary>
        public T Data { get; }

        /// <nodoc />
        public static implicit operator StructResult<T>(T data)
        {
            return new StructResult<T>(data);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ Data.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Succeeded ? $"Success Data=[{Data}]" : GetErrorString();
        }
    }
}
