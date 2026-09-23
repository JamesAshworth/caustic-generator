using CausticsEngineering.Geometry;

namespace CausticsEngineering.Solver;

public static class ScalarField
{
    /// Forward-difference gradient. The right edge of du and the bottom edge of dv are left at zero.
    public static (double[,] Du, double[,] Dv) Gradient(double[,] f)
    {
        int width = f.GetLength(0);
        int height = f.GetLength(1);

        double[,] du = new double[width, height];
        double[,] dv = new double[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                du[x, y] = x == width - 1 ? 0 : f[x + 1, y] - f[x, y];
                dv[x, y] = y == height - 1 ? 0 : f[x, y + 1] - f[x, y];
            }
        }

        return (du, dv);
    }

    /// Area covered by each pixel-quad of the mesh, split along its diagonal into two triangles.
    /// Brightness is proportional to the reciprocal of this area, so it is the quantity the solver drives.
    public static double[,] PixelArea(Mesh mesh)
    {
        double[,] areas = new double[mesh.Width - 1, mesh.Height - 1];

        for (int x = 0; x < mesh.Width - 1; x++)
        {
            for (int y = 0; y < mesh.Height - 1; y++)
            {
                Point3D upperLeft = mesh.NodeArray[x, y];
                Point3D upperRight = mesh.NodeArray[x + 1, y];
                Point3D lowerLeft = mesh.NodeArray[x, y + 1];
                Point3D lowerRight = mesh.NodeArray[x + 1, y + 1];

                areas[x, y] =
                    Point3D.TriangleArea(lowerLeft, upperRight, upperLeft) +
                    Point3D.TriangleArea(lowerLeft, lowerRight, upperRight);
            }
        }

        return areas;
    }

    /// One sweep of successive over-relaxation solving the Poisson equation laplacian(matrix) = d.
    /// Neumann boundary conditions are baked in: edge and corner nodes relax against only the
    /// neighbours they have, which is equivalent to forcing zero derivative across the boundary.
    /// See https://math.stackexchange.com/questions/3790299
    /// Returns the largest absolute update applied, for convergence testing.
    public static double Relax(double[,] matrix, double[,] d, double omega = 1.99)
    {
        int width = matrix.GetLength(0);
        int height = matrix.GetLength(1);

        double maxUpdate = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double neighbourSum = 0;
                int neighbourCount = 0;

                if (x > 0)
                {
                    neighbourSum += matrix[x - 1, y];
                    neighbourCount++;
                }

                if (x < width - 1)
                {
                    neighbourSum += matrix[x + 1, y];
                    neighbourCount++;
                }

                if (y > 0)
                {
                    neighbourSum += matrix[x, y - 1];
                    neighbourCount++;
                }

                if (y < height - 1)
                {
                    neighbourSum += matrix[x, y + 1];
                    neighbourCount++;
                }

                double delta = omega / neighbourCount *
                    (neighbourSum - neighbourCount * matrix[x, y] - d[x, y]);

                if (Math.Abs(delta) > maxUpdate)
                {
                    maxUpdate = Math.Abs(delta);
                }

                matrix[x, y] += delta;
            }
        }

        return maxUpdate;
    }

    /// Relaxes in place until the largest update falls below tolerance, or the iteration cap is hit.
    public static RelaxationResult RelaxToConvergence(
        double[,] matrix,
        double[,] d,
        int maxIterations = 10_000,
        double tolerance = 0.00001,
        Action<string>? log = null)
    {
        double maxUpdate = double.NaN;
        for (int i = 1; i <= maxIterations; i++)
        {
            maxUpdate = Relax(matrix, d);

            if (double.IsNaN(maxUpdate))
            {
                return new RelaxationResult(i, maxUpdate, Converged: false);
            }

            if (i % 500 == 0)
            {
                log?.Invoke($"Relaxation step {i}, max update {maxUpdate}");
            }

            if (maxUpdate < tolerance)
            {
                log?.Invoke($"Convergence reached at step {i} with max update of {maxUpdate}");

                return new RelaxationResult(i, maxUpdate, Converged: true);
            }
        }

        return new RelaxationResult(maxIterations, maxUpdate, Converged: false);
    }

    /// Shifts the field so that it sums to zero, which a Poisson problem with Neumann
    /// boundaries requires for a solution to exist.
    public static void SubtractMean(double[,] field, int divisor)
    {
        double sum = Sum(field);
        int width = field.GetLength(0);
        int height = field.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                field[x, y] -= sum / divisor;
            }
        }
    }

    public static double Sum(double[,] field)
    {
        double sum = 0;
        foreach (double value in field)
        {
            sum += value;
        }

        return sum;
    }

    public static double Min(double[,] field)
    {
        double min = double.MaxValue;
        foreach (double value in field)
        {
            min = Math.Min(min, value);
        }

        return min;
    }

    public static double Max(double[,] field)
    {
        double max = double.MinValue;
        foreach (double value in field)
        {
            max = Math.Max(max, value);
        }

        return max;
    }
}

public readonly record struct RelaxationResult(int Iterations, double MaxUpdate, bool Converged);
