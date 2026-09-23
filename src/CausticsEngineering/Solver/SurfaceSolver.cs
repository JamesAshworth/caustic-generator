using CausticsEngineering.Geometry;

namespace CausticsEngineering.Solver;

public static class SurfaceSolver
{
    private const double RefractiveIndexLens = 1.49;

    /// Recovers the lens height field that produces the marched mesh's ray displacements.
    /// Each node's horizontal displacement gives the refracted ray angle, Snell's law turns that
    /// into a required surface normal, and the height field is the Poisson solution whose
    /// gradient matches those normals.
    public static (double[,] Heights, double MetersPerPixel) FindSurface(
        Mesh mesh,
        double[,] image,
        double focalLength,
        double artifactLongestEdgeMeters,
        Action<string>? log = null)
    {
        int width = image.GetLength(0);
        int height = image.GetLength(1);

        double h = focalLength;
        // Scale off the longer axis, so the artifact size is the longest edge of the lens
        // whatever the image's aspect ratio
        double metersPerPixel = artifactLongestEdgeMeters / Math.Max(width, height);
        log?.Invoke($"Meters per pixel: {metersPerPixel}");

        double[,] normalX = new double[width + 1, height + 1];
        double[,] normalY = new double[width + 1, height + 1];

        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                Point3D node = mesh.NodeArray[i, j];
                double dx = (node.Ix - node.X) * metersPerPixel;
                double dy = (node.Iy - node.Y) * metersPerPixel;

                double littleH = node.Z * metersPerPixel;
                double dz = h - littleH;

                normalX[i, j] = Math.Tan(Math.Atan(dx / dz) / (RefractiveIndexLens - 1));
                normalY[i, j] = Math.Tan(Math.Atan(dy / dz) / (RefractiveIndexLens - 1));
            }
        }

        double[,] divergence = new double[width, height];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                divergence[i, j] =
                    normalX[i + 1, j] - normalX[i, j] +
                    normalY[i, j + 1] - normalY[i, j];
            }
        }

        log?.Invoke($"Divergence sum: {ScalarField.Sum(divergence)}");
        ScalarField.SubtractMean(divergence, width * height);

        double[,] heights = new double[width, height];
        ScalarField.RelaxToConvergence(heights, divergence, log: log);

        return (heights, metersPerPixel);
    }

    /// Writes the solved heights onto the mesh and converts the whole mesh from pixel units into
    /// millimetres. The surface is shifted so its lowest point sits at exactly z = 0, which makes
    /// the back face's depth the minimum material thickness. XY is shifted to start at the origin,
    /// so the model's bounding box is the artifact size.
    /// The mesh is one node wider and taller than the height field, so the trailing row and column
    /// repeat their neighbour.
    public static void SetHeightsInMillimetres(Mesh mesh, double[,] heights, double mmPerPixel, double heightScale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(heights);

        int width = heights.GetLength(0);
        int height = heights.GetLength(1);

        double lowest = ScalarField.Min(heights) * heightScale;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                mesh.NodeArray[x, y].Z = (heights[x, y] * heightScale - lowest) * mmPerPixel;
            }
        }

        for (int y = 0; y < height; y++)
        {
            mesh.NodeArray[width, y].Z = mesh.NodeArray[width - 1, y].Z;
        }

        for (int x = 0; x <= width; x++)
        {
            mesh.NodeArray[x, height].Z = mesh.NodeArray[x, height - 1].Z;
        }

        // XY last: the height pass above reads nothing from XY, and FindSurface has already run
        foreach (Point3D node in mesh.Nodes)
        {
            node.X = (node.X - 1) * mmPerPixel;
            node.Y = (node.Y - 1) * mmPerPixel;
        }
    }
}
