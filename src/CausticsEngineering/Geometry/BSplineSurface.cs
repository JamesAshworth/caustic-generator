namespace CausticsEngineering.Geometry;

/// A clamped bicubic tensor-product B-spline surface that passes through every point of a
/// topologically rectangular grid of data.
///
/// Interpolating rather than approximating matters here: feeding the solved node grid straight in
/// as control points would smooth the relief, and the relief is the caustic.
///
/// Clamping earns something beyond the usual corner interpolation. The first and last basis
/// functions are 1 at their end of the parameter range and 0 everywhere else, so a boundary row of
/// the control net is the one-dimensional interpolant of the corresponding boundary row of data,
/// and the corner control points sit on the data exactly. StepWriter leans on both to hand its
/// skirt faces the surface's own boundary curves and its vertices the surface's own corners.
public sealed class BSplineSurface
{
    public const int Degree = 3;

    private BSplineSurface(Vec3[,] controlPoints, double[] uKnots, double[] vKnots)
    {
        ControlPoints = controlPoints;
        UKnots = uKnots;
        VKnots = vKnots;
    }

    /// Indexed [u, v], matching the outer-list-is-u convention of STEP's control_points_list.
    public Vec3[,] ControlPoints { get; }

    /// Full knot vector, length UCount + Degree + 1.
    public double[] UKnots { get; }

    public double[] VKnots { get; }

    public int UCount => ControlPoints.GetLength(0);

    public int VCount => ControlPoints.GetLength(1);

    /// Fits the surface through every point of the grid. Both grid dimensions must be at least
    /// Degree + 1, since a cubic interpolant needs four points per direction to be determined.
    public static BSplineSurface Interpolate(Vec3[,] grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        int uCount = grid.GetLength(0);
        int vCount = grid.GetLength(1);

        if (uCount <= Degree || vCount <= Degree)
        {
            throw new ArgumentException(
                $"A degree {Degree} interpolant needs at least {Degree + 1} points per direction, got {uCount} by {vCount}.",
                nameof(grid));
        }

        double[] uParams = UniformParameters(uCount);
        double[] vParams = UniformParameters(vCount);

        double[] uKnots = ClampedKnots(uParams);
        double[] vKnots = ClampedKnots(vParams);

        // Both solves reuse one factorisation per direction: the collocation matrix depends only on
        // the parameters and knots, not on the data
        LuDecomposition uSolver = new(Collocation(uParams, uKnots));
        LuDecomposition vSolver = new(Collocation(vParams, vKnots));

        Vec3[,] intermediate = SolveAlongU(grid, uSolver, uCount, vCount);
        Vec3[,] controlPoints = SolveAlongV(intermediate, vSolver, uCount, vCount);

        return new BSplineSurface(controlPoints, uKnots, vKnots);
    }

    /// The control points of the isoparametric curve at u = 0 or u = 1, which is a boundary of the
    /// surface. Its knots and degree are the surface's v knots and degree.
    public Vec3[] UBoundaryControlPoints(bool atEnd)
    {
        int u = atEnd ? UCount - 1 : 0;

        Vec3[] row = new Vec3[VCount];
        for (int v = 0; v < VCount; v++)
        {
            row[v] = ControlPoints[u, v];
        }

        return row;
    }

    /// The control points of the isoparametric curve at v = 0 or v = 1, over the surface's u knots.
    public Vec3[] VBoundaryControlPoints(bool atEnd)
    {
        int v = atEnd ? VCount - 1 : 0;

        Vec3[] row = new Vec3[UCount];
        for (int u = 0; u < UCount; u++)
        {
            row[u] = ControlPoints[u, v];
        }

        return row;
    }

    /// Evaluates the surface, for tests and for anything that needs to check the fit.
    public Vec3 Evaluate(double u, double v)
    {
        int uSpan = FindSpan(UCount, u, UKnots);
        int vSpan = FindSpan(VCount, v, VKnots);

        double[] uBasis = BasisValues(uSpan, u, UKnots);
        double[] vBasis = BasisValues(vSpan, v, VKnots);

        Vec3 point = default;
        for (int i = 0; i <= Degree; i++)
        {
            for (int j = 0; j <= Degree; j++)
            {
                point += uBasis[i] * vBasis[j] * ControlPoints[uSpan - Degree + i, vSpan - Degree + j];
            }
        }

        return point;
    }

    private static Vec3[,] SolveAlongU(Vec3[,] grid, LuDecomposition solver, int uCount, int vCount)
    {
        Vec3[,] intermediate = new Vec3[uCount, vCount];

        double[] rhs = new double[uCount];
        for (int v = 0; v < vCount; v++)
        {
            for (int component = 0; component < 3; component++)
            {
                for (int u = 0; u < uCount; u++)
                {
                    rhs[u] = Component(grid[u, v], component);
                }

                double[] solved = solver.Solve(rhs);
                for (int u = 0; u < uCount; u++)
                {
                    intermediate[u, v] = WithComponent(intermediate[u, v], component, solved[u]);
                }
            }
        }

        return intermediate;
    }

    private static Vec3[,] SolveAlongV(Vec3[,] intermediate, LuDecomposition solver, int uCount, int vCount)
    {
        Vec3[,] controlPoints = new Vec3[uCount, vCount];

        double[] rhs = new double[vCount];
        for (int u = 0; u < uCount; u++)
        {
            for (int component = 0; component < 3; component++)
            {
                for (int v = 0; v < vCount; v++)
                {
                    rhs[v] = Component(intermediate[u, v], component);
                }

                double[] solved = solver.Solve(rhs);
                for (int v = 0; v < vCount; v++)
                {
                    controlPoints[u, v] = WithComponent(controlPoints[u, v], component, solved[v]);
                }
            }
        }

        return controlPoints;
    }

    private static double Component(Vec3 point, int component)
        => component switch
        {
            0 => point.X,
            1 => point.Y,
            _ => point.Z,
        };

    private static Vec3 WithComponent(Vec3 point, int component, double value)
        => component switch
        {
            0 => point with { X = value },
            1 => point with { Y = value },
            _ => point with { Z = value },
        };

    private static double[] UniformParameters(int count)
    {
        double[] parameters = new double[count];
        for (int i = 0; i < count; i++)
        {
            parameters[i] = (double)i / (count - 1);
        }

        return parameters;
    }

    /// De Boor's averaging placement, clamped at both ends. Averaging the parameters is what keeps
    /// the collocation matrix nonsingular, by satisfying the Schoenberg-Whitney conditions.
    private static double[] ClampedKnots(double[] parameters)
    {
        int count = parameters.Length;
        double[] knots = new double[count + Degree + 1];

        for (int i = 0; i <= Degree; i++)
        {
            knots[i] = 0.0;
            knots[count + Degree - i] = 1.0;
        }

        for (int j = 1; j <= count - Degree - 1; j++)
        {
            double sum = 0.0;
            for (int i = j; i <= j + Degree - 1; i++)
            {
                sum += parameters[i];
            }

            knots[j + Degree] = sum / Degree;
        }

        return knots;
    }

    private static double[,] Collocation(double[] parameters, double[] knots)
    {
        int count = parameters.Length;
        double[,] matrix = new double[count, count];

        for (int row = 0; row < count; row++)
        {
            int span = FindSpan(count, parameters[row], knots);
            double[] basis = BasisValues(span, parameters[row], knots);

            for (int k = 0; k <= Degree; k++)
            {
                matrix[row, span - Degree + k] = basis[k];
            }
        }

        return matrix;
    }

    /// Index of the knot span containing the parameter, clamped so that the closing parameter
    /// value lands in the last non-empty span rather than off the end.
    private static int FindSpan(int count, double parameter, double[] knots)
    {
        int last = count - 1;
        if (parameter >= knots[last + 1])
        {
            return last;
        }

        int low = Degree;
        int high = last + 1;
        int mid = (low + high) / 2;

        while (parameter < knots[mid] || parameter >= knots[mid + 1])
        {
            if (parameter < knots[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }

            mid = (low + high) / 2;
        }

        return mid;
    }

    /// The Degree + 1 basis functions that are non-zero on the given span, by the triangular
    /// recurrence. Built to avoid the divide-by-zero the textbook definition runs into on
    /// repeated knots.
    private static double[] BasisValues(int span, double parameter, double[] knots)
    {
        double[] basis = new double[Degree + 1];
        double[] left = new double[Degree + 1];
        double[] right = new double[Degree + 1];

        basis[0] = 1.0;

        for (int j = 1; j <= Degree; j++)
        {
            left[j] = parameter - knots[span + 1 - j];
            right[j] = knots[span + j] - parameter;

            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                double temp = basis[r] / (right[r + 1] + left[j - r]);
                basis[r] = saved + (right[r + 1] * temp);
                saved = left[j - r] * temp;
            }

            basis[j] = saved;
        }

        return basis;
    }

    /// Dense LU with partial pivoting, factored once and back-substituted many times. The
    /// collocation matrices are a few hundred square, so a banded solver would buy nothing.
    private sealed class LuDecomposition
    {
        private readonly double[,] _lu;
        private readonly int[] _pivots;
        private readonly int _size;

        public LuDecomposition(double[,] matrix)
        {
            _size = matrix.GetLength(0);
            _lu = (double[,])matrix.Clone();
            _pivots = new int[_size];

            for (int i = 0; i < _size; i++)
            {
                _pivots[i] = i;
            }

            Factor();
        }

        public double[] Solve(double[] rhs)
        {
            double[] solution = new double[_size];
            for (int i = 0; i < _size; i++)
            {
                solution[i] = rhs[_pivots[i]];
            }

            for (int i = 1; i < _size; i++)
            {
                double sum = solution[i];
                for (int j = 0; j < i; j++)
                {
                    sum -= _lu[i, j] * solution[j];
                }

                solution[i] = sum;
            }

            for (int i = _size - 1; i >= 0; i--)
            {
                double sum = solution[i];
                for (int j = i + 1; j < _size; j++)
                {
                    sum -= _lu[i, j] * solution[j];
                }

                solution[i] = sum / _lu[i, i];
            }

            return solution;
        }

        private void Factor()
        {
            for (int column = 0; column < _size; column++)
            {
                int best = column;
                double bestMagnitude = Math.Abs(_lu[column, column]);
                for (int row = column + 1; row < _size; row++)
                {
                    double magnitude = Math.Abs(_lu[row, column]);
                    if (magnitude > bestMagnitude)
                    {
                        best = row;
                        bestMagnitude = magnitude;
                    }
                }

                if (bestMagnitude == 0.0)
                {
                    throw new InvalidOperationException(
                        $"The B-spline collocation matrix is singular at column {column}, so the surface cannot be fitted.");
                }

                if (best != column)
                {
                    SwapRows(best, column);
                }

                for (int row = column + 1; row < _size; row++)
                {
                    double factor = _lu[row, column] / _lu[column, column];
                    _lu[row, column] = factor;

                    for (int j = column + 1; j < _size; j++)
                    {
                        _lu[row, j] -= factor * _lu[column, j];
                    }
                }
            }
        }

        private void SwapRows(int a, int b)
        {
            (_pivots[a], _pivots[b]) = (_pivots[b], _pivots[a]);

            for (int j = 0; j < _size; j++)
            {
                (_lu[a, j], _lu[b, j]) = (_lu[b, j], _lu[a, j]);
            }
        }
    }
}
