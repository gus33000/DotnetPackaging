using System.Text;
using BlockCompressor;
using CSharpFunctionalExtensions;
using DotnetPackaging.Msix.Core.BlockMap;
using DotnetPackaging.Msix.Core.Compression;
using DotnetPackaging.Msix.Core.ContentTypes;
using Zafiro.Mixins;
using Zafiro.Reactive;

namespace DotnetPackaging.Msix.Core;

public class MsixPackager(Maybe<ILogger> logger)
{
    private const bool USE_EXTERNAL_CONTENTTYPE = true;
    private const string EXTERNAL_CONTENTTYPE_LOCATION = @"C:\Users\gus33\Documents\GitHub\DotnetPackaging\RemakeAppxTestContent\Microsoft.zGamesTwoGo_8wekyb3d8bbwe\[Content_Types].xml";
    private const bool USE_EXISTING_BLOCKMAP = true;

    private string[] NoCompressionExtensions =
    [
        "appx",
        "avi",
        "cab",
        "docm",
        "docx",
        "dotm",
        "dotx",
        "gif",
        "gz",
        "jpeg",
        "jpg",
        "m4a",
        "mov",
        "mp3",
        "mpeg",
        "mpg",
        "png",
        "potm",
        "potx",
        "ppam",
        "ppsm",
        "ppsx",
        "pptm",
        "pptx",
        "rar",
        "tar",
        "wma",
        "wmv",
        "xap",
        "xlam",
        "xlsb",
        "xlsm",
        "xlsx",
        "xltm",
        "xltx",
        "zip"
    ];

    public Result<IByteSource> Pack(IDirectory directory, bool bundleMode, bool unsignedMode, string inputPath)
    {
        IEnumerable<INamedByteSourceWithPath> files = directory.FilesWithPathsRecursive();

        files = files.Where(file =>
        {
            if (file.Name.Equals("[Content_Types].xml"))
                return false;

            if (unsignedMode)
            {
                if (file.Name.Equals("AppxSignature.p7x"))
                    return false;

                if (file.Path.Value.Equals("AppxMetadata") && file.Name.Equals("CodeIntegrity.cat"))
                    return false;
            }

            return true;
        });

        return Result.Success()
            .Map(() => Compress(files, bundleMode, unsignedMode, inputPath));
    }

    private IByteSource Compress(IEnumerable<INamedByteSourceWithPath> files, bool bundleMode, bool unsignedMode, string inputPath)
    {
        return ByteSource.FromAsyncStreamFactory(() => GetStream(files.ToList(), bundleMode, unsignedMode, inputPath));
    }

    private async Task<Stream> GetStream(IList<INamedByteSourceWithPath> files, bool bundleMode, bool unsignedMode, string inputPath)
    {
        var zipStream = new MemoryStream();
        
        await using (var zipper = new MsixBuilder(zipStream, logger, inputPath))
        {
            await WritePayload(files, zipper, bundleMode);
            await WriteContentTypes(files, zipper, bundleMode);

            if (!unsignedMode && !bundleMode)
                await AddAppxMetadata(zipper, files);

            if (!unsignedMode)
                await AddAppxSignature(zipper, files);
        }
        
        var finalStream = new MemoryStream();
        zipStream.Position = 0;
        await zipStream.CopyToAsync(finalStream);

        finalStream.Position = 0;
        return finalStream;
    }

    private static async Task WriteContentTypes(IEnumerable<INamedByteSourceWithPath> files, MsixBuilder msix, bool bundleMode)
    {
        if (USE_EXTERNAL_CONTENTTYPE)
        {
            // Use external content type
            var bytes = System.IO.File.ReadAllBytes(EXTERNAL_CONTENTTYPE_LOCATION);

            await msix.PutNextEntry(MsixEntryFactory.Compress("[Content_Types].xml", ByteSource.FromBytes(bytes)));
        }
        else
        {
            var allFileNames = files.Select(x => x.Name);
            if (!allFileNames.Contains("AppxBlockMap.xml"))
            {
                allFileNames.Append("AppxBlockMap.xml");
            }

            var contentTypes = ContentTypesGenerator.Create(allFileNames, bundleMode);
            var xml = ContentTypesSerializer.Serialize(contentTypes);
            await msix.PutNextEntry(MsixEntryFactory.Compress("[Content_Types].xml", ByteSource.FromString(xml, Encoding.UTF8)));
        }

    }

    private async Task WritePayload(IEnumerable<INamedByteSourceWithPath> files, MsixBuilder msix, bool bundleMode)
    {
        var blockInfos = new List<FileBlockInfo>();

        foreach (var file in files)
        {
            if (file.Name.Equals("AppxBlockMap.xml"))
                continue;

            if (file.Name.Equals("AppxSignature.p7x"))
                continue;

            if (file.Path.Value.Equals("AppxMetadata") && file.Name.Equals("CodeIntegrity.cat"))
                continue;

            logger.Debug("Processing {File}", file.FullPath());
            MsixEntry entry;
            IList<DeflateBlock> blocks;
            
            if (NoCompressionExtensions.Any(t => file.Name.EndsWith("." + t)))
            {
                entry = new MsixEntry()
                {
                    Compressed = file,
                    Original = file,
                    FullPath = file.FullPath(),
                    CompressionLevel = CompressionLevel.NoCompression
                };

                blocks = await file.Bytes.Flatten().Buffer(64 * 1024).Select(list => new DeflateBlock
                {
                    CompressedData = list.ToArray(),
                    OriginalData = list.ToArray(),
                }).ToList();
            }
            else
            {
                var compressionBlocks = file.Bytes.CompressionBlocks();
                entry = new MsixEntry
                {
                    Original = file,
                    Compressed = ByteSource.FromByteObservable(compressionBlocks.Select(x => x.CompressedData)),
                    FullPath = file.FullPath(),
                    CompressionLevel = CompressionLevel.Optimal
                };

                blocks = await compressionBlocks.ToList();
            }
            
            await msix.PutNextEntry(entry);
            
            logger.Debug("Added entry for {File}", file.FullPath());

            if (!bundleMode || !file.FullPath().Value.EndsWith("appx"))
            {
                var fileBlockInfo = new FileBlockInfo(entry, blocks);
                blockInfos.Add(fileBlockInfo);
            }
        }
        
        await AddBlockMap(msix, blockInfos, files);
    }

    private async Task AddBlockMap(MsixBuilder msix, List<FileBlockInfo> blockInfos, IEnumerable<INamedByteSourceWithPath> files)
    {
        if (USE_EXISTING_BLOCKMAP)
        {
            IObservable<byte[]> blockMapXml = files.First(t => t.Name.Equals("AppxBlockMap.xml")).Bytes;

            logger.Debug("Adding Block Map entry to package");
            await msix.PutNextEntry(MsixEntryFactory.Compress("AppxBlockMap.xml", ByteSource.FromByteObservable(blockMapXml)));
        }
        else
        {
            logger.Debug("Adding Block Map");
            var blockMapModel = new BlockMapModel("SHA256", blockInfos.ToImmutableList());
            logger.Debug("Serializing block map");
            var blockMapXml = await new BlockMapSerializer(logger).GenerateBlockMapXml(blockMapModel);
            logger.Debug("Block map serialized");

            logger.Debug("Adding Block Map entry to package");
            await msix.PutNextEntry(MsixEntryFactory.Compress("AppxBlockMap.xml", ByteSource.FromString(blockMapXml, Encoding.UTF8)));
        }

        logger.Debug("Block map added");
    }

    private async Task AddAppxMetadata(MsixBuilder msix, IEnumerable<INamedByteSourceWithPath> files)
    {
        if (!files.Any(t => t.Path.Value.Equals("AppxMetadata") && t.Name.Equals("CodeIntegrity.cat")))
        {
            return;
        }

        IObservable<byte[]> AppxMetadata = files.First(t => t.Path.Value.Equals("AppxMetadata") && t.Name.Equals("CodeIntegrity.cat")).Bytes;

        logger.Debug("Adding AppxMetadata entry to package");
        await msix.PutNextEntry(MsixEntryFactory.Compress("AppxMetadata/CodeIntegrity.cat", ByteSource.FromByteObservable(AppxMetadata)));

        logger.Debug("AppxMetadata added");
    }

    private async Task AddAppxSignature(MsixBuilder msix, IEnumerable<INamedByteSourceWithPath> files)
    {
        if (!files.Any(t => t.Name.Equals("AppxSignature.p7x")))
        {
            return;
        }

        IObservable<byte[]> AppxSignature = files.First(t => t.Name.Equals("AppxSignature.p7x")).Bytes;

        logger.Debug("Adding AppxSignature.p7x entry to package");
        await msix.PutNextEntry(MsixEntryFactory.Compress("AppxSignature.p7x", ByteSource.FromByteObservable(AppxSignature)));

        logger.Debug("AppxSignature.p7x added");
    }
}