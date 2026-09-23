using CausticsEngineering.Geometry;

namespace CausticsEngineering.Solver;

public static class MeshMarcher
{
    /// Given three points and their velocities, the two values of t at which the triangle's
    /// area passes through zero. Returns NoSolution for both roots when the quadratic has none.
    public const double NoSolution = -123.0;

    public static (double T1, double T2) FindT(
        Point3D p1,
        Point3D p2,
        Point3D p3,
        Point3D dp1,
        Point3D dp2,
        Point3D dp3)
    {
        double x1 = p2.X - p1.X;
        double y1 = p2.Y - p1.Y;

        double x2 = p3.X - p1.X;
        double y2 = p3.Y - p1.Y;

        double u1 = dp2.X - dp1.X;
        double v1 = dp2.Y - dp1.Y;

        double u2 = dp3.X - dp1.X;
        double v2 = dp3.Y - dp1.Y;

        double a = u1 * v2 - u2 * v1;
        double b = x1 * v1 + y2 * u1 - x2 * v1 - y1 * u2;
        double c = x1 * y2 - x2 * y1;

        if (a != 0)
        {
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                return (NoSolution, NoSolution);
            }

            double d = Math.Sqrt(discriminant);

            return ((-b - d) / (2 * a), (-b + d) / (2 * a));
        }

        // No dependence on t squared, but still one on t
        double root = -c / b;

        return (root, root);
    }

    /// Moves every mesh node down the gradient of phi. The step is half the smallest positive t
    /// over all triangles, so no triangle can invert during the march.
    public static double March(Mesh mesh, double[,] phi, Action<string>? log = null)
    {
        (double[,] gradientU, double[,] gradientV) = ScalarField.Gradient(phi);

        // Mesh XY coordinates are image XY coordinates. The mesh carries one extra row and column
        // at the bottom right so the triangles can be closed, and those borrow their neighbour's velocity.
        Point3D[,] velocities = new Point3D[mesh.Width, mesh.Height];
        for (int x = 1; x <= mesh.Width; x++)
        {
            for (int y = 1; y <= mesh.Height; y++)
            {
                double u = x == mesh.Width
                    ? 0
                    : y == mesh.Height ? gradientU[x - 1, y - 2] : gradientU[x - 1, y - 1];

                double v = y == mesh.Height
                    ? 0
                    : x == mesh.Width ? gradientV[x - 2, y - 1] : gradientV[x - 1, y - 1];

                velocities[x - 1, y - 1] = new Point3D(-u, -v, 0, 0, 0);
            }
        }

        double minT = 10000;
        foreach (Triangle triangle in mesh.Triangles)
        {
            Point3D p1 = mesh.Node(triangle.Pt1);
            Point3D p2 = mesh.Node(triangle.Pt2);
            Point3D p3 = mesh.Node(triangle.Pt3);

            Point3D v1 = velocities[p1.Ix - 1, p1.Iy - 1];
            Point3D v2 = velocities[p2.Ix - 1, p2.Iy - 1];
            Point3D v3 = velocities[p3.Ix - 1, p3.Iy - 1];

            (double t1, double t2) = FindT(p1, p2, p3, v1, v2, v3);

            if (t1 > 0 && t1 < minT)
            {
                minT = t1;
            }

            if (t2 > 0 && t2 < minT)
            {
                minT = t2;
            }
        }

        log?.Invoke($"Overall min_t: {minT}");
        double delta = minT / 2;

        foreach (Point3D point in mesh.Nodes)
        {
            Point3D velocity = velocities[point.Ix - 1, point.Iy - 1];
            point.X += velocity.X * delta;
            point.Y += velocity.Y * delta;
        }

        return minT;
    }
}
