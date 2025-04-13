using System.Text;
using CSharpFunctionalExtensions;
using Zafiro.Mixins;

namespace DotnetPackaging.Msix.Core;

/// <summary>
/// Class for creating a ZIP file from pre-compressed entries.
/// Implements the bare minimum to write local headers,
/// the central directory, and the end of central directory record.
/// </summary>
public class MsixBuilder : IAsyncDisposable
{
    private readonly Stream baseStream;
    private readonly Maybe<ILogger> logger;
    private readonly List<MsixEntry> entries = new List<MsixEntry>();
    private readonly List<long> localHeaderOffsets = new List<long>();
    private bool finished = false;
    private readonly string inputPath;
    
    public MsixBuilder(Stream stream, Maybe<ILogger> logger, string inputPath)
    {
        this.inputPath = inputPath;
        this.logger = logger.Map(l => l.ForContext<MsixBuilder>());
        baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Adds a new pre-compressed entry to the ZIP.
    /// </summary>
    /// <param name="entry">The entry with all its metadata.</param>
    public async Task PutNextEntry(MsixEntry entry)
    {
        if (finished)
            throw new InvalidOperationException("ZIP has already been finalized.");

        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        // Record the current offset where the local header will be written
        long localHeaderOffset = baseStream.Position;
        localHeaderOffsets.Add(localHeaderOffset);

        await WriteLocalFileHeader(entry);
        logger.Debug("Dumping data for {Entry}", entry);
        await entry.Compressed.DumpTo(baseStream);

        if (!entry.FullPath.Equals("[Content_Types].xml") &&
            !entry.FullPath.Equals("AppxSignature.p7x") &&
            !entry.FullPath.Equals("AppxMetadata/CodeIntegrity.cat"))
        {
            await WriteDataDescriptor(entry);
        }

        //// Add the entry for the central directory
        entries.Add(entry);
    }

    private async Task WriteLocalFileHeader(MsixEntry entry)
    {
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            // Signature
            writer.Write(0x04034b50);

            // Version
            if (entry.FullPath.Equals("[Content_Types].xml") ||
                entry.FullPath.Equals("AppxSignature.p7x") ||
                entry.FullPath.Equals("AppxMetadata/CodeIntegrity.cat"))
            {
                writer.Write((short)20); // 0x0014

                // Flags: none
                writer.Write((short)0); // 0x0000
            }
            else
            {
                writer.Write((short)45); // 0x002D for MSIX

                // Flags: enable Data Descriptor (bit 3)
                writer.Write((short)8); // 0x0008
            }

            // Compression method
            short compressionMethod = (short)(entry.CompressionLevel == CompressionLevel.Optimal ? 8 : 0);
            writer.Write(compressionMethod);
            // Date/time
            // This is temp!
            //int dosTime = GetDosTime(entry.ModificationTime);
            string filePath = System.IO.Path.Combine(inputPath, entry.FullPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (entry.FullPath.Equals("[Content_Types].xml") && !System.IO.File.Exists(entry.FullPath))
            {
                filePath = System.IO.Path.Combine(inputPath, "AppxBlockMap.xml");
            }

            int dosTime = GetDosTime(System.IO.File.GetLastWriteTime(filePath));
            writer.Write(dosTime);

            if (entry.FullPath.Equals("[Content_Types].xml") ||
                entry.FullPath.Equals("AppxSignature.p7x") ||
                entry.FullPath.Equals("AppxMetadata/CodeIntegrity.cat"))
            {
                uint CRC32 = await entry.Original.Crc32();
                long compressedSize = await entry.Compressed.GetSize();
                long uncompressedSize = await entry.Original.GetSize();

                // CRC-32, compressed size and uncompressed size
                writer.Write((int)CRC32);                  // CRC-32
                writer.Write((int)compressedSize);   // Compressed size
                writer.Write((int)uncompressedSize); // Uncompressed size
            }
            else
            {
                // CRC-32, compressed size and uncompressed size: 0 (to be specified in Data Descriptor)
                writer.Write(0); // CRC-32
                writer.Write(0); // Compressed size
                writer.Write(0); // Uncompressed size
            }

            // Name
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FullPath);
            writer.Write((short)nameBytes.Length);
            
            // Extra field for Zip64
            writer.Write((short)0); // No extra field, but still using Zip64 features elsewhere
            
            // File name
            writer.Write(nameBytes);
        }
    }

    private async Task WriteDataDescriptor(MsixEntry entry)
    {
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            // Data Descriptor signature
            writer.Write(0x08074b50);
            // CRC-32
            writer.Write(await entry.Original.Crc32());

            // IMPORTANT: Write 8 bytes for each size (Zip64 format)
            writer.Write((uint)await entry.Compressed.GetSize());
            writer.Write((uint)0); // 4 more bytes to complete 8

            writer.Write((uint)await entry.Original.GetSize());
            writer.Write((uint)0); // 4 more bytes to complete 8
        }
    }

    private async Task WriteCentralDirectory()
    {
        long centralDirStart = baseStream.Position;
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MsixEntry entry = entries[i];
                long localHeaderOffset = localHeaderOffsets[i];
                byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FullPath);

                // Central header signature: 0x02014b50
                writer.Write(0x02014b50);
                writer.Write((short)45); // Version made by

                 // Version needed to extract
                if (entry.FullPath.Equals("[Content_Types].xml") ||
                    entry.FullPath.Equals("AppxSignature.p7x") ||
                    entry.FullPath.Equals("AppxMetadata/CodeIntegrity.cat"))
                {
                    writer.Write((short)20);
                    writer.Write((short)0);
                }
                else
                {
                    writer.Write((short)45);
                    writer.Write((short)8);  // General purpose flag (Data Descriptor)
                }

                short compressionMethod = (short)(entry.CompressionLevel == CompressionLevel.Optimal ? 8 : 0);
                writer.Write(compressionMethod);
                // This is temp!
                //int dosTime = GetDosTime(entry.ModificationTime);
                string filePath = System.IO.Path.Combine(inputPath, entry.FullPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (entry.FullPath.Equals("[Content_Types].xml") && !System.IO.File.Exists(entry.FullPath))
                {
                    filePath = System.IO.Path.Combine(inputPath, "AppxBlockMap.xml");
                }

                int dosTime = GetDosTime(System.IO.File.GetLastWriteTime(filePath));
                writer.Write((int)dosTime);
                writer.Write(await entry.Original.Crc32());

                writer.Write((uint)await entry.Compressed.GetSize());
                writer.Write((uint)await entry.Original.GetSize());

                writer.Write((short)nameBytes.Length);
                writer.Write((short)0); // Extra field size
                writer.Write((short)0); // Comment length
                writer.Write((short)0); // Disk number
                writer.Write((short)0); // Internal attributes
                writer.Write((int)0);   // External attributes

                writer.Write((uint)localHeaderOffset);

                writer.Write(nameBytes);
            }

            long centralDirSize = baseStream.Position - centralDirStart;

            WriteZip64EndOfCentralDirectoryRecord(centralDirStart, centralDirSize, entries.Count);
            WriteZip64EndOfCentralDirectoryLocator(baseStream.Position - 56);

            WriteEndOfCentralDirectory(centralDirStart, centralDirSize, entries.Count);
        }
    }

    private void WriteZip64EndOfCentralDirectoryRecord(long centralDirStart, long centralDirSize, int entryCount)
    {
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06064b50); // Zip64 EOCD Record signature
            writer.Write((ulong)44);  // Record size
            writer.Write((ushort)45); // Version made by
            writer.Write((ushort)45); // Version needed to extract
            writer.Write((uint)0);    // Disk number
            writer.Write((uint)0);    // Disk with central dir start
            writer.Write((ulong)entryCount);
            writer.Write((ulong)entryCount);
            writer.Write((ulong)centralDirSize);
            writer.Write((ulong)centralDirStart);
        }
    }

    private void WriteZip64EndOfCentralDirectoryLocator(long zip64EOCDPos)
    {
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x07064b50); // Zip64 EOCD Locator signature
            writer.Write((uint)0);    // Disk with EOCD
            writer.Write((ulong)zip64EOCDPos);
            writer.Write((uint)1);    // Total disks
        }
    }

    private void WriteEndOfCentralDirectory(long centralDirStart, long centralDirSize, int entryCount)
    {
        using (var writer = new BinaryWriter(baseStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06054b50); // EOCD signature

            writer.Write((ushort)0x0000); // Disk number
            writer.Write((ushort)0x0000); // Disk with central dir
            writer.Write((ushort)0xFFFF); // Entries on this disk
            writer.Write((ushort)0xFFFF); // Total entries
            writer.Write(0xFFFFFFFF);     // Central dir size
            writer.Write(0xFFFFFFFF);     // Offset of start

            writer.Write((ushort)0); // Comment length
        }
    }

    /// <summary>
    /// Finalizes the ZIP by writing the central directory.
    /// </summary>
    public async Task Finish()
    {
        if (!finished)
        {
            await WriteCentralDirectory();
            finished = true;
        }
    }

    /// <summary>
    /// Converts a DateTime to DOS format (packed into an int).
    /// </summary>
    private int GetDosTime(DateTime dt)
    {
        int dosTime = ((dt.Second / 2) & 0x1F)
                      | ((dt.Minute & 0x3F) << 5)
                      | ((dt.Hour & 0x1F) << 11);
        int dosDate = (dt.Day & 0x1F)
                      | ((dt.Month & 0x0F) << 5)
                      | (((dt.Year - 1980) & 0x7F) << 9);
        return (dosDate << 16) | dosTime;
    }

    public async ValueTask DisposeAsync()
    {
        if (!finished)
        {
            await Finish();
        }

        GC.SuppressFinalize(this);
    }
}
