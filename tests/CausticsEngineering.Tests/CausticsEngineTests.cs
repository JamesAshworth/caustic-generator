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
    public void SetHeights_HeightField_RepeatsTheTrailingRowAndColumn()
    {
        // GIVEN a mesh one node larger than its height field
        Mesh mesh = MeshBuilder.Square(4, 4);
        double[,] heights = new double[3, 3];
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                heights[x, y] = x + y;
            }
        }

        // WHEN the heights are applied with a scale and offset
        SurfaceSolver.SetHeights(mesh, heights, heightScale: 2.0, heightOffset: 1.0);

        // THEN interior nodes are scaled and offset, and the closing row and column
        // repeat their neighbour so the mesh stays watertight
        Assert.Multiple(() =>
        {
            Assert.That(mesh.NodeArray[2, 2].Z, Is.EqualTo(4 * 2.0 + 1.0));
            Assert.That(mesh.NodeArray[3, 2].Z, Is.EqualTo(mesh.NodeArray[2, 2].Z));
            Assert.That(mesh.NodeArray[2, 3].Z, Is.EqualTo(mesh.NodeArray[2, 2].Z));
        });
    }

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

    private CausticsEngine Engine()
        => new(new CausticsOptions
        {
            OutputDirectory = _outputDirectory,
            SaveLossImages = false,
            Iterations = 3,
            LossNormalisationDivisor = Size * Size,
        });
}
