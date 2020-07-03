// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;

namespace BuildXL.Cache.Interfaces.Test
{
    /// <summary>
    /// Helper for producing random instances of the ICache types.
    /// </summary>
    public static class RandomHelpers
    {
        /// <summary>
        /// Create a random FullCacheRecord
        /// </summary>
        public static FullCacheRecord CreateRandomFullCacheRecord(string cacheId, CacheDeterminism determinism)
        {
            return new FullCacheRecord(CreateRandomStrongFingerprint(cacheId), CreateRandomCasEntries(determinism: determinism));
        }

        /// <summary>
        /// Create a random StrongFingerprint
        /// </summary>
        public static StrongFingerprint CreateRandomStrongFingerprint(string cacheId)
        {
            return new StrongFingerprint(CreateRandomWeakFingerprintHash(), CreateRandomCasHash(), new Hash(FingerprintUtilities.CreateRandom()), cacheId);
        }

        /// <summary>
        /// Create a random WeakFingerprintHash
        /// </summary>
        public static WeakFingerprintHash CreateRandomWeakFingerprintHash()
        {
            return WeakFingerprintHash.Random();
        }

        /// <summary>
        /// Create a random CasHash
        /// </summary>
        public static CasHash CreateRandomCasHash()
        {
            return new CasHash(new Hash(ContentHashingUtilities.CreateRandom()));
        }

        /// <summary>
        /// Create a random CasEntries
        /// </summary>
        public static CasEntries CreateRandomCasEntries(int casEntryCount = 2, CacheDeterminism determinism = default(CacheDeterminism))
        {
            var casHashes = Enumerable.Range(0, casEntryCount).Select(x => CreateRandomCasHash());
            return new CasEntries(casHashes, determinism);
        }
    }
}
