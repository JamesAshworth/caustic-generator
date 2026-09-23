using CausticsEngineering.Geometry;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class MeshBuilderTests
{
    [Test]
    public void Square_GivenDimensions_ProducesTwoTrianglesPerCell()
    {
        // GIVEN a 5x4 grid
        // WHEN the mesh is built
        Mesh mesh = MeshBuilder.Square(5, 4);

        // THEN node and triangle counts follow the grid
        Assert.Multiple(() =>
        {
            Assert.That(mesh.Nodes, Has.Length.EqualTo(20));
            Assert.That(mesh.Triangles, Has.Length.EqualTo(4 * 3 * 2));
        });
    }

    [Test]
    public void Square_NodeArrayAndNodeList_ShareReferences()
    {
        // GIVEN a square mesh
        Mesh mesh = MeshBuilder.Square(4, 4);

        // WHEN a node is moved through the array view
        mesh.NodeArray[0, 0].X = 8;

        // THEN the flat list sees the same move
        Assert.That(mesh.Nodes[0].X, Is.EqualTo(8));
    }

    [Test]
    public void Square_NodeIndices_AreRowMajorAndOneBased()
    {
        // GIVEN a 3x3 mesh
        Mesh mesh = MeshBuilder.Square(3, 3);

        // WHEN the node at grid position (2, 3) is looked up both ways
        Point3D fromArray = mesh.NodeArray[1, 2];
        Point3D fromIndex = mesh.Node((3 - 1) * 3 + 2);

        // THEN both views agree, and the node remembers its grid position
        Assert.Multiple(() =>
        {
            Assert.That(fromIndex, Is.SameAs(fromArray));
            Assert.That(fromArray.Ix, Is.EqualTo(2));
            Assert.That(fromArray.Iy, Is.EqualTo(3));
        });
    }

    [Test]
    public void Square_UnitCell_HasUnitArea()
    {
        // GIVEN a mesh whose nodes sit one unit apart
        Mesh mesh = MeshBuilder.Square(2, 2);

        // WHEN the two triangles covering the single cell are summed
        double area = mesh.TriangleArea(0) + mesh.TriangleArea(1);

        // THEN the cell covers one square unit
        Assert.That(area, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void Solidify_GivenSurface_DoublesNodesAndClosesTheSkirt()
    {
        // GIVEN a 4x4 open surface
        Mesh surface = MeshBuilder.Square(4, 4);

        // WHEN it is solidified
        Mesh solid = MeshBuilder.Solidify(surface, offset: 10);

        // THEN it has a top and bottom surface plus two triangles per edge node,
        // and every triangle references a real node
        int edgeNodes = 4 * 2 + (4 - 2) * 2;
        Assert.Multiple(() =>
        {
            Assert.That(solid.Nodes, Has.Length.EqualTo(32));
            Assert.That(solid.Triangles, Has.Length.EqualTo(3 * 3 * 2 * 2 + edgeNodes * 2));
            Assert.That(
                solid.Triangles.SelectMany(t => new[] { t.Pt1, t.Pt2, t.Pt3 }),
                Has.All.InRange(1, solid.Nodes.Length));
        });
    }

    [Test]
    public void Solidify_BottomSurface_SitsAtNegativeOffset()
    {
        // GIVEN a flat surface at z = 0
        Mesh surface = MeshBuilder.Square(3, 3);

        // WHEN it is solidified with an offset of 7
        Mesh solid = MeshBuilder.Solidify(surface, offset: 7);

        // THEN the bottom half of the node list sits at -7
        Assert.That(solid.Nodes.Take(9).Select(n => n.Z), Has.All.EqualTo(-7));
    }

    [Test]
    public void FromMatrix_ScalarField_BecomesNodeHeights()
    {
        // GIVEN a scalar field
        double[,] field = { { 1, 2 }, { 3, 4 } };

        // WHEN it is lifted onto a mesh
        Mesh mesh = MeshBuilder.FromMatrix(field);

        // THEN node heights match the field
        Assert.Multiple(() =>
        {
            Assert.That(mesh.NodeArray[0, 0].Z, Is.EqualTo(1));
            Assert.That(mesh.NodeArray[1, 1].Z, Is.EqualTo(4));
        });
    }
}
