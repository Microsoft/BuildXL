// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Distributed;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public class TestPathTransformer : IAbsolutePathTransformer, IPathTransformer<AbsolutePath>
    {
        public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot)
        {
            return new MachineLocation(cacheRoot.Path);
        }

        public virtual AbsolutePath GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent)
        {
            string rootPath = new string(Encoding.Default.GetChars(contentLocationIdContent));
            return PathUtilities.GetContentPath(rootPath, contentHash);
        }

        public byte[] GetPathLocation(AbsolutePath path)
        {
            string rootPath = PathUtilities.GetRootPath(path);
            return Encoding.Default.GetBytes(rootPath.ToCharArray());
        }
    }
}
