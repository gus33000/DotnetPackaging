using System.Diagnostics;
using Zafiro.Reactive;

namespace DotnetPackaging.Msix.Core
{
    public static class MakeAppxDeflate
    {
        public static IObservable<byte[]> GetMakeAppxVersionOfDeflate(IByteSource source)
        {
            using var MemoryStream = new MemoryStream();
            source.DumpTo(MemoryStream).GetAwaiter().GetResult();

            return ByteSource.FromBytes(GetMakeAppxVersionOfDeflate(MemoryStream.ToArray()).GetAwaiter().GetResult());
        }

        public static async Task<byte[]?> GetMakeAppxVersionOfDeflate(byte[] inputBuffer)
        {
            string makeAppxLocation = @"C:\Users\gus33\Documents\GitHub\DotnetPackaging\RemakeAppxTestContent\19599 ITS\Windows Kits\10\tools\bin\i386\makeappx.exe";

            string AppxContent = "C:\\Users\\gus33\\Documents\\GitHub\\DotnetPackaging\\RemakeAppxTestContent\\Minimal\\Contents";
            string AppxOutput = "C:\\Users\\gus33\\Documents\\GitHub\\DotnetPackaging\\RemakeAppxTestContent\\Minimal\\App.appx";

            if (System.IO.File.Exists(AppxOutput))
            {
                await Task.Delay(1000);

                System.IO.File.Delete(AppxOutput);
            }

            System.IO.File.WriteAllBytes(System.IO.Path.Combine(AppxContent, "HelloWorld.exe"), inputBuffer);

            string args = $"pack -d \"{AppxContent}\" -p \"{AppxOutput}\" -l";

            Process proc = new()
            {
                StartInfo = new ProcessStartInfo(makeAppxLocation, args)
                {
                    RedirectStandardOutput = false,
                    RedirectStandardInput = false
                },
            };
            proc.Start();
            await proc.WaitForExitAsync();

            List<byte> compressed = [];

            using (Stream appxStream = System.IO.File.OpenRead(AppxOutput))
            {
                appxStream.Seek(0x118, SeekOrigin.Begin);

                while (true)
                {
                    int b = appxStream.ReadByte();

                    if (b == -1)
                    {
                        break;
                    }

                    compressed.Add((byte)b);

                    if (compressed.Count > 4 && compressed[^1] == 0x08 && compressed[^2] == 0x07 && compressed[^3] == 0x4B && compressed[^4] == 0x50)
                    {
                        compressed.RemoveRange(compressed.Count - 5, 4);
                        break;
                    }
                }
            }

            if (System.IO.File.Exists(AppxOutput))
            {
                System.IO.File.Delete(AppxOutput);
            }

            if (compressed[^1] == 0x00 && compressed[^2] == 0x03 && compressed[^3] == 0xFF && compressed[^4] == 0xFF)
            {
                return [.. compressed];
            }

            if (compressed[^1] == 0x08 && compressed[^2] == 0x03 && compressed[^3] == 0xFF && compressed[^4] == 0xFF)
            {
                compressed[^1] = 0x00;
                return [.. compressed];
            }

            /*if (compressed[^1] == 0x00 && compressed[^2] == 0x03 && compressed[^3] == 0xFF && compressed[^4] == 0xFF)
            {
                return [.. compressed];
            }

            if (compressed[^1] == 0x08 && compressed[^2] == 0x03 && compressed[^3] == 0xFF && compressed[^4] == 0xFF)
            {
                compressed[^1] = 0x00;
                return [.. compressed];
            }

            // FF FF 03 00
            if (compressed[^1] == 0x00 && compressed[^2] == 0x03 && compressed[^3] == 0xFF && compressed[^4] == 0xFF)
            {
                return [.. compressed];
            }
            else if (compressed[^1] == 0x03 && compressed[^2] == 0xFF && compressed[^3] == 0xFF)
            {
                return [.. compressed, 0x00];
            }
            else if (compressed[^1] == 0xFF && compressed[^2] == 0xFF)
            {
                return [.. compressed, 0x03, 0x00];
            }
            else if (compressed[^1] == 0xFF)
            {
                return [.. compressed, 0xFF, 0x03, 0x00];
            }
            else
            {
                return [.. compressed, 0xFF, 0xFF, 0x03, 0x00];
            }*/
            return [.. compressed];
        }
    }
}
