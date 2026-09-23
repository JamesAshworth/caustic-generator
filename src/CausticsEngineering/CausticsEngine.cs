using CausticsEngineering.Geometry;
using CausticsEngineering.Io;
using CausticsEngineering.Solver;

namespace CausticsEngineering;

public sealed record CausticsOptions
{
    public double ArtifactSizeMeters { get; init; } = 0.1;

    public double FocalLengthMeters { get; init; } = 0.2;

    public int Iterations { get; init; } = 4;

    public double HeightScale { get; init; } = 1.0;

    public double HeightOffset { get; init; } = 10;

    public double SolidifyOffset { get; init; } = 100;

    /// Divisor used to zero-centre the loss field. The reference implementation hardcodes 512 * 512
    /// regardless of image size, so images of other sizes get a differently scaled offset there.
    /// Null reproduces that behaviour; set it to use the actual pixel count instead.
    public int? LossNormalisationDivisor { get; init; }

    public string OutputDirectory { get; init; } = ".";

    public bool SaveLossImages { get; init; } = true;

    /// STL is always written. Set this to additionally write OBJ, which preserves vertex sharing
    /// and grid dimensions and so can be loaded back into a Mesh via ObjWriter.Load.
    public bool AlsoSaveObj { get; init; }
}

public sealed record CausticsResult(Mesh Mesh, double[,] NormalisedImage, double[,] Heights, double MetersPerPixel);

public sealed class CausticsEngine(CausticsOptions? options = null, Action<string>? log = null)
{
    private const int ReferenceLossDivisor = 512 * 512;

    private readonly CausticsOptions _options = options ?? new CausticsOptions();

    /// Solves for the lens that focuses light into the given greyscale image and writes the
    /// solid mesh to `original_image.stl` in the output directory.
    public CausticsResult EngineerCaustics(double[,] greyscaleImage)
    {
        ArgumentNullException.ThrowIfNull(greyscaleImage);

        int width = greyscaleImage.GetLength(0);
        int height = greyscaleImage.GetLength(1);

        // The mesh carries one extra row and column so every pixel quad can be closed
        Mesh mesh = MeshBuilder.Square(width + 1, height + 1);

        // Boost the brightness so the image's total energy equals the mesh's total area.
        // Without this the solver would be chasing a target it cannot reach.
        double meshSum = (double)width * height;
        double imageSum = ScalarField.Sum(greyscaleImage);
        double boostRatio = meshSum / imageSum;

        double[,] target = new double[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                target[x, y] = greyscaleImage[x, y] * boostRatio;
            }
        }

        for (int iteration = 1; iteration <= _options.Iterations; iteration++)
        {
            RunIteration(mesh, target, $"it{iteration}");
        }

        (double[,] heights, double metersPerPixel) = SurfaceSolver.FindSurface(
            mesh,
            target,
            _options.FocalLengthMeters,
            _options.ArtifactSizeMeters,
            log);

        SurfaceSolver.SetHeights(mesh, heights, _options.HeightScale, _options.HeightOffset);

        Mesh solidMesh = MeshBuilder.Solidify(mesh, _options.SolidifyOffset);

        Directory.CreateDirectory(_options.OutputDirectory);
        double meshScale = 1 / 512.0 * _options.ArtifactSizeMeters;

        StlWriter.Save(
            solidMesh,
            Path.Combine(_options.OutputDirectory, "original_image.stl"),
            scale: meshScale,
            scaleZ: meshScale);

        if (_options.AlsoSaveObj)
        {
            ObjWriter.Save(
                solidMesh,
                Path.Combine(_options.OutputDirectory, "original_image.obj"),
                scale: meshScale,
                scaleZ: meshScale);
        }

        return new CausticsResult(mesh, target, heights, metersPerPixel);
    }

    /// One outer step: measure how much light each mesh quad currently delivers, solve the
    /// Poisson equation for the potential that corrects the shortfall, then march the mesh down
    /// that potential's gradient.
    public void RunIteration(Mesh mesh, double[,] target, string suffix)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(target);

        double[,] luminance = ScalarField.PixelArea(mesh);

        int width = target.GetLength(0);
        int height = target.GetLength(1);

        double[,] loss = new double[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                loss[x, y] = luminance[x, y] - target[x, y];
            }
        }

        ScalarField.SubtractMean(loss, _options.LossNormalisationDivisor ?? ReferenceLossDivisor);

        log?.Invoke($"Loss {suffix}: min {ScalarField.Min(loss)}, max {ScalarField.Max(loss)}, sum {ScalarField.Sum(loss)}");

        if (_options.SaveLossImages)
        {
            Directory.CreateDirectory(_options.OutputDirectory);
            ImageIo.SaveSignedField(loss, Path.Combine(_options.OutputDirectory, $"loss_{suffix}.png"));
        }

        double[,] phi = new double[width, height];

        log?.Invoke("Building phi");
        RelaxationResult result = ScalarField.RelaxToConvergence(phi, loss, log: log);
        if (double.IsNaN(result.MaxUpdate))
        {
            throw new InvalidOperationException(
                $"Relaxation diverged to NaN at step {result.Iterations} while building phi for {suffix}.");
        }

        MeshMarcher.March(mesh, phi, log);
    }
}
