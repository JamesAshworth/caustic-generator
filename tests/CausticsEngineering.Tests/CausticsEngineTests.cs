using System.Globalization;
using System.Text.RegularExpressions;
using CausticsEngineering.Geometry;
using CausticsEngineering.Solver;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class CausticsEngineTests
{
    private const int Size = 24;

    private string _outputDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _outputDirectory = Path.Combine(Path.GetTempPath(), $"caustics-tests-{Guid.NewGuid()}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Test]
    public void EngineerCaustics_HalfBrightHalfDarkImage_DrivesCellAreaTowardsBrightness()
    {
        // GIVEN an image that is bright on the left and dark on the right
        double[,] image = new double[Size, Size];
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                image[x, y] = x < Size / 2 ? 0.9 : 0.1;
            }
        }

        // WHEN caustics are engineered
        CausticsResult result = Engine().EngineerCaustics(image);
        double[,] areas = ScalarField.PixelArea(result.Mesh);

        // THEN cell area tracks target brightness: cells over the bright half are larger
        Assert.That(areas[Size / 4, Size / 2], Is.GreaterThan(areas[3 * Size / 4, Size / 2]));
    }

    [Test]
    public void EngineerCaustics_AnyImage_WritesTheSolidLensStl()
    {
        // GIVEN a uniform grey image
        double[,] image = Uniform();

        // WHEN caustics are engineered
        Engine().EngineerCaustics(image);

        // THEN the solid lens mesh is written to the output directory
        Assert.That(File.Exists(Path.Combine(_outputDirectory, "original_image.stl")), Is.True);
    }

    [Test]
    public void EngineerCaustics_StepRequested_WritesAClosedBrepSolid()
    {
        // GIVEN STEP output requested
        CausticsOptions options = Options() with { Formats = OutputFormats.Step };

        // WHEN caustics are engineered
        new CausticsEngine(options).EngineerCaustics(Uniform());
        string step = File.ReadAllText(Path.Combine(_outputDirectory, "original_image.stp"));

        // THEN the STEP file is a solid whose lens surface is one B-spline patch, written from the
        // open surface rather than from the solidified triangle mesh
        Assert.Multiple(() =>
        {
            Assert.That(step, Does.Contain("MANIFOLD_SOLID_BREP"));
            Assert.That(step, Does.Contain("B_SPLINE_SURFACE_WITH_KNOTS"));
        });
    }

    [TestCaseSource(nameof(FormatCases))]
    public void EngineerCaustics_Formats_WritesExactlyThoseFiles(OutputFormats formats, string[] expected)
    {
        // GIVEN a specific set of formats
        CausticsOptions options = Options() with { Formats = formats };

        // WHEN caustics are engineered
        new CausticsEngine(options).EngineerCaustics(Uniform());

        // THEN only those files are written: asking for one format does not drag the others along
        Assert.That(
            Directory.GetFiles(_outputDirectory).Select(Path.GetFileName),
            Is.EquivalentTo(expected));
    }

    [Test]
    public void EngineerCaustics_NoFormats_SolvesWithoutWritingAMesh()
    {
        // GIVEN no output format, which the CLI never produces but an API caller may want
        CausticsOptions options = Options() with { Formats = OutputFormats.None };

        // WHEN caustics are engineered
        CausticsResult result = new CausticsEngine(options).EngineerCaustics(Uniform());

        // THEN the solve still returns its result, and nothing is written
        Assert.Multiple(() =>
        {
            Assert.That(result.Heights, Is.Not.Null);
            Assert.That(Directory.GetFiles(_outputDirectory), Is.Empty);
        });
    }

    [Test]
    public void EngineerCaustics_WithStepRequested_PutsTheStepSolidInTheSameBoxAsTheStl()
    {
        // GIVEN an artifact size and depth to check against
        CausticsOptions options = Options() with
        {
            Formats = OutputFormats.Step,
            ArtifactSizeMm = 60,
            MinimumDepthMm = 3,
        };

        // WHEN caustics are engineered
        new CausticsEngine(options).EngineerCaustics(Uniform());
        string step = File.ReadAllText(Path.Combine(_outputDirectory, "original_image.stp"));

        double[] zs = StepVertexCoordinates(step, component: 2);
        double[] xs = StepVertexCoordinates(step, component: 0);

        // THEN the STEP solid carries the same artifact size and back-face depth the STL does,
        // so the two formats describe the same lens
        Assert.Multiple(() =>
        {
            Assert.That(xs.Max(), Is.EqualTo(60).Within(0.01));
            Assert.That(zs.Min(), Is.EqualTo(-3).Within(1e-9));
        });
    }

    [Test]
    public void EngineerCaustics_AnyImage_NormalisesTheTargetToTheMeshArea()
    {
        // GIVEN a dim image whose total brightness is far below the mesh area
        double[,] image = new double[Size, Size];
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                image[x, y] = 0.05;
            }
        }

        // WHEN caustics are engineered
        CausticsResult result = Engine().EngineerCaustics(image);

        // THEN the normalised target carries the same total energy as the undistorted mesh
        Assert.That(
            ScalarField.Sum(result.NormalisedImage),
            Is.EqualTo((double)Size * Size).Within(1e-6));
    }

    [Test]
    public void EngineerCaustics_AnyImage_ProducesAMeshOneNodeLargerThanTheImage()
    {
        // GIVEN a uniform image
        double[,] image = Uniform();

        // WHEN caustics are engineered
        CausticsResult result = Engine().EngineerCaustics(image);

        // THEN the mesh carries the extra closing row and column
        Assert.Multiple(() =>
        {
            Assert.That(result.Mesh.Width, Is.EqualTo(Size + 1));
            Assert.That(result.Mesh.Height, Is.EqualTo(Size + 1));
            Assert.That(result.Heights.GetLength(0), Is.EqualTo(Size));
        });
    }

    [Test]
    public void EngineerCaustics_AnyImage_KeepsEveryTriangleNonDegenerate()
    {
        // GIVEN an image with a bright spot
        double[,] image = Uniform();
        image[Size / 2, Size / 2] = 1.0;

        // WHEN caustics are engineered
        CausticsResult result = Engine().EngineerCaustics(image);

        // THEN no triangle has collapsed or inverted over the iterations
        Assert.That(
            Enumerable.Range(0, result.Mesh.Triangles.Length).Select(result.Mesh.TriangleArea),
            Has.All.GreaterThan(0));
    }

    [Test]
    public void SetHeightsInMillimetres_HeightField_RepeatsTheTrailingRowAndColumn()
    {
        // GIVEN a mesh one node larger than its height field
        Mesh mesh = MeshBuilder.Square(4, 4);
        double[,] heights = Ramp(3);

        // WHEN the heights are applied
        SurfaceSolver.SetHeightsInMillimetres(mesh, heights, mmPerPixel: 0.5);

        // THEN the closing row and column repeat their neighbour, so the mesh stays watertight
        Assert.Multiple(() =>
        {
            Assert.That(mesh.NodeArray[3, 2].Z, Is.EqualTo(mesh.NodeArray[2, 2].Z));
            Assert.That(mesh.NodeArray[2, 3].Z, Is.EqualTo(mesh.NodeArray[2, 2].Z));
            Assert.That(mesh.NodeArray[3, 3].Z, Is.EqualTo(mesh.NodeArray[2, 2].Z));
        });
    }

    [Test]
    public void SetHeightsInMillimetres_HeightFieldWithNegativeValues_PutsTheLowestPointAtZero()
    {
        // GIVEN a height field straddling zero, as the Poisson solve produces
        Mesh mesh = MeshBuilder.Square(4, 4);
        double[,] heights = Ramp(3);
        heights[1, 1] = -12.5;

        // WHEN the heights are applied
        SurfaceSolver.SetHeightsInMillimetres(mesh, heights, mmPerPixel: 0.5);

        // THEN the surface is shifted so its lowest point sits at exactly zero
        Assert.Multiple(() =>
        {
            Assert.That(mesh.Nodes.Min(n => n.Z), Is.EqualTo(0));
            Assert.That(mesh.NodeArray[1, 1].Z, Is.EqualTo(0));
        });
    }

    [Test]
    public void SetHeightsInMillimetres_HeightField_ConvertsHeightsAndSpacingIntoMillimetres()
    {
        // GIVEN a height field whose lowest value is zero
        Mesh mesh = MeshBuilder.Square(4, 4);
        double[,] heights = Ramp(3);

        // WHEN the heights are applied at 0.5 mm per pixel
        SurfaceSolver.SetHeightsInMillimetres(mesh, heights, mmPerPixel: 0.5);

        // THEN heights carry the pixel pitch, and node spacing is the pitch, starting from the origin
        Assert.Multiple(() =>
        {
            Assert.That(mesh.NodeArray[2, 2].Z, Is.EqualTo(4 * 0.5));
            Assert.That(mesh.NodeArray[0, 0].X, Is.EqualTo(0));
            Assert.That(mesh.NodeArray[3, 0].X, Is.EqualTo(1.5));
            Assert.That(mesh.NodeArray[0, 3].Y, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void EngineerCaustics_AnyImage_PutsTheLensSurfaceOnZeroAndTheBackFaceAtMinimumDepth()
    {
        // GIVEN an image with a bright spot, so the solved surface has real relief
        double[,] image = Uniform();
        image[Size / 2, Size / 2] = 1.0;

        // WHEN caustics are engineered with a 3 mm minimum depth
        CausticsOptions options = Options() with { MinimumDepthMm = 3 };
        new CausticsEngine(options).EngineerCaustics(image);

        // THEN the saved solid runs from the back face at -3 up to a lens surface
        // whose lowest point is exactly zero
        StlBounds bounds = ReadStlBounds();
        Assert.Multiple(() =>
        {
            Assert.That(bounds.MinZ, Is.EqualTo(-3.0f));
            Assert.That(bounds.MaxZ, Is.GreaterThan(0));
        });
    }

    [Test]
    public void EngineerCaustics_SquareImage_SizesBothEdgesFromTheArtifactSize()
    {
        // GIVEN a uniform image and an artifact width that is not the reference 512-pixel case
        double[,] image = Uniform();

        // WHEN caustics are engineered at 60 mm wide
        CausticsOptions options = Options() with { ArtifactSizeMm = 60 };
        CausticsResult result = new CausticsEngine(options).EngineerCaustics(image);

        // THEN the scale comes from the image's own width, and the model spans that width
        StlBounds bounds = ReadStlBounds();
        Assert.Multiple(() =>
        {
            Assert.That(result.MmPerPixel, Is.EqualTo(60.0 / Size).Within(1e-12));
            Assert.That(bounds.MinX, Is.EqualTo(0.0f).Within(1e-4f));
            Assert.That(bounds.MaxX, Is.EqualTo(60.0f).Within(1e-3f));
        });
    }

    [TestCase(32, 16, TestName = "Landscape")]
    [TestCase(16, 32, TestName = "Portrait")]
    public void EngineerCaustics_RectangularImage_PutsTheArtifactSizeOnTheLongerAxisAndKeepsAspect(
        int imageWidth,
        int imageHeight)
    {
        // GIVEN a 2:1 image, in either orientation
        double[,] image = new double[imageWidth, imageHeight];
        for (int x = 0; x < imageWidth; x++)
        {
            for (int y = 0; y < imageHeight; y++)
            {
                image[x, y] = 0.5;
            }
        }

        // WHEN caustics are engineered at 60 mm
        CausticsOptions options = Options() with { ArtifactSizeMm = 60 };
        CausticsResult result = new CausticsEngine(options).EngineerCaustics(image);

        // THEN the longer axis is the artifact size, the shorter is half it, and the scale
        // comes off the longer axis whichever one it is
        StlBounds bounds = ReadStlBounds();
        Assert.Multiple(() =>
        {
            Assert.That(result.MmPerPixel, Is.EqualTo(60.0 / Math.Max(imageWidth, imageHeight)).Within(1e-12));
            Assert.That(bounds.MaxX - bounds.MinX, Is.EqualTo(imageWidth * 60.0f / 32).Within(1e-3f));
            Assert.That(bounds.MaxY - bounds.MinY, Is.EqualTo(imageHeight * 60.0f / 32).Within(1e-3f));
        });
    }

    private static double[,] Ramp(int size)
    {
        double[,] heights = new double[size, size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                heights[x, y] = x + y;
            }
        }

        return heights;
    }

    /// Reads the saved STL back so the assertions see what a slicer would, not just in-memory state.
    private StlBounds ReadStlBounds()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(_outputDirectory, "original_image.stl"));
        uint facets = BitConverter.ToUInt32(bytes, 80);

        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (uint facet = 0; facet < facets; facet++)
        {
            for (int vertex = 0; vertex < 3; vertex++)
            {
                int offset = 84 + ((int)facet * 50) + 12 + (vertex * 12);
                float x = BitConverter.ToSingle(bytes, offset);
                float y = BitConverter.ToSingle(bytes, offset + 4);
                float z = BitConverter.ToSingle(bytes, offset + 8);

                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return new StlBounds(minX, maxX, minY, maxY, minZ, maxZ);
    }

    private readonly record struct StlBounds(
        float MinX,
        float MaxX,
        float MinY,
        float MaxY,
        float MinZ,
        float MaxZ);

    private static double[,] Uniform()
    {
        double[,] image = new double[Size, Size];
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                image[x, y] = 0.5;
            }
        }

        return image;
    }

    /// The corner vertices of the STEP solid, by axis. Control points are excluded: a cubic
    /// control net can sit outside the data it interpolates, so it does not bound the solid.
    private static double[] StepVertexCoordinates(string step, int component)
    {
        Dictionary<string, string[]> points = Regex
            .Matches(step, @"#(\d+)=CARTESIAN_POINT\('',\(([^)]+)\)\)")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value.Split(','));

        return Regex
            .Matches(step, @"=VERTEX_POINT\('',#(\d+)\)")
            .Select(match => points[match.Groups[1].Value][component])
            .Select(value => double.Parse(value.TrimEnd('.'), CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static readonly IList<object[]> FormatCases =
    [
        [OutputFormats.Stl, new[] { "original_image.stl" }],
        [OutputFormats.Obj, new[] { "original_image.obj" }],
        [OutputFormats.Step, new[] { "original_image.stp" }],
        [OutputFormats.Stl | OutputFormats.Step, new[] { "original_image.stl", "original_image.stp" }],
        [
            OutputFormats.Stl | OutputFormats.Obj | OutputFormats.Step,
            new[] { "original_image.stl", "original_image.obj", "original_image.stp" }
        ],
    ];

    private CausticsEngine Engine() => new(Options());

    private CausticsOptions Options()
        => new()
        {
            OutputDirectory = _outputDirectory,
            SaveLossImages = false,
            Iterations = 3,
        };
}
