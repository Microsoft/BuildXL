// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    public sealed class NativeUnitTests
    {
        [Fact]
        public void UsnRecordEquality()
        {
            var baseValue = new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend);
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new UsnRecord(new FileId(124, 456), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 457), new FileId(123, 457), new Usn(789), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(790), UsnChangeReasons.DataExtend),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 457), new Usn(790), UsnChangeReasons.DataOverwrite),
                                    new UsnRecord(new FileId(123, 456), new FileId(123, 458), new Usn(789), UsnChangeReasons.DataExtend)
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void MiniUsnRecordEquality()
        {
            var baseValue = new MiniUsnRecord(new FileId(123, 456), new Usn(789));
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new MiniUsnRecord(new FileId(123, 456), new Usn(790)),
                                    new MiniUsnRecord(new FileId(123, 457), new Usn(789)),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void FileIdEquality()
        {
            var baseValue = new FileId(123, 456);
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new FileId(123, 457),
                                    new FileId(124, 456),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [Fact]
        public void FileIdAndVolumeIdEquality()
        {
            var baseValue = new FileIdAndVolumeId(789, new FileId(123, 456));
            StructTester.TestEquality(
                baseValue: baseValue,
                equalValue: baseValue,
                notEqualValues: new[]
                                {
                                    new FileIdAndVolumeId(790, new FileId(123, 456)),
                                    new FileIdAndVolumeId(789, new FileId(124, 456)),
                                    new FileIdAndVolumeId(789, new FileId(123, 457)),
                                },
                eq: (a, b) => a == b,
                neq: (a, b) => a != b,
                skipHashCodeForNotEqualValues: false);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void OsVersionCheckConsistency()
        {
            // On Windows 8.1, this will return Windows 8.
            OperatingSystem version = Environment.OSVersion;
            XAssert.IsTrue(FileSystemWin.StaticIsOSVersionGreaterOrEqual(version.Version.Major, version.Version.Minor));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void OsVersionCheckNegative()
        {
            // This comment is a time capsule to the person who has to fix this test when 999.5 is a shipping Windows version
            // (perhaps Windows Aquamarine Space Station Edition): Sorry!

            // The major.minor version 999.5 is definitely not a valid Windows version as of writing (we are at 10.0 currently),
            // and shouldn't be for a long while. But it would be very unfortunate if IsOSVersionGreaterOrEqual always returned true.
            XAssert.IsFalse(FileSystemWin.StaticIsOSVersionGreaterOrEqual(999, 5));
        }
    }
}
