using CausticsEngineering.Geometry;
using CausticsEngineering.Solver;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class MeshMarcherTests
{
    [Test]
    public void FindT_PointsCollapsingTowardsEachOther_ReturnsTheTimeAreaHitsZero()
    {
        // GIVEN a unit right triangle whose second vertex slides towards the first at unit speed
        Point3D p1 = new(0, 0, 0, 1, 1);
        Point3D p2 = new(1, 0, 0, 2, 1);
        Point3D p3 = new(0, 1, 0, 1, 2);

        Point3D stationary = new(0, 0, 0, 0, 0);
        Point3D closing = new(-1, 0, 0, 0, 0);

        // WHEN the collapse times are found
        (double t1, double t2) = MeshMarcher.FindT(p1, p2, p3, stationary, closing, stationary);

        // THEN the triangle degenerates after one unit of time
        Assert.Multiple(() =>
        {
            Assert.That(t1, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(t2, Is.EqualTo(1.0).Within(1e-9));
        });
    }

    [Test]
    public void FindT_NoRealRoot_ReturnsTheNoSolutionSentinel()
    {
        // GIVEN a triangle whose vertices rotate rather than collapse
        Point3D p1 = new(0, 0, 0, 0, 0);
        Point3D p2 = new(1, 0, 0, 0, 0);
        Point3D p3 = new(0, 1, 0, 0, 0);

        Point3D v1 = new(0, 0, 0, 0, 0);
        Point3D v2 = new(0, 1, 0, 0, 0);
        Point3D v3 = new(-1, 0, 0, 0, 0);

        // WHEN the collapse times are found
        (double t1, double t2) = MeshMarcher.FindT(p1, p2, p3, v1, v2, v3);

        // THEN both roots report no solution
        Assert.Multiple(() =>
        {
            Assert.That(t1, Is.EqualTo(MeshMarcher.NoSolution));
            Assert.That(t2, Is.EqualTo(MeshMarcher.NoSolution));
        });
    }

    [Test]
    public void March_FlatPotential_LeavesTheMeshWhereItWas()
    {
        // GIVEN a mesh and a potential with no gradient anywhere
        Mesh mesh = MeshBuilder.Square(5, 5);
        double[,] phi = new double[4, 4];
        double[] originalX = mesh.Nodes.Select(n => n.X).ToArray();

        // WHEN the mesh marches
        MeshMarcher.March(mesh, phi);

        // THEN no node has moved
        Assert.That(mesh.Nodes.Select(n => n.X), Is.EqualTo(originalX));
    }

    [Test]
    public void March_SlopedPotential_ShiftsTheMeshDownTheGradient()
    {
        // GIVEN a potential that ramps in x, so the velocity field points in -x everywhere
        Mesh mesh = MeshBuilder.Square(5, 5);
        double[,] phi = new double[4, 4];
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                phi[x, y] = x;
            }
        }

        // WHEN the mesh marches
        MeshMarcher.March(mesh, phi);

        // THEN interior nodes have moved in -x, and the trailing column stays put
        // because it carries zero velocity by construction
        Assert.Multiple(() =>
        {
            Assert.That(mesh.NodeArray[0, 0].X, Is.LessThan(1));
            Assert.That(mesh.NodeArray[4, 0].X, Is.EqualTo(5));
        });
    }

    [Test]
    public void March_AnyPotential_NeverInvertsATriangle()
    {
        // GIVEN a mesh and a potential with a sharp local well
        Mesh mesh = MeshBuilder.Square(9, 9);
        double[,] phi = new double[8, 8];
        phi[4, 4] = -50;

        // WHEN the mesh marches
        MeshMarcher.March(mesh, phi);

        // THEN every triangle still has positive area, because the step is half the
        // smallest positive collapse time
        Assert.That(
            Enumerable.Range(0, mesh.Triangles.Length).Select(mesh.TriangleArea),
            Has.All.GreaterThan(0));
    }
}
