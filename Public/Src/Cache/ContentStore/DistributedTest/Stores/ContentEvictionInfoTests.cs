﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores
{
    public class ContentEvictionInfoTests
    {
        [Fact]
        public void TestContentEvictionInfoComparer()
        {
            var inputs = new []
                         {
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(20), TimeSpan.FromHours(2), replicaCount: 1, size: 1, rank: ReplicaRank.None),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(20), TimeSpan.FromHours(2), replicaCount: 1, size: 2, rank: ReplicaRank.Important),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(1), TimeSpan.FromHours(20), TimeSpan.FromHours(2), replicaCount: 1, size: 1, rank: ReplicaRank.Important),
                                                                                                                     
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(2), TimeSpan.FromHours(20), TimeSpan.FromHours(3), replicaCount: 1, size: 1, rank: ReplicaRank.Important),
                             new ContentEvictionInfo(ContentHash.Random(), TimeSpan.FromHours(2), TimeSpan.FromHours(20), TimeSpan.FromHours(3), replicaCount: 1, size: 1, rank: ReplicaRank.None),
                         };

            var list = inputs.ToList();
            list.Sort(ContentEvictionInfo.AgeBucketingPrecedenceComparer.Instance);

            var expected = new[]
                           {
                               inputs[4], // EffAge=3, Importance=false
                               inputs[3], // EffAge=3, Importance=true

                               inputs[0], // EffAge=2, Importance=false
                               inputs[1], // EffAge=2, Importance=true, Cost=2
                               inputs[2], // EffAge=2, Importance=true, Cost=1
                           };

            Assert.Equal(expected, list.ToArray());
        }
    }
}
