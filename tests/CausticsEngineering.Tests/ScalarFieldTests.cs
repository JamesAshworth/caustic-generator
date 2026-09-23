using CausticsEngineering.Geometry;
using CausticsEngineering.Solver;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class ScalarFieldTests
{
    [Test]
    public void Gradient_LinearRamp_IsConstantExceptOnTheTrailingEdge()
    {
        // GIVEN a field that ramps by 2 per step in x and 3 per step in y
        double[,] field = new double[3, 3];
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                field[x, y] = 2 * x + 3 * y;
            }
        }

        // WHEN the gradient is taken
        (double[,] du, double[,] dv) = ScalarField.Gradient(field);

        // THEN interior differences are the ramp slopes and the trailing edges are zero
        Assert.Multiple(() =>
        {
            Assert.That(du[0, 0], Is.EqualTo(2));
            Assert.That(dv[0, 0], Is.EqualTo(3));
            Assert.That(du[2, 1], Is.EqualTo(0));
            Assert.That(dv[1, 2], Is.EqualTo(0));
        });
    }

    [Test]
    public void PixelArea_UndistortedMesh_IsOnePerCell()
    {
        // GIVEN an undistorted 5x5 mesh
        Mesh mesh = MeshBuilder.Square(5, 5);

        // WHEN pixel areas are computed
        double[,] areas = ScalarField.PixelArea(mesh);

        // THEN every cell covers one square unit
        Assert.Multiple(() =>
        {
            Assert.That(areas.GetLength(0), Is.EqualTo(4));
            Assert.That(areas.Cast<double>(), Has.All.EqualTo(1.0).Within(1e-9));
        });
    }

    [Test]
    public void PixelArea_StretchedColumn_GrowsThatCellsArea()
    {
        // GIVEN a mesh whose second column has been pushed one unit further right
        Mesh mesh = MeshBuilder.Square(3, 3);
        for (int y = 0; y < 3; y++)
        {
            mesh.NodeArray[1, y].X += 1;
        }

        // WHEN pixel areas are computed
        double[,] areas = ScalarField.PixelArea(mesh);

        // THEN the widened cell is twice the area and its neighbour is empty
        Assert.Multiple(() =>
        {
            Assert.That(areas[0, 0], Is.EqualTo(2.0).Within(1e-9));
            Assert.That(areas[1, 0], Is.EqualTo(0.0).Within(1e-9));
        });
    }

    [Test]
    public void Relax_ZeroSourceTerm_LeavesAFlatFieldUntouched()
    {
        // GIVEN a flat field and no source term
        double[,] matrix = new double[4, 4];
        double[,] source = new double[4, 4];

        // WHEN one relaxation sweep runs
        double maxUpdate = ScalarField.Relax(matrix, source);

        // THEN nothing moves
        Assert.Multiple(() =>
        {
            Assert.That(maxUpdate, Is.EqualTo(0));
            Assert.That(matrix.Cast<double>(), Has.All.EqualTo(0));
        });
    }

    [Test]
    public void RelaxToConvergence_ZeroMeanSource_SolvesThePoissonEquation()
    {
        // GIVEN a zero-mean source term
        double[,] source = new double[8, 8];
        source[2, 3] = 1;
        source[5, 6] = -1;

        double[,] solution = new double[8, 8];

        // WHEN relaxation runs to convergence
        RelaxationResult result = ScalarField.RelaxToConvergence(solution, source);

        // THEN it converges and the discrete Laplacian of the solution reproduces the source
        // under the Neumann boundary conditions the relaxation assumes
        Assert.Multiple(() =>
        {
            Assert.That(result.Converged, Is.True);
            Assert.That(Laplacian(solution, 2, 3), Is.EqualTo(1.0).Within(1e-3));
            Assert.That(Laplacian(solution, 5, 6), Is.EqualTo(-1.0).Within(1e-3));
            Assert.That(Laplacian(solution, 4, 4), Is.EqualTo(0.0).Within(1e-3));
        });
    }

    [Test]
    public void RelaxToConvergence_NonZeroMeanSource_DoesNotConverge()
    {
        // GIVEN a source term whose sum is not zero, which has no Neumann solution
        double[,] source = new double[8, 8];
        source[4, 4] = 1;

        double[,] solution = new double[8, 8];

        // WHEN relaxation runs with a small iteration cap
        RelaxationResult result = ScalarField.RelaxToConvergence(solution, source, maxIterations: 200);

        // THEN it hits the cap rather than settling
        Assert.Multiple(() =>
        {
            Assert.That(result.Converged, Is.False);
            Assert.That(result.Iterations, Is.EqualTo(200));
        });
    }

    [Test]
    public void SubtractMean_Field_LeavesASumOfZero()
    {
        // GIVEN a field with a non-zero sum
        double[,] field = { { 1, 2 }, { 3, 4 } };

        // WHEN the mean is subtracted
        ScalarField.SubtractMean(field, 4);

        // THEN the field sums to zero
        Assert.That(ScalarField.Sum(field), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void MinMaxSum_Field_ReportTheExtremesAndTotal()
    {
        // GIVEN a field with a known spread
        double[,] field = { { -2, 5 }, { 1, 0 } };

        // WHEN the aggregates are taken
        // THEN they describe the field
        Assert.Multiple(() =>
        {
            Assert.That(ScalarField.Min(field), Is.EqualTo(-2));
            Assert.That(ScalarField.Max(field), Is.EqualTo(5));
            Assert.That(ScalarField.Sum(field), Is.EqualTo(4));
        });
    }

    private static double Laplacian(double[,] field, int x, int y)
        => field[x - 1, y] + field[x + 1, y] + field[x, y - 1] + field[x, y + 1] - 4 * field[x, y];
}
