using CausticsEngineering.Geometry;
using CausticsEngineering.Io;
using CausticsEngineering.Solver;

namespace CausticsEngineering;

/// All lengths are millimetres. The pixel-to-millimetre scale is derived from the image's own
/// longest edge, so no dimension is tied to a particular image size or aspect ratio.
public sealed record CausticsOptions
{
    /// Longest edge of the printed lens. The other edge follows from the image's aspect ratio.
    public double ArtifactSizeMm { get; init; } = 100;

    public double FocalLengthMm { get; init; } = 200;

    public int Iterations { get; init; } = 4;

    /// Refractive index of the lens material, used in Snell's law when turning ray displacements
    /// into surface normals. Default 1.49 is cast acrylic (PMMA); clear epoxy resins run nearer
    /// 1.565, glass around 1.52. Must be greater than 1.
    public double RefractiveIndex { get; init; } = 1.49;

    /// Material thickness at the thinnest point of the lens. The solved surface is shifted so its
    /// lowest point sits at z = 0 and the flat back face is placed this far below it.
    public double MinimumDepthMm { get; init; } = 10;

    /// Divisor used to zero-centre the loss field. Null uses the image's pixel count, which is
    /// what the maths wants. The reference implementation hardcodes 262144 (512 * 512) whatever the
    /// image size, so pass that explicitly for bit-for-bit parity with it on other sizes.
    public int? LossNormalisationDivisor { get; init; }

    /// Defaults to the working directory.
    public string OutputDirectory { get; init; } = ".";

    public bool SaveLossImages { get; init; } = true;

    /// STL is always written. Set this to additionally write OBJ, which preserves vertex sharing
    /// and grid dimensions and so can be loaded back into a Mesh via ObjWriter.Load.
    public bool AlsoSaveObj { get; init; }

    /// Longest edge the input image is resized to before solving, preserving aspect ratio.
    /// Both Poisson solves cost O(pixels) per sweep over thousands of sweeps, so this caps the
    /// work on a large input. Null, the default, solves at the image's own size.
    public int? ResizeTo { get; init; }
}

public sealed record CausticsResult(Mesh Mesh, double[,] NormalisedImage, double[,] Heights, double MmPerPixel);

public sealed class CausticsEngine(CausticsOptions? options = null, Action<string>? log = null)
{
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

        // The solver works in metres; the public surface is millimetres
        (double[,] heights, double metersPerPixel) = SurfaceSolver.FindSurface(
            mesh,
            target,
            _options.FocalLengthMm / 1000.0,
            _options.ArtifactSizeMm / 1000.0,
            _options.RefractiveIndex,
            log);

        double mmPerPixel = metersPerPixel * 1000.0;

        SurfaceSolver.SetHeightsInMillimetres(mesh, heights, mmPerPixel);

        // The surface now bottoms out at z = 0, so the back face goes one minimum depth below it.
        // STL and OBJ are unitless, and slicers read both as millimetres.
        Mesh solidMesh = MeshBuilder.Solidify(mesh, -_options.MinimumDepthMm, mmPerPixel);

        Directory.CreateDirectory(_options.OutputDirectory);

        StlWriter.Save(solidMesh, Path.Combine(_options.OutputDirectory, "original_image.stl"));

        if (_options.AlsoSaveObj)
        {
            ObjWriter.Save(solidMesh, Path.Combine(_options.OutputDirectory, "original_image.obj"));
        }

        return new CausticsResult(mesh, target, heights, mmPerPixel);
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

        ScalarField.SubtractMean(loss, _options.LossNormalisationDivisor ?? (width * height));

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
