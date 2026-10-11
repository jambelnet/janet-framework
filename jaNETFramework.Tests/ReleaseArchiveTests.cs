// (c) J@mBeL.net 2010-2026. This file is part of jaNET Framework, GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using jaNET.ReleaseTools;
using Xunit;

namespace jaNETFramework.Tests;

public sealed class ReleaseArchiveTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArchivesContainFullOutputAndUnixExecutableModes(bool zip)
    {
        string temporary = Directory.CreateTempSubdirectory("janet-archive-tests-").FullName;
        try
        {
            string folder = Path.Combine(temporary, "published with spaces");
            Directory.CreateDirectory(Path.Combine(folder, "www", "empty"));
            var files = new Dictionary<string, string>
            {
                ["jaNETProgram"] = "app", ["createdump"] = "dump", ["README.md"] = "readme",
                ["LICENSE"] = "license", ["janet.service"] = "service", ["www/index.html"] = "web"
            };
            foreach (var file in files) File.WriteAllText(Path.Combine(folder, file.Key), file.Value);
            string target = Path.Combine(temporary, zip ? "release.zip" : "release.tar.gz");
            File.WriteAllText(target, "previous archive");
            ReleaseArchive.Create(target, "janet-test-linux-x64", folder, zip);
            var actual = new Dictionary<string, string>();
            if (zip)
            {
                using var archive = ZipFile.OpenRead(target);
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/empty/", StringComparison.Ordinal));
                foreach (var entry in archive.Entries.Where(entry => entry.Name.Length > 0))
                {
                    using var reader = new StreamReader(entry.Open());
                    actual.Add(entry.FullName, reader.ReadToEnd());
                }
            }
            else
            {
                using var input = File.OpenRead(target);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var archive = new TarReader(gzip);
                TarEntry entry;
                bool empty = false;
                while ((entry = archive.GetNextEntry()) != null)
                {
                    bool directory = entry.EntryType == TarEntryType.Directory;
                    bool executable = Path.GetFileName(entry.Name) is "jaNETProgram" or "createdump";
                    Assert.Equal((UnixFileMode)(directory || executable ? 493 : 420), entry.Mode);
                    if (directory) { empty |= entry.Name.EndsWith("/empty", StringComparison.Ordinal); continue; }
                    using var reader = new StreamReader(entry.DataStream);
                    actual.Add(entry.Name, reader.ReadToEnd());
                }
                Assert.True(empty);
            }
            Assert.Equal(files.Count, actual.Count);
            foreach (var file in files) Assert.Equal(file.Value, actual["janet-test-linux-x64/" + file.Key]);
            Assert.Empty(Directory.GetFiles(temporary, "*.tmp"));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("..")]
    [InlineData("")]
    public void RejectsUnsafeTopLevelFolder(string top)
    {
        Assert.Throws<ArgumentException>(() => ReleaseArchive.Create("unused", top, "unused", false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedPackagingPreservesPreviousArchiveAndRemovesTemporaryFile(bool zip)
    {
        string temporary = Directory.CreateTempSubdirectory("janet-archive-failure-").FullName;
        try
        {
            string target = Path.Combine(temporary, "release");
            File.WriteAllText(target, "previous archive");
            Assert.ThrowsAny<IOException>(() => ReleaseArchive.Create(target, "janet-test", Path.Combine(temporary, "missing"), zip));
            Assert.Equal("previous archive", File.ReadAllText(target));
            Assert.Single(Directory.GetFiles(temporary));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
