// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Simply wraps a string.
    /// </summary>
    public readonly struct StringMoniker : IIpcMoniker, IEquatable<StringMoniker>
    {
        /// <inheritdoc />
        public string Id { get; }

        /// <nodoc />
        public StringMoniker(string id)
        {
            Contract.Requires(!string.IsNullOrEmpty(id));

            Id = id;
        }

        /// <summary>Returns true if <paramref name="obj"/> is of the same type and has the same <see cref="Id"/>.</summary>
        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return Id == ((StringMoniker)obj).Id;
        }

        /// <summary>Returns the hash code of the <see cref="Id"/> property.</summary>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        bool IEquatable<StringMoniker>.Equals(StringMoniker other) => Equals(other);

        bool IEquatable<IIpcMoniker>.Equals(IIpcMoniker other) => Equals(other);
    }
}
