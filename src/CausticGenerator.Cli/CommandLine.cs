using System.Globalization;
using CausticsEngineering;

namespace CausticGenerator.Cli;

public sealed record ParsedArguments(string ImagePath, CausticsOptions Options);

public static class CommandLine
{
    public const string Usage = """
        Usage: caustic-generator <image> [output-directory] [options]

          <image>                     Source image. Any format ImageSharp can read.
          [output-directory]          Where to write output. Defaults to ./output.

        Optics:
          --artifact-size <metres>    Width of the printed lens. Default 0.1.
          --focal-length <metres>     Distance from lens to projection surface. Default 0.2.

        Solver:
          --iterations <n>            Outer march iterations. Default 4.
          --loss-divisor <n|pixels>   Divisor used to zero-centre the loss field. Default 262144
                                      (512 * 512), matching the reference implementation whatever
                                      the image size. Pass `pixels` to use the true pixel count.

        Mesh:
          --height-scale <x>          Multiplier applied to solved heights. Default 1.
          --height-offset <x>         Constant added to solved heights. Default 10.
          --solidify-offset <x>       Depth of the flat bottom below the lens. Default 100.

        Output:
          --output <dir>              Same as the positional output directory.
          --no-loss-images            Skip the per-iteration loss PNG diagnostics.
          --save-obj                  Also write OBJ alongside the STL.
          -h, --help                  Show this help.
        """;

    /// Parses argv into an image path and a fully populated CausticsOptions.
    /// Returns false with a message on bad input, or on --help.
    public static bool TryParse(string[] args, out ParsedArguments? parsed, out string message)
    {
        ArgumentNullException.ThrowIfNull(args);

        parsed = null;
        message = string.Empty;

        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            message = Usage;

            return false;
        }

        List<string> positional = [];
        CausticsOptions options = new();
        string? outputFlag = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--no-loss-images":
                    options = options with { SaveLossImages = false };
                    continue;

                case "--save-obj":
                    options = options with { AlsoSaveObj = true };
                    continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                message = $"Option {arg} needs a value.";

                return false;
            }

            string value = args[++i];

            switch (arg)
            {
                case "--artifact-size":
                    if (!TryPositiveDouble(arg, value, out double artifactSize, out message))
                    {
                        return false;
                    }

                    options = options with { ArtifactSizeMeters = artifactSize };
                    break;

                case "--focal-length":
                    if (!TryPositiveDouble(arg, value, out double focalLength, out message))
                    {
                        return false;
                    }

                    options = options with { FocalLengthMeters = focalLength };
                    break;

                case "--iterations":
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out int iterations) || iterations < 1)
                    {
                        message = $"Option {arg} needs a positive whole number, got '{value}'.";

                        return false;
                    }

                    options = options with { Iterations = iterations };
                    break;

                case "--loss-divisor":
                    if (value.Equals("pixels", StringComparison.OrdinalIgnoreCase))
                    {
                        // Null makes the engine size the divisor from the image itself
                        options = options with { LossNormalisationDivisor = null };
                        break;
                    }

                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out int divisor) || divisor < 1)
                    {
                        message = $"Option {arg} needs a positive whole number or 'pixels', got '{value}'.";

                        return false;
                    }

                    options = options with { LossNormalisationDivisor = divisor };
                    break;

                case "--height-scale":
                    if (!TryDouble(arg, value, out double heightScale, out message))
                    {
                        return false;
                    }

                    options = options with { HeightScale = heightScale };
                    break;

                case "--height-offset":
                    if (!TryDouble(arg, value, out double heightOffset, out message))
                    {
                        return false;
                    }

                    options = options with { HeightOffset = heightOffset };
                    break;

                case "--solidify-offset":
                    if (!TryDouble(arg, value, out double solidifyOffset, out message))
                    {
                        return false;
                    }

                    options = options with { SolidifyOffset = solidifyOffset };
                    break;

                case "--output":
                    outputFlag = value;
                    break;

                default:
                    message = $"Unknown option {arg}.";

                    return false;
            }
        }

        if (positional.Count == 0)
        {
            message = "No image given.";

            return false;
        }

        if (positional.Count > 2)
        {
            message = $"Expected at most an image and an output directory, got {positional.Count} values.";

            return false;
        }

        // An explicit --output wins over the positional form
        string outputDirectory = outputFlag
            ?? (positional.Count == 2 ? positional[1] : Path.Combine(Environment.CurrentDirectory, "output"));

        parsed = new ParsedArguments(positional[0], options with { OutputDirectory = outputDirectory });

        return true;
    }

    private static bool TryDouble(string option, string value, out double parsed, out string message)
    {
        message = string.Empty;
        if (double.TryParse(value, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        message = $"Option {option} needs a number, got '{value}'.";

        return false;
    }

    private static bool TryPositiveDouble(string option, string value, out double parsed, out string message)
    {
        if (!TryDouble(option, value, out parsed, out message))
        {
            return false;
        }

        if (parsed <= 0)
        {
            message = $"Option {option} needs a positive number, got '{value}'.";

            return false;
        }

        return true;
    }
}
