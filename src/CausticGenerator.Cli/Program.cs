using CausticsEngineering;
using CausticsEngineering.Io;

namespace CausticGenerator.Cli;

public static class Program
{
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

        Console.WriteLine($"Wrote lens mesh to {Path.Combine(options.OutputDirectory, "original_image.stl")}");
        if (options.AlsoSaveObj)
        {
            Console.WriteLine($"Wrote lens mesh to {Path.Combine(options.OutputDirectory, "original_image.obj")}");
        }

        double widthMm = image.GetLength(0) * result.MmPerPixel;
        double heightMm = image.GetLength(1) * result.MmPerPixel;

        Console.WriteLine(
            $"Lens {widthMm:0.##} x {heightMm:0.##} mm at {result.MmPerPixel} mm/pixel, " +
            $"minimum depth {options.MinimumDepthMm} mm");

        return 0;
    }
}
