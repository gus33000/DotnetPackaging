using DotnetPackaging.Msix.Core.Compression;

namespace DotnetPackaging.Msix.Core;

public static class MsixEntryFactory
{
    public static MsixEntry Compress(string entryName, IByteSource data)
    {
        var compressionLevel = CompressionLevel.Optimal;

        var msixEntry = new MsixEntry
        {
            Original = data,
            Compressed = ByteSource.FromByteObservable(data.Bytes.Compressed()),
            FullPath = entryName,
            CompressionLevel = compressionLevel,
            //2020-01-29 21:35:18
            ModificationTime = new DateTime(2020, 01, 29, 21, 35, 18, DateTimeKind.Utc)
        };

        return msixEntry;
    }
}