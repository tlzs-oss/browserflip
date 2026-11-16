using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace BrowserFlip.IconGen;

/// <summary>
/// Generates assets/icon/: browserflip.svg, browserflip-small.svg, browserflip-16.svg, png/browserflip-*.png,
/// browserflip.ico and preview.png. Usage: BrowserFlip.IconGen [output-dir]
/// (default: assets/icon under the repository root, located via BrowserFlip.slnx).
/// </summary>
internal static class Program
{
    /// <summary>Sizes at or below this use the simplified artwork (16 px uses the pixel-fitted one).</summary>
    private const int SmallMax = 32;

    private static readonly int[] PngSizes = [16, 24, 32, 48, 64, 128, 256, 512];
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 256];
    private static readonly int[] PreviewSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindRepoRoot(), "assets", "icon");
        var pngDir = Path.Combine(outDir, "png");
        Directory.CreateDirectory(pngDir);

        var full = Design.Full();
        var small = Design.Small();
        var tiny = Design.Tiny();
        var utf8 = new UTF8Encoding(false);

        File.WriteAllText(Path.Combine(outDir, "browserflip.svg"), SvgWriter.Write(full), utf8);
        File.WriteAllText(Path.Combine(outDir, "browserflip-small.svg"), SvgWriter.Write(small), utf8);
        File.WriteAllText(Path.Combine(outDir, "browserflip-16.svg"), SvgWriter.Write(tiny), utf8);

        var renders = new Dictionary<int, BitmapSource>();
        foreach (var size in PngSizes.Concat(IcoSizes).Concat(PreviewSizes).Distinct().Order())
        {
            renders[size] = Renderer.Render(size <= 16 ? tiny : size <= SmallMax ? small : full, size);
        }

        foreach (var size in PngSizes)
        {
            File.WriteAllBytes(Path.Combine(pngDir, string.Create(CultureInfo.InvariantCulture, $"browserflip-{size}.png")), Renderer.EncodePng(renders[size]));
        }

        var icoPath = Path.Combine(outDir, "browserflip.ico");
        IcoWriter.Write(icoPath, IcoSizes.Select(s => renders[s]).ToList());

        File.WriteAllBytes(Path.Combine(outDir, "preview.png"), Renderer.EncodePng(Preview.Build(renders, PreviewSizes)));

        Console.WriteLine("Wrote icon assets to " + outDir);
        Console.WriteLine("ICO entries:");
        foreach (var e in IcoWriter.Inspect(icoPath))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {e.Width}x{e.Height} {e.Bpp}bpp {e.Format} {e.Bytes} bytes"));
        }

        return 0;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BrowserFlip.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root (BrowserFlip.slnx) not found; pass the output directory explicitly.");
    }
}
