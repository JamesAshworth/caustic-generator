using CausticsEngineering.Geometry;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class BSplineSurfaceTests
{
    [TestCase(3, 6)]
    [TestCase(6, 3)]
    [TestCase(2, 2)]
    public void Interpolate_GridTooSmallForACubic_Throws(int width, int height)
    {
        // GIVEN a grid with fewer than four points along one direction
        Vec3[,] grid = Grid(width, height, (x, y) => x + y);

        // WHEN it is fitted
        // THEN the fit is refused rather than silently dropping degree
        Assert.That(() => BSplineSurface.Interpolate(grid), Throws.ArgumentException);
    }

    [Test]
    public void Interpolate_Grid_PassesThroughEveryDataPoint()
    {
        // GIVEN a grid with relief that no low-order surface could match by accident
        Vec3[,] grid = Grid(9, 7, (x, y) => Math.Sin(x * 0.7) * Math.Cos(y * 0.9));

        // WHEN it is fitted and evaluated back at each data point's parameters
        BSplineSurface surface = BSplineSurface.Interpolate(grid);

        List<double> errors = [];
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 7; y++)
            {
                Vec3 evaluated = surface.Evaluate(x / 8.0, y / 6.0);
                errors.Add(Math.Abs(evaluated.X - grid[x, y].X));
                errors.Add(Math.Abs(evaluated.Y - grid[x, y].Y));
                errors.Add(Math.Abs(evaluated.Z - grid[x, y].Z));
            }
        }

        // THEN it interpolates rather than approximates
        Assert.That(errors, Has.All.LessThan(1e-9));
    }

    [Test]
    public void Interpolate_Grid_ProducesOneControlPointPerDataPoint()
    {
        // GIVEN a grid
        Vec3[,] grid = Grid(9, 7, (x, y) => x * y);

        // WHEN it is fitted
        BSplineSurface surface = BSplineSurface.Interpolate(grid);

        // THEN a square collocation system has been solved, so the counts match
        Assert.Multiple(() =>
        {
            Assert.That(surface.UCount, Is.EqualTo(9));
            Assert.That(surface.VCount, Is.EqualTo(7));
            Assert.That(surface.UKnots, Has.Length.EqualTo(9 + BSplineSurface.Degree + 1));
            Assert.That(surface.VKnots, Has.Length.EqualTo(7 + BSplineSurface.Degree + 1));
        });
    }

    [Test]
    public void Interpolate_Grid_InterpolatesItsCornerControlPointsExactly()
    {
        // GIVEN a grid whose corners are all distinct
        Vec3[,] grid = Grid(9, 7, (x, y) => (x * 3) - (y * 2));

        // WHEN it is fitted
        BSplineSurface surface = BSplineSurface.Interpolate(grid);

        // THEN the clamped fit leaves the corner control points sitting on the data, which is what
        // lets StepWriter use them directly as the solid's vertices
        Assert.Multiple(() =>
        {
            Assert.That(surface.ControlPoints[0, 0], Is.EqualTo(grid[0, 0]));
            Assert.That(surface.ControlPoints[8, 0], Is.EqualTo(grid[8, 0]));
            Assert.That(surface.ControlPoints[8, 6], Is.EqualTo(grid[8, 6]));
            Assert.That(surface.ControlPoints[0, 6], Is.EqualTo(grid[0, 6]));
        });
    }

    [Test]
    public void Interpolate_GridWithAConstantBoundaryCoordinate_ReproducesThatConstantToRoundOff()
    {
        // GIVEN a grid whose x = 0 boundary column is pinned to a non-zero constant
        const double pinned = 5.0;
        Vec3[,] grid = Grid(9, 7, (x, y) => Math.Sin(x + y));
        for (int y = 0; y < 7; y++)
        {
            grid[0, y] = grid[0, y] with { X = pinned };
        }

        // WHEN it is fitted
        BSplineSurface surface = BSplineSurface.Interpolate(grid);

        // THEN the boundary control row holds the constant, but only as closely as the collocation
        // solve manages — which is why StepWriter pins its skirt planes rather than trusting this
        Assert.That(
            surface.UBoundaryControlPoints(atEnd: false).Select(p => p.X),
            Has.All.EqualTo(pinned).Within(1e-9));
    }

    [Test]
    public void UBoundaryControlPoints_Surface_MatchTheControlNetEdges()
    {
        // GIVEN a fitted surface
        Vec3[,] grid = Grid(9, 7, (x, y) => x - y);
        BSplineSurface surface = BSplineSurface.Interpolate(grid);

        // WHEN the boundary rows are read
        // THEN they are the edges of the control net, so a boundary curve built from them is the
        // surface's own edge rather than a copy of it
        Assert.Multiple(() =>
        {
            Assert.That(surface.UBoundaryControlPoints(atEnd: true)[3], Is.EqualTo(surface.ControlPoints[8, 3]));
            Assert.That(surface.VBoundaryControlPoints(atEnd: true)[3], Is.EqualTo(surface.ControlPoints[3, 6]));
        });
    }

    private static Vec3[,] Grid(int width, int height, Func<int, int, double> heightAt)
    {
        Vec3[,] grid = new Vec3[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Vec3(x, y, heightAt(x, y));
            }
        }

        return grid;
    }
}
