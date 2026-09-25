using CausticsEngineering;
using CausticsEngineering.Io;

namespace CausticGenerator.Cli;

public static class Program
{
    private static readonly (OutputFormats Format, string Extension)[] SavedFormats =
    [
        (OutputFormats.Stl, "stl"),
        (OutputFormats.Obj, "obj"),
        (OutputFormats.Step, "stp"),
    ];

    public static int Main(string[] args)
    {
        if (!CommandLine.TryParse(args, out ParsedArguments? parsed, out string message))
        {
            Console.Error.WriteLine(message);

            return 1;
        }

        CausticsOptions options = parsed!.Options;

        if (!File.Exists(parsed.ImagePath))
        {
            Console.Error.WriteLine($"Image not found: {parsed.ImagePath}");

            return 1;
        }

        double[,] image = ImageIo.LoadGreyscale(parsed.ImagePath, options.ResizeTo);
        Console.WriteLine($"Loaded {image.GetLength(0)}x{image.GetLength(1)} image from {parsed.ImagePath}");

        CausticsEngine engine = new(options, Console.WriteLine);
        CausticsResult result = engine.EngineerCaustics(image);

        foreach ((OutputFormats format, string extension) in SavedFormats)
        {
            if (options.Formats.HasFlag(format))
            {
                Console.WriteLine(
                    $"Wrote lens mesh to {Path.Combine(options.OutputDirectory, $"original_image.{extension}")}");
            }
        }

        double widthMm = image.GetLength(0) * result.MmPerPixel;
        double heightMm = image.GetLength(1) * result.MmPerPixel;

        Console.WriteLine(
            $"Lens {widthMm:0.##} x {heightMm:0.##} mm at {result.MmPerPixel} mm/pixel, " +
            $"minimum depth {options.MinimumDepthMm} mm");

        return 0;
    }
}
