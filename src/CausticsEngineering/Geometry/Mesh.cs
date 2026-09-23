namespace CausticsEngineering.Geometry;

/// A grid of 3D points plus the triangulation over it. Nodes and NodeArray share references,
/// so moving a node through either view moves it in both.
public sealed class Mesh(Point3D[] nodes, Point3D[,] nodeArray, Triangle[] triangles, int width, int height)
{
    public Point3D[] Nodes { get; } = nodes;

    public Point3D[,] NodeArray { get; } = nodeArray;

    public Triangle[] Triangles { get; } = triangles;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public Point3D Node(int oneBasedIndex) => Nodes[oneBasedIndex - 1];

    public double TriangleArea(int index)
    {
        Triangle triangle = Triangles[index];

        return Point3D.TriangleArea(Node(triangle.Pt1), Node(triangle.Pt2), Node(triangle.Pt3));
    }

    public Point3D Centroid(int index)
    {
        Triangle triangle = Triangles[index];

        return Point3D.Centroid(Node(triangle.Pt1), Node(triangle.Pt2), Node(triangle.Pt3));
    }
}
