namespace CausticsEngineering.Geometry;

/// Mesh node. Ix/Iy record the node's original integer grid position, which stays fixed
/// while X/Y march, so a node can always be mapped back to its source pixel.
public sealed class Point3D(double x, double y, double z, int ix, int iy)
{
    public double X { get; set; } = x;

    public double Y { get; set; } = y;

    public double Z { get; set; } = z;

    public int Ix { get; } = ix;

    public int Iy { get; } = iy;

    public static double Distance(Point3D p1, Point3D p2)
    {
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static Point3D Midpoint(Point3D p1, Point3D p2)
        => new(0.5 * p1.X + 0.5 * p2.X, 0.5 * p1.Y + 0.5 * p2.Y, 0.5 * p1.Z + 0.5 * p2.Z, 0, 0);

    public static Point3D Centroid(Point3D p1, Point3D p2, Point3D p3)
        => new(
            (p1.X + p2.X + p3.X) / 3.0,
            (p1.Y + p2.Y + p3.Y) / 3.0,
            (p1.Z + p2.Z + p3.Z) / 3.0,
            0,
            0);

    public static double TriangleArea(Point3D p1, Point3D p2, Point3D p3)
    {
        double a = Distance(p1, p2);
        double b = Distance(p2, p3);
        double c = Distance(p3, p1);
        double s = (a + b + c) / 2.0;

        return Math.Sqrt(s * (s - a) * (s - b) * (s - c));
    }
}
