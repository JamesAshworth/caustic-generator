using CausticGenerator.Cli;
using CausticsEngineering;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class CommandLineTests
{
    private static readonly IList<OptionCase> OptionCases =
    [
        new("--artifact-size", "250", o => o.ArtifactSizeMm, 250.0),
        new("--focal-length", "1500", o => o.FocalLengthMm, 1500.0),
        new("--refractive-index", "1.565", o => o.RefractiveIndex, 1.565),
        new("--iterations", "9", o => o.Iterations, 9),
        new("--resize", "256", o => o.ResizeTo, 256),
        new("--loss-divisor", "1024", o => o.LossNormalisationDivisor, 1024),
        new("--minimum-depth", "3.5", o => o.MinimumDepthMm, 3.5),
    ];

    private static readonly IList<string[]> RejectedCases =
    [
        ["--artifact-size", "nonsense", "cat.jpg"],
        ["--artifact-size", "0", "cat.jpg"],
        ["--artifact-size", "-1", "cat.jpg"],
        ["--focal-length", "0", "cat.jpg"],
        ["--refractive-index", "1", "cat.jpg"],
        ["--refractive-index", "0.9", "cat.jpg"],
        ["--refractive-index", "glass", "cat.jpg"],
        ["--iterations", "0", "cat.jpg"],
        ["--iterations", "1.5", "cat.jpg"],
        ["--loss-divisor", "0", "cat.jpg"],
        ["--loss-divisor", "half", "cat.jpg"],
        ["--minimum-depth", "deep", "cat.jpg"],
        ["--minimum-depth", "0", "cat.jpg"],
        ["--minimum-depth", "-2", "cat.jpg"],
        ["--resize", "1", "cat.jpg"],
        ["--resize", "big", "cat.jpg"],
        ["--artifact-size"],
        ["--unknown-option", "1", "cat.jpg"],
        ["--no-loss-images"],
        ["cat.jpg", "out"],
        ["cat.jpg", "out", "extra"],
        ["--height-scale", "2", "cat.jpg"],
    ];

    [TestCaseSource(nameof(OptionCases))]
    public void TryParse_ValuedOption_SetsThatOptionAndLeavesTheRestAtTheirDefaults(OptionCase optionCase)
    {
        // GIVEN an image plus a single valued option
        string[] args = ["cat.jpg", optionCase.Flag, optionCase.Value];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN the option takes the given value and nothing else moves
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(optionCase.Read(parsed!.Options), Is.EqualTo(optionCase.Expected));
            Assert.That(parsed.ImagePath, Is.EqualTo("cat.jpg"));
        });
    }

    [TestCaseSource(nameof(RejectedCases))]
    public void TryParse_BadArguments_FailsWithAMessage(string[] args)
    {
        // GIVEN arguments that cannot be honoured
        // WHEN they are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN parsing fails and says why
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(message, Is.Not.Empty);
        });
    }

    [Test]
    public void TryParse_NoArguments_ReturnsUsage()
    {
        // GIVEN no arguments
        // WHEN they are parsed
        bool parsedOk = CommandLine.TryParse([], out ParsedArguments? parsed, out string message);

        // THEN usage is reported rather than a parse
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(message, Is.EqualTo(CommandLine.Usage));
        });
    }

    [TestCase("-h")]
    [TestCase("--help")]
    public void TryParse_HelpFlag_ReturnsUsageEvenAlongsideOtherArguments(string flag)
    {
        // GIVEN a help flag mixed in with real arguments
        string[] args = ["cat.jpg", "--iterations", "3", flag];

        // WHEN they are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN usage wins
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(message, Is.EqualTo(CommandLine.Usage));
        });
    }

    [Test]
    public void TryParse_ImageOnly_MatchesTheEngineDefaults()
    {
        // GIVEN only an image
        // WHEN it is parsed
        bool parsedOk = CommandLine.TryParse(["cat.jpg"], out ParsedArguments? parsed, out string message);

        // THEN every option holds its engine default, output included
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.Options, Is.EqualTo(new CausticsOptions()));
            Assert.That(parsed.Options.OutputDirectory, Is.EqualTo("."));
        });
    }

    [TestCase("none")]
    [TestCase("NONE")]
    public void TryParse_ResizeOfNone_LeavesTheImageAtItsOwnSize(string value)
    {
        // GIVEN the opt-out form of resize
        string[] args = ["cat.jpg", "--resize", value];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN no resize is requested, which is also the default
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.Options.ResizeTo, Is.Null);
            Assert.That(new CausticsOptions().ResizeTo, Is.Null);
        });
    }

    [Test]
    public void TryParse_LossDivisorOfPixels_LeavesTheDivisorForTheEngineToSize()
    {
        // GIVEN the pixel-count form of the loss divisor
        string[] args = ["cat.jpg", "--loss-divisor", "pixels"];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN the divisor is left unset, which the engine reads as "use the image's pixel count"
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.Options.LossNormalisationDivisor, Is.Null);
        });
    }

    [Test]
    public void TryParse_BooleanFlags_FlipTheirOptions()
    {
        // GIVEN both boolean flags
        string[] args = ["cat.jpg", "--no-loss-images", "--save-obj"];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN loss diagnostics are off and OBJ output is on
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.Options.SaveLossImages, Is.False);
            Assert.That(parsed.Options.AlsoSaveObj, Is.True);
        });
    }

    [Test]
    public void TryParse_OutputFlag_SetsTheOutputDirectory()
    {
        // GIVEN an image and an output directory
        string[] args = ["cat.jpg", "--output", "lens-output"];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN that directory is the output directory
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.Options.OutputDirectory, Is.EqualTo("lens-output"));
        });
    }

    [Test]
    public void TryParse_SecondPositionalValue_IsRejectedRatherThanTakenAsTheOutputDirectory()
    {
        // GIVEN a second bare value, which earlier versions read as the output directory
        string[] args = ["cat.jpg", "lens-output"];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN it is refused, pointing at the flag, rather than silently writing somewhere else
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(message, Does.Contain("--output"));
        });
    }

    [Test]
    public void TryParse_OptionsBeforeTheImage_AreStillRecognised()
    {
        // GIVEN options given ahead of the positional image
        string[] args = ["--iterations", "2", "--save-obj", "cat.jpg"];

        // WHEN the arguments are parsed
        bool parsedOk = CommandLine.TryParse(args, out ParsedArguments? parsed, out string message);

        // THEN order does not matter
        Assert.Multiple(() =>
        {
            Assert.That(parsedOk, Is.True, message);
            Assert.That(parsed!.ImagePath, Is.EqualTo("cat.jpg"));
            Assert.That(parsed.Options.Iterations, Is.EqualTo(2));
            Assert.That(parsed.Options.AlsoSaveObj, Is.True);
        });
    }

    [Test]
    public void TryParse_EveryOption_IsCoveredByTheValuedOptionCases()
    {
        // GIVEN the CausticsOptions properties that take a value on the command line
        string[] valued =
        [
            nameof(CausticsOptions.ArtifactSizeMm),
            nameof(CausticsOptions.FocalLengthMm),
            nameof(CausticsOptions.RefractiveIndex),
            nameof(CausticsOptions.Iterations),
            nameof(CausticsOptions.ResizeTo),
            nameof(CausticsOptions.LossNormalisationDivisor),
            nameof(CausticsOptions.MinimumDepthMm),
        ];

        // WHEN the full property set is compared against the valued ones plus those covered elsewhere
        string[] coveredElsewhere =
        [
            nameof(CausticsOptions.OutputDirectory),
            nameof(CausticsOptions.SaveLossImages),
            nameof(CausticsOptions.AlsoSaveObj),
        ];

        string[] all = typeof(CausticsOptions)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // THEN no option is missing a CLI flag — a new option added to CausticsOptions fails here
        // until it is wired up and covered
        Assert.That(all, Is.EquivalentTo(valued.Concat(coveredElsewhere)));
    }

    public sealed record OptionCase(string Flag, string Value, Func<CausticsOptions, object?> Read, object Expected)
    {
        public override string ToString() => $"{Flag} {Value}";
    }
}
