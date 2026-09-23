using CausticsEngineering.Geometry;
using CausticsEngineering.Io;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class ObjWriterTests
{
    private string _file = string.Empty;

    [SetUp]
    public void SetUp() => _file = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.obj");

    [TearDown]
    public void TearDown() => File.Delete(_file);

    [Test]
    public void Save_Mesh_WritesVerticesFacesAndGridDimensions()
    {
        // GIVEN a small mesh
        Mesh mesh = MeshBuilder.Square(2, 2);

        // WHEN it is saved
        ObjWriter.Save(mesh, _file);
        string[] lines = File.ReadAllLines(_file);

        // THEN the file holds one vertex line per node, one face line per triangle, and the dims line
        Assert.Multiple(() =>
        {
            Assert.That(lines.Count(l => l.StartsWith('v')), Is.EqualTo(4));
            Assert.That(lines.Count(l => l.StartsWith('f')), Is.EqualTo(2));
            Assert.That(lines[^1], Is.EqualTo("dims 2 2"));
        });
    }

    [Test]
    public void Save_WithFlipXy_SwapsTheFirstTwoVertexCoordinates()
    {
        // GIVEN a mesh whose first node sits at distinct x and y
        Mesh mesh = MeshBuilder.Square(2, 2);
        mesh.NodeArray[0, 0].X = 3;
        mesh.NodeArray[0, 0].Y = 7;

        // WHEN it is saved with flipped axes
        ObjWriter.Save(mesh, _file, flipXy: true);
        string firstVertex = File.ReadLines(_file).First();

        // THEN y precedes x
        Assert.That(firstVertex, Is.EqualTo("v 7 3 0"));
    }

    [Test]
    public void Save_WithReverse_ReversesFaceWinding()
    {
        // GIVEN a mesh
        Mesh mesh = MeshBuilder.Square(2, 2);
        string expected = $"f {mesh.Triangles[0].Pt3} {mesh.Triangles[0].Pt2} {mesh.Triangles[0].Pt1}";

        // WHEN it is saved reversed
        ObjWriter.Save(mesh, _file, reverse: true);
        string firstFace = File.ReadLines(_file).First(l => l.StartsWith('f'));

        // THEN the face's vertex order is reversed
        Assert.That(firstFace, Is.EqualTo(expected));
    }

    [Test]
    public void Load_SavedMesh_RoundTripsGeometryAndTopology()
    {
        // GIVEN a saved mesh with non-trivial heights
        Mesh mesh = MeshBuilder.Square(3, 3);
        mesh.NodeArray[1, 1].Z = 2.5;
        ObjWriter.Save(mesh, _file);

        // WHEN it is loaded back
        Mesh loaded = ObjWriter.Load(_file);

        // THEN dimensions, counts and coordinates survive (z is scaled by 10 on load,
        // matching the reference implementation)
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(3));
            Assert.That(loaded.Nodes, Has.Length.EqualTo(9));
            Assert.That(loaded.Triangles, Has.Length.EqualTo(8));
            Assert.That(loaded.NodeArray[1, 1].X, Is.EqualTo(2));
            Assert.That(loaded.NodeArray[1, 1].Z, Is.EqualTo(25));
        });
    }
}
