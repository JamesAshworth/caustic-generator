using System.Globalization;
using System.Text.RegularExpressions;
using CausticsEngineering.Geometry;
using CausticsEngineering.Io;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class StepWriterTests
{
    private const double BottomZ = -4.0;

    /// A scale with no exact binary representation, as a real image's derived scale will be. A
    /// tidy value like 0.75 hides round-off that this fixture exists to expose.
    private const double MmPerPixel = 0.1931818181818182;

    private string _file = string.Empty;

    [SetUp]
    public void SetUp() => _file = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.stp");

    [TearDown]
    public void TearDown() => File.Delete(_file);

    [Test]
    public void Save_Surface_WritesAnAp214HeaderAndMillimetreUnits()
    {
        // GIVEN a lens surface
        // WHEN it is saved
        StepWriter.Save(Surface(9, 7), _file, BottomZ);
        string step = File.ReadAllText(_file);

        // THEN the file is a well-formed AP214 part declaring millimetres, so the recipient's
        // importer never has to guess the scale
        Assert.Multiple(() =>
        {
            Assert.That(step, Does.StartWith("ISO-10303-21;"));
            Assert.That(step.TrimEnd(), Does.EndWith("END-ISO-10303-21;"));
            Assert.That(step, Does.Contain("FILE_SCHEMA(('AUTOMOTIVE_DESIGN"));
            Assert.That(step, Does.Contain("SI_UNIT(.MILLI.,.METRE.)"));
            Assert.That(step, Does.Contain("MANIFOLD_SOLID_BREP"));
        });
    }

    [Test]
    public void Save_Surface_WritesSixFacesOnOneClosedShell()
    {
        // GIVEN a lens surface
        // WHEN it is saved
        StepWriter.Save(Surface(9, 7), _file, BottomZ);
        string step = File.ReadAllText(_file);

        // THEN the solid is the B-spline lens plus a back face and four skirt faces, not a facet
        // per triangle
        Assert.Multiple(() =>
        {
            Assert.That(Count(step, "ADVANCED_FACE"), Is.EqualTo(6));
            Assert.That(Count(step, "B_SPLINE_SURFACE_WITH_KNOTS"), Is.EqualTo(1));
            Assert.That(Count(step, "PLANE("), Is.EqualTo(5));
            Assert.That(Count(step, "CLOSED_SHELL"), Is.EqualTo(1));
            Assert.That(Count(step, "VERTEX_POINT"), Is.EqualTo(8));
            Assert.That(Count(step, "EDGE_CURVE"), Is.EqualTo(12));
        });
    }

    [Test]
    public void Save_Surface_UsesEveryEdgeExactlyOnceInEachDirection()
    {
        // GIVEN a saved lens
        StepWriter.Save(Surface(9, 7), _file, BottomZ);
        string step = File.ReadAllText(_file);

        // WHEN each edge's oriented uses are counted
        Dictionary<string, (int Forward, int Reversed)> uses = [];
        foreach (Match match in Regex.Matches(step, @"ORIENTED_EDGE\('',\*,\*,(#\d+),\.([TF])\.\)"))
        {
            (int forward, int reversed) = uses.GetValueOrDefault(match.Groups[1].Value);
            uses[match.Groups[1].Value] = match.Groups[2].Value == "T"
                ? (forward + 1, reversed)
                : (forward, reversed + 1);
        }

        // THEN all twelve edges are shared by exactly two faces with opposite orientation, which is
        // the topological condition for the shell to be closed and the winding to be consistent
        Assert.Multiple(() =>
        {
            Assert.That(uses, Has.Count.EqualTo(12));
            Assert.That(uses.Values, Has.All.EqualTo((1, 1)));
        });
    }

    [Test]
    public void Save_Surface_GivesTheSkirtFacesTheSurfaceOwnBoundaryCurves()
    {
        // GIVEN a saved lens
        StepWriter.Save(Surface(9, 7), _file, BottomZ);
        string step = File.ReadAllText(_file);

        // WHEN the boundary curves are counted
        // THEN there are exactly four, one per skirt, each shared with the top face rather than
        // duplicated — a duplicate would sew only to tolerance
        Assert.That(Count(step, "B_SPLINE_CURVE_WITH_KNOTS"), Is.EqualTo(4));
    }

    [Test]
    public void Save_MarchedSurface_LeavesEverySkirtBoundaryCurveExactlyInItsPlane()
    {
        // GIVEN a surface whose nodes have all marched, as the solver leaves them, at a size and
        // scale where the collocation solve is known to lose the boundary constant. Whether it
        // loses it is erratic and data-dependent — it reproduces a constant exactly more often than
        // not — so this fixture is a specific case measured to drift, not an arbitrary one.
        Mesh surface = Surface(40, 31, gridScale: MmPerPixel);
        foreach (Point3D node in surface.Nodes)
        {
            node.X += 0.01 * Math.Sin(node.Iy * 1.7);
            node.Y += 0.01 * Math.Cos(node.Ix * 1.3);
        }

        // WHEN it is saved
        StepWriter.Save(surface, _file, BottomZ, gridScale: MmPerPixel);

        // WHEN each boundary curve's control points are read back
        double[] spreads = BoundaryCurveControlPoints(_file)
            .Select(curve => Enumerable.Range(0, 3)
                .Min(axis => Spread(curve, axis)))
            .ToArray();

        // THEN all four are flat in one axis to the bit, so every skirt face is exactly planar and
        // the shell is watertight by construction rather than within a tolerance
        Assert.Multiple(() =>
        {
            Assert.That(spreads, Has.Length.EqualTo(4));
            Assert.That(spreads, Has.All.EqualTo(0.0));
        });
    }

    [Test]
    public void Save_MarchedSurface_PullsTheBoundaryOntoTheRectangleTheBackFaceUses()
    {
        // GIVEN a surface whose edge nodes have marched off the grid, as the solver leaves them
        Mesh surface = Surface(9, 7);
        surface.NodeArray[0, 3].X = -0.04;
        surface.NodeArray[8, 3].X = 8.03;
        surface.NodeArray[4, 0].Y = -0.02;
        surface.NodeArray[4, 6].Y = 6.05;

        // WHEN it is saved
        StepWriter.Save(surface, _file, BottomZ);
        Vec3[] vertices = Vertices(_file);

        // THEN the solid's corners land exactly on the rectangle rather than on the drifted nodes,
        // so all four skirt planes are exact and the drift cannot leak into the sewing tolerance
        Assert.Multiple(() =>
        {
            Assert.That(vertices.Select(v => v.X).Distinct(), Is.EquivalentTo(new[] { 0.0, 8.0 }));
            Assert.That(vertices.Select(v => v.Y).Distinct(), Is.EquivalentTo(new[] { 0.0, 6.0 }));
        });
    }

    [Test]
    public void Save_WithGridScale_WritesTheSameBoundingBoxAsTheSolidifiedMesh()
    {
        // GIVEN a surface already in millimetres, as SetHeightsInMillimetres leaves it, plus the
        // scale the engine hands Solidify for the back face
        const double gridScale = 0.25;
        Mesh surface = Surface(9, 7, gridScale);

        // WHEN it is saved as STEP
        StepWriter.Save(surface, _file, BottomZ, gridScale);
        Vec3[] vertices = Vertices(_file);

        // THEN the solid occupies the same box the STL would, back face included
        Assert.Multiple(() =>
        {
            Assert.That(vertices.Max(v => v.X), Is.EqualTo(8 * gridScale).Within(1e-9));
            Assert.That(vertices.Max(v => v.Y), Is.EqualTo(6 * gridScale).Within(1e-9));
            Assert.That(vertices.Min(v => v.Z), Is.EqualTo(BottomZ).Within(1e-9));
        });
    }

    [Test]
    public void Save_Surface_WritesEveryRealWithADecimalPoint()
    {
        // GIVEN a surface with a height small enough to be formatted in exponent notation
        Mesh surface = Surface(9, 7);
        surface.NodeArray[4, 3].Z = 1.5e-7;

        // WHEN it is saved
        StepWriter.Save(surface, _file, BottomZ);
        string step = File.ReadAllText(_file);

        // THEN no bare integer or dotless exponent slips into a coordinate list: STEP reals must
        // carry a decimal point, and a reader that enforces it would reject the file
        string[] offenders = Regex.Matches(step, @"\(([-\d.E+,]+)\)\)?;")
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Where(value => value.Length > 0 && !value.Contains('.', StringComparison.Ordinal))
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void Save_SameSurfaceTwice_IsReproducibleGivenATimestamp()
    {
        // GIVEN a fixed timestamp, so the only varying part of the header is pinned
        DateTimeOffset stamp = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        string second = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.stp");

        try
        {
            // WHEN the same surface is saved twice
            StepWriter.Save(Surface(9, 7), _file, BottomZ, timestamp: stamp);
            StepWriter.Save(Surface(9, 7), second, BottomZ, timestamp: stamp);

            // THEN the files are identical, so a re-export does not show up as a spurious change
            Assert.That(File.ReadAllText(_file), Is.EqualTo(File.ReadAllText(second)));
        }
        finally
        {
            File.Delete(second);
        }
    }

    [Test]
    public void Save_Surface_IsFarSmallerThanTheEquivalentStl()
    {
        // GIVEN a surface big enough for the comparison to mean something
        Mesh surface = Surface(40, 30);
        string stl = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.stl");

        try
        {
            // WHEN the same lens is written both ways
            StepWriter.Save(surface, _file, BottomZ);
            StlWriter.Save(MeshBuilder.Solidify(surface, BottomZ), stl);

            // THEN STEP carries one control point per node instead of a standalone facet per
            // triangle, which is the whole reason to prefer it for CAD
            Assert.That(new FileInfo(_file).Length, Is.LessThan(new FileInfo(stl).Length));
        }
        finally
        {
            File.Delete(stl);
        }
    }

    /// A lens surface laid out the way the engine leaves it: origin at zero and already scaled,
    /// with relief that varies in both directions.
    private static Mesh Surface(int width, int height, double gridScale = 1.0)
    {
        Mesh mesh = MeshBuilder.Square(width, height);

        foreach (Point3D node in mesh.Nodes)
        {
            node.X = (node.X - 1) * gridScale;
            node.Y = (node.Y - 1) * gridScale;
            node.Z = 0.5 + (0.3 * Math.Sin(node.Ix * 0.6) * Math.Cos(node.Iy * 0.4));
        }

        return mesh;
    }

    private static int Count(string step, string entity)
        => Regex.Matches(step, $"={Regex.Escape(entity)}").Count;

    /// The solid's eight corner vertices, read back off the file rather than off the mesh, so the
    /// bounds are checked as the recipient's CAD would see them. Control points are deliberately
    /// not included: a cubic control net can sit outside the data it interpolates, and it is the
    /// vertices and boundary curves that fix the solid's extent.
    private static Vec3[] Vertices(string file)
    {
        string step = File.ReadAllText(file);

        Dictionary<string, Vec3> points = Points(step);

        return Regex.Matches(step, @"=VERTEX_POINT\('',#(\d+)\)")
            .Select(match => points[match.Groups[1].Value])
            .ToArray();
    }

    /// The control points of each B-spline boundary curve, resolved through their point references.
    private static Vec3[][] BoundaryCurveControlPoints(string file)
    {
        string step = File.ReadAllText(file);
        Dictionary<string, Vec3> points = Points(step);

        return Regex.Matches(step, @"=B_SPLINE_CURVE_WITH_KNOTS\('',\d+,\(([^)]+)\)")
            .Select(match => Regex.Matches(match.Groups[1].Value, @"#(\d+)")
                .Select(reference => points[reference.Groups[1].Value])
                .ToArray())
            .ToArray();
    }

    private static double Spread(Vec3[] curve, int axis)
    {
        double[] values = curve.Select(point => axis switch
        {
            0 => point.X,
            1 => point.Y,
            _ => point.Z,
        }).ToArray();

        return values.Max() - values.Min();
    }

    private static Dictionary<string, Vec3> Points(string step)
        => Regex.Matches(step, @"#(\d+)=CARTESIAN_POINT\('',\(([^)]+)\)\)")
            .ToDictionary(
                match => match.Groups[1].Value,
                match => ToVec3(match.Groups[2].Value));

    private static Vec3 ToVec3(string coordinates)
    {
        double[] parsed = coordinates
            .Split(',')
            .Select(value => double.Parse(value.TrimEnd('.'), CultureInfo.InvariantCulture))
            .ToArray();

        return new Vec3(parsed[0], parsed[1], parsed[2]);
    }
}
