// (c) J@mBeL.net 2010-2026. This file is part of jaNET Framework, GNU GPL version 3 or later.
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace jaNET.ReleaseTools;

internal static class ReleaseArchive
{
    internal static void Create(string target, string top, string folder, bool zip)
    {
        if (string.IsNullOrEmpty(top) || top is "." or ".." || top.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("The archive must have a single top-level folder.", nameof(top));
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream output = File.Create(temporary))
            {
                if (zip) WriteZip(output, top, folder);
                else WriteTar(output, top, folder);
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteZip(Stream output, string top, string folder)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (string path in Entries(folder))
        {
            string name = top + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (Directory.Exists(path)) archive.CreateEntry(name + "/");
            else archive.CreateEntryFromFile(path, name, CompressionLevel.SmallestSize);
        }
    }

    private static void WriteTar(Stream output, string top, string folder)
    {
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        using var archive = new TarWriter(gzip, leaveOpen: true);
        WriteDirectory(archive, top);
        foreach (string path in Entries(folder))
        {
            string name = top + "/" + Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (Directory.Exists(path)) WriteDirectory(archive, name);
            else
            {
                using FileStream input = File.OpenRead(path);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = input,
                    Mode = (UnixFileMode)(Path.GetFileName(path) is "jaNETProgram" or "createdump" ? 493 : 420)
                };
                archive.WriteEntry(entry);
            }
        }
    }

    private static void WriteDirectory(TarWriter archive, string name) =>
        archive.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { Mode = (UnixFileMode)493 });

    private static string[] Entries(string folder)
    {
        // Do not follow links outside the freshly published folder.
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links are not supported in release output.");
            foreach (string path in Directory.EnumerateFileSystemEntries(current))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Symbolic links are not supported in release output.");
                entries.Add(path);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
        return entries.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}
