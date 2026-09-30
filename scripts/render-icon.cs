#:package Svg.Skia@5.2.3
#:property ManagePackageVersionsCentrally=false

using SkiaSharp;

using Svg.Skia;

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/render-icon.cs -- [resources directory]");
    return 2;
}

var resources = args.Length == 1 ? args[0] : Path.Join("BackupZCrypt.Desktop", "Resources");
var source = Path.Join(resources, "BackupZCrypt.svg");

int[] iconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const int WindowIconSize = 256;
const float ViewBoxSize = 256f;

using SKSvg svg = new();
var picture = svg.Load(source);

if (picture is null)
{
    Console.Error.WriteLine($"Could not read {source}.");
    return 1;
}

List<(int Size, byte[] Png)> images = [];

foreach (var size in iconSizes)
{
    images.Add((size, Render(picture, size)));
}

File.WriteAllBytes(
    Path.Join(resources, "BackupZCrypt.png"),
    images.Single(image => image.Size == WindowIconSize).Png
);

using (var ico = File.Create(Path.Join(resources, "BackupZCrypt.ico")))
using (BinaryWriter writer = new(ico))
{
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);

    var offset = 6 + (16 * images.Count);

    foreach (var (size, png) in images)
    {
        var dimension = (byte)(size >= 256 ? 0 : size);
        writer.Write(dimension);
        writer.Write(dimension);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(png.Length);
        writer.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
    {
        writer.Write(png);
    }
}

Console.WriteLine($"Rendered {source} into BackupZCrypt.png and a {images.Count}-size BackupZCrypt.ico.");
return 0;

static byte[] Render(SKPicture picture, int size)
{
    using var surface = SKSurface.Create(
        new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul)
    );
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / ViewBoxSize);
    canvas.DrawPicture(picture);
    canvas.Flush();

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
