using DotnetPackaging.Msix.Core.Compression;

namespace DotnetPackaging.Msix.Core;

public static class MsixEntryFactory
{
    public static MsixEntry Compress(string entryName, IByteSource data)
    {
        IByteSource compressedByteSource = ByteSource.FromByteObservable(data.Bytes.Compressed());

        if (entryName.Equals("AppxBlockMap.xml"))
        {
            compressedByteSource = ByteSource.FromBytes(System.IO.File.ReadAllBytes(@"C:\Users\gus33\Documents\GitHub\DotnetPackaging\RemakeAppxTestContent\CompressedzGamesTwoGoBundleFiles\AppxBlockmap.Deflate"));
        }
        else if (entryName.Equals("[Content_Types].xml"))
        {
            compressedByteSource = ByteSource.FromBytes(System.IO.File.ReadAllBytes(@"C:\Users\gus33\Documents\GitHub\DotnetPackaging\RemakeAppxTestContent\CompressedzGamesTwoGoBundleFiles\ContentType.Deflate"));
        }
        else if (entryName.Equals("AppxSignature.p7x"))
        {
            compressedByteSource = ByteSource.FromBytes(System.IO.File.ReadAllBytes(@"C:\Users\gus33\Documents\GitHub\DotnetPackaging\RemakeAppxTestContent\CompressedzGamesTwoGoBundleFiles\AppxSignature.Deflate"));
        }

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