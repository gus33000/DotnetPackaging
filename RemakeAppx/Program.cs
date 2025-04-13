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

            await MakeAppx(args[0], args[1], false, false);
        }

        private static async Task TestBundle()
        {
            await MakeAppx(
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_10.1906.55.0_x64__8wekyb3d8bbwe",
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_2020.1906.55.0_neutral_~_8wekyb3d8bbwe\Calculator_10.1906.55.0_x64.appx",
                false,
                false);

            await MakeAppx(
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_10.1906.55.0_neutral_split.scale-100_8wekyb3d8bbwe",
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_2020.190²6.55.0_neutral_~_8wekyb3d8bbwe\Calculator_10.1906.55.0_scale-100.appx",
                false,
                false);

            await MakeAppx(
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_10.1906.55.0_neutral_split.scale-125_8wekyb3d8bbwe",
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_2020.1906.55.0_neutral_~_8wekyb3d8bbwe\Calculator_10.1906.55.0_scale-125.appx",
                false,
                false);

            await MakeAppx(
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_2020.1906.55.0_neutral_~_8wekyb3d8bbwe",
                @"C:\Users\gus33\Downloads\BundleTest2\Microsoft.WindowsCalculator_2020.1906.55.0_neutral_~_8wekyb3d8bbwe.Rebuilt.appxbundle",
                true,
                false);
        }

        private static async Task MakeAppx(string inputFolder, string outputFile, bool bundleMode, bool unsignedMode)
        {
            Console.WriteLine("Making " + outputFile + " out of " + inputFolder);
            Console.WriteLine("Bundle Mode: " + bundleMode);
            Console.WriteLine("Unsigned Mode: " + unsignedMode);

            FileSystem fs = new FileSystem();
            IDirectoryInfo directoryInfo = fs.DirectoryInfo.New(inputFolder);
            IODir ioDir = new IODir(directoryInfo);

            await Msix.FromDirectory(ioDir, Maybe<ILogger>.None, bundleMode, unsignedMode, inputFolder)
                .Map(async source =>
                {
                    await using var fileStream = File.Open(outputFile, FileMode.Create);
                    return await source.DumpTo(fileStream);
                });
        }
    }
}
