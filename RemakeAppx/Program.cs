using System.IO.Abstractions;
using System.Reactive.Linq;
using CSharpFunctionalExtensions;
using DotnetPackaging.Msix;
using Zafiro.DivineBytes;
using Serilog;
using RemakeAppx.Helpers;
using File = System.IO.File;

namespace RemakeAppx
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("RemakeAppx - Version 0.0.0.2");
            Console.WriteLine("Copyright (c) 2025 Gustave Monce");
            Console.WriteLine("Copyright (c) 2024 José Manuel Nieto");
            Console.WriteLine();

            if (args.Count() != 2)
            {
                Console.WriteLine("Usage: <path to Windows Apps folder (with AppxSignature.p7x and AppxMetadata\\CodeIntegrity.cat)> <Output path for the appx to remake out of this path>");

                return;
            }

            if (!System.IO.Directory.Exists(args[0]))
            {
                Console.WriteLine("Provided directory: ");
                Console.WriteLine(args[0]);
                Console.WriteLine("does not exist.");

                return;
            }

            FileSystem fs = new FileSystem();
            IDirectoryInfo directoryInfo = fs.DirectoryInfo.New(args[0]);
            IODir ioDir = new IODir(directoryInfo);

            await Msix.FromDirectory(ioDir, Maybe<ILogger>.None)
                .Map(async source =>
                {
                    await using var fileStream = File.Open(args[1], FileMode.Create);
                    return await source.DumpTo(fileStream);
                });
        }
    }
}
