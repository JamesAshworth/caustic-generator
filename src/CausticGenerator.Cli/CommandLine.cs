using System.Globalization;
using CausticsEngineering;

namespace CausticGenerator.Cli;

public sealed record ParsedArguments(string ImagePath, CausticsOptions Options);

public static class CommandLine
{
    public const string Usage = """
        Usage: caustic-generator <image> [options]

        All lengths are millimetres. The pixel-to-millimetre scale is derived from the image's own
        longest edge, so nothing is tied to a particular image size. Aspect ratio is preserved
        throughout: --artifact-size is the lens's longest edge, and the other edge follows from the
        image's proportions.

          <image>                     Source image. Any format ImageSharp can read.

        Optics:
          --artifact-size <mm>        Longest edge of the printed lens. Default 100.
          --focal-length <mm>         Distance from lens to projection surface. Default 200.

        Solver:
          --iterations <n>            Outer march iterations. Default 4.
          --resize <n|none>           Cap the image's longest edge at n before solving, preserving
                                      aspect ratio, to cap solver cost. Default none.
          --loss-divisor <n|pixels>   Divisor used to zero-centre the loss field. Default is the
                                      pixel count. Pass 262144 for parity with the reference
                                      implementation, which hardcodes 512 * 512.

        Mesh:
          --minimum-depth <mm>        Material thickness at the thinnest point. The surface is
                                      shifted so its lowest point sits at z = 0 and the flat back
                                      face goes this far below it. Default 10.

        Output:
          --output <dir>              Where to write output. Defaults to the working directory.
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

                    options = options with { ArtifactSizeMm = artifactSize };
                    break;

                case "--focal-length":
                    if (!TryPositiveDouble(arg, value, out double focalLength, out message))
                    {
                        return false;
                    }

                    options = options with { FocalLengthMm = focalLength };
                    break;

                case "--resize":
                    if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        options = options with { ResizeTo = null };
                        break;
                    }

                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out int edge) || edge < 2)
                    {
                        message = $"Option {arg} needs a whole number of 2 or more, or 'none', got '{value}'.";

                        return false;
                    }

                    options = options with { ResizeTo = edge };
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

                case "--minimum-depth":
                    if (!TryPositiveDouble(arg, value, out double minimumDepth, out message))
                    {
                        return false;
                    }

                    options = options with { MinimumDepthMm = minimumDepth };
                    break;

                case "--output":
                    options = options with { OutputDirectory = value };
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

        if (positional.Count > 1)
        {
            message = $"Expected a single image, got {positional.Count} values. Use --output for the output directory.";

            return false;
        }

        parsed = new ParsedArguments(positional[0], options);

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
