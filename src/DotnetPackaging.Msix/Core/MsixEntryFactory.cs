using DotnetPackaging.Msix.Core.Compression;

namespace DotnetPackaging.Msix.Core;

public static class MsixEntryFactory
{
    public static MsixEntry Compress(string entryName, IByteSource data)
    {
        IByteSource compressedByteSource = ByteSource.FromByteObservable(data.Bytes.Compressed());

        var compressionLevel = CompressionLevel.Optimal;

        var msixEntry = new MsixEntry
        {
            Original = data,
            Compressed = compressedByteSource,
            FullPath = entryName,
            CompressionLevel = compressionLevel
        };

        return msixEntry;
    }
}