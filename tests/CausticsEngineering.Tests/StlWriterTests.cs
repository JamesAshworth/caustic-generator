using CausticsEngineering.Geometry;
using CausticsEngineering.Io;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class StlWriterTests
{
    private const int HeaderBytes = 80;
    private const int FacetBytes = 50;

    private string _file = string.Empty;

    [SetUp]
    public void SetUp() => _file = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.stl");

    [TearDown]
    public void TearDown() => File.Delete(_file);

    [Test]
    public void Save_Mesh_WritesOneBinaryFacetPerTriangle()
    {
        // GIVEN a mesh with two triangles
        Mesh mesh = MeshBuilder.Square(2, 2);

        // WHEN it is saved
        StlWriter.Save(mesh, _file);
        byte[] bytes = File.ReadAllBytes(_file);

        // THEN the file is a header, a facet count, and one fixed-size facet per triangle
        Assert.Multiple(() =>
        {
            Assert.That(bytes, Has.Length.EqualTo(HeaderBytes + 4 + (2 * FacetBytes)));
            Assert.That(BitConverter.ToUInt32(bytes, HeaderBytes), Is.EqualTo(2));
        });
    }

    [Test]
    public void Save_FlatMeshWoundAnticlockwise_WritesAnUpwardNormal()
    {
        // GIVEN a flat mesh in the z = 0 plane
        Mesh mesh = MeshBuilder.Square(2, 2);

        // WHEN it is saved
        StlWriter.Save(mesh, _file);
        (float X, float Y, float Z) normal = ReadFacetNormal(0);

        // THEN the first facet's normal is a unit vector along z
        Assert.Multiple(() =>
        {
            Assert.That(normal.X, Is.EqualTo(0));
            Assert.That(normal.Y, Is.EqualTo(0));
            Assert.That(Math.Abs(normal.Z), Is.EqualTo(1.0f).Within(1e-6));
        });
    }

    [Test]
    public void Save_WithReverse_FlipsTheFacetNormal()
    {
        // GIVEN a flat mesh
        Mesh mesh = MeshBuilder.Square(2, 2);
        StlWriter.Save(mesh, _file);
        float forwardZ = ReadFacetNormal(0).Z;

        // WHEN it is saved with reversed winding
        StlWriter.Save(mesh, _file, reverse: true);
        float reversedZ = ReadFacetNormal(0).Z;

        // THEN the normal points the other way
        Assert.That(reversedZ, Is.EqualTo(-forwardZ));
    }

    [Test]
    public void Save_WithScale_ScalesVerticesInPlaneAndInZIndependently()
    {
        // GIVEN a mesh whose first node sits at (1, 1) with a height of 4
        Mesh mesh = MeshBuilder.Square(2, 2);
        mesh.NodeArray[0, 0].Z = 4;

        // WHEN it is saved with separate in-plane and height scales
        StlWriter.Save(mesh, _file, scale: 2.0, scaleZ: 0.5);
        (float X, float Y, float Z) vertex = ReadFacetVertex(facet: 0, vertex: 0);

        // THEN each axis carries its own scale
        Assert.Multiple(() =>
        {
            Assert.That(vertex.X, Is.EqualTo(2.0f));
            Assert.That(vertex.Y, Is.EqualTo(2.0f));
            Assert.That(vertex.Z, Is.EqualTo(2.0f));
        });
    }

    [Test]
    public void Save_WithFlipXy_SwapsTheInPlaneCoordinates()
    {
        // GIVEN a mesh whose first node sits at distinct x and y
        Mesh mesh = MeshBuilder.Square(2, 2);
        mesh.NodeArray[0, 0].X = 3;
        mesh.NodeArray[0, 0].Y = 7;

        // WHEN it is saved with flipped axes
        StlWriter.Save(mesh, _file, flipXy: true);
        (float X, float Y, float Z) vertex = ReadFacetVertex(facet: 0, vertex: 0);

        // THEN y is written first
        Assert.Multiple(() =>
        {
            Assert.That(vertex.X, Is.EqualTo(7.0f));
            Assert.That(vertex.Y, Is.EqualTo(3.0f));
        });
    }

    [Test]
    public void Save_SolidMesh_WritesEveryFacetWithAFiniteNormal()
    {
        // GIVEN a solidified mesh with varying heights
        Mesh surface = MeshBuilder.Square(5, 5);
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                surface.NodeArray[x, y].Z = x + y;
            }
        }

        Mesh solid = MeshBuilder.Solidify(surface, offset: 5);

        // WHEN it is saved
        StlWriter.Save(solid, _file);

        // THEN every facet carries a finite normal, so no triangle is degenerate
        Assert.That(
            Enumerable.Range(0, solid.Triangles.Length)
                .SelectMany(i => new[] { ReadFacetNormal(i).X, ReadFacetNormal(i).Y, ReadFacetNormal(i).Z }),
            Has.All.Matches<float>(float.IsFinite));
    }

    private (float X, float Y, float Z) ReadFacetNormal(int facet) => ReadTriple(HeaderBytes + 4 + (facet * FacetBytes));

    private (float X, float Y, float Z) ReadFacetVertex(int facet, int vertex)
        => ReadTriple(HeaderBytes + 4 + (facet * FacetBytes) + 12 + (vertex * 12));

    private (float X, float Y, float Z) ReadTriple(int offset)
    {
        byte[] bytes = File.ReadAllBytes(_file);

        return (
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + 4),
            BitConverter.ToSingle(bytes, offset + 8));
    }
}
