using System.Globalization;
using CausticsEngineering.Geometry;

namespace CausticsEngineering.Io;

/// Writes the lens as an ISO 10303 (STEP) boundary-representation solid.
///
/// STL and OBJ hand over half a million triangles. STEP instead gets a single bicubic B-spline
/// patch for the lens surface, five planar faces for the skirt and back, and a closed shell around
/// the lot: six faces rather than 511,556 facets, in a few megabytes, and a real solid that opens
/// in CAD rather than a mesh a CAD kernel has to be talked into accepting.
///
/// Written as AP214 (automotive_design), which is the most widely read STEP flavour, with
/// millimetre units declared explicitly so the recipient's importer does not have to guess.
public static class StepWriter
{
    private const int Degree = BSplineSurface.Degree;

    /// Writes the surface mesh as a solid STEP file.
    ///
    /// The mesh is the open lens surface, not the output of MeshBuilder.Solidify — the back face
    /// and skirt are generated here as planar faces instead of triangles. bottomZ places the back
    /// face, and gridScale converts a grid step into the units the surface is already in, exactly
    /// as Solidify uses them.
    public static void Save(
        Mesh surface,
        string filename,
        double bottomZ,
        double gridScale = 1.0,
        string productName = "caustic-lens",
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(surface);

        double right = (surface.Width - 1) * gridScale;
        double far = (surface.Height - 1) * gridScale;

        Vec3[,] grid = SnapBoundaryToRectangle(surface, right, far);
        BSplineSurface fitted = BSplineSurface.Interpolate(grid);
        PinBoundariesToRectangle(fitted, right, far);

        using StreamWriter writer = new(filename);

        new StepDocument(writer, fitted, bottomZ, productName, timestamp ?? DateTimeOffset.UtcNow).Write();
    }

    /// Reads the surface into a plain coordinate grid, pulling the four boundary rows onto the
    /// exact rectangle that Solidify gives the back face.
    ///
    /// Marched edge nodes drift a little outside the grid. Left alone that drift makes all four
    /// skirt faces doubly curved, which costs four B-spline surfaces and the tolerance argument
    /// that goes with sewing them. Snapping the boundary makes the skirts exactly planar for a
    /// sub-tolerance change to the relief at the very rim.
    private static Vec3[,] SnapBoundaryToRectangle(Mesh surface, double right, double far)
    {
        int width = surface.Width;
        int height = surface.Height;

        Vec3[,] grid = new Vec3[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Point3D node = surface.NodeArray[x, y];

                double nodeX = x == 0 ? 0.0 : x == width - 1 ? right : node.X;
                double nodeY = y == 0 ? 0.0 : y == height - 1 ? far : node.Y;

                grid[x, y] = new Vec3(nodeX, nodeY, node.Z);
            }
        }

        return grid;
    }

    /// Forces each boundary control row exactly onto its skirt plane.
    ///
    /// Snapping the data is not quite enough. A B-spline reproduces a constant exactly in theory,
    /// but the collocation solve reproduces it only to round-off, so the two boundaries at non-zero
    /// coordinates come back spread over roughly 1e-13 mm. That is far inside the tolerance the
    /// file declares and no importer would complain, but it leaves the skirt faces planar by
    /// accident rather than by construction, and only because the other two boundaries sit at zero,
    /// where a zero right-hand side back-substitutes to exact zero. Pinning the rows removes the
    /// accident.
    private static void PinBoundariesToRectangle(BSplineSurface fitted, double right, double far)
    {
        Vec3[,] controlPoints = fitted.ControlPoints;

        for (int v = 0; v < fitted.VCount; v++)
        {
            controlPoints[0, v] = controlPoints[0, v] with { X = 0.0 };
            controlPoints[fitted.UCount - 1, v] = controlPoints[fitted.UCount - 1, v] with { X = right };
        }

        for (int u = 0; u < fitted.UCount; u++)
        {
            controlPoints[u, 0] = controlPoints[u, 0] with { Y = 0.0 };
            controlPoints[u, fitted.VCount - 1] = controlPoints[u, fitted.VCount - 1] with { Y = far };
        }
    }

    /// Lays out and emits the entity graph. Entity ids are assigned up front so every reference is
    /// known before anything is written, which keeps the whole file streamable.
    private sealed class StepDocument(
        StreamWriter writer,
        BSplineSurface surface,
        double bottomZ,
        string productName,
        DateTimeOffset timestamp)
    {
        // Fixed ids for the product and unit boilerplate
        private const int ApplicationContext = 1;
        private const int ProtocolDefinition = 2;
        private const int ProductContext = 3;
        private const int Product = 4;
        private const int DefinitionFormation = 5;
        private const int DefinitionContext = 6;
        private const int Definition = 7;
        private const int DefinitionShape = 8;
        private const int ShapeRepresentation = 9;
        private const int GeometricContext = 10;
        private const int MillimetreUnit = 11;
        private const int RadianUnit = 12;
        private const int SteradianUnit = 13;
        private const int Uncertainty = 14;
        private const int ShapeDefinition = 15;
        private const int WorldOrigin = 16;
        private const int WorldPlacement = 17;
        private const int Solid = 18;
        private const int Shell = 19;
        private const int FirstFreeId = 20;

        private readonly int _uCount = surface.UCount;
        private readonly int _vCount = surface.VCount;

        private int _nextId = FirstFreeId;

        // Assigned during Write, in the order the entities are emitted
        private int _controlPointBase;
        private int _surfaceId;
        private int[] _topVertices = [];
        private int[] _bottomVertices = [];
        private int[] _topEdges = [];
        private int[] _verticalEdges = [];
        private int[] _bottomEdges = [];

        /// Corner order used throughout: 0 = (0, 0), 1 = (max x, 0), 2 = (max x, max y), 3 = (0, max y).
        private static readonly int[] NextCorner = [1, 2, 3, 0];

        public void Write()
        {
            WriteHeader();

            _controlPointBase = ReserveControlPoints();
            WriteControlPoints();
            WriteSurface();
            WriteBoilerplate();
            WriteVertices();
            WriteEdges();
            WriteFaces();

            WriteFooter();
        }

        private void WriteHeader()
        {
            writer.WriteLine("ISO-10303-21;");
            writer.WriteLine("HEADER;");
            writer.WriteLine("FILE_DESCRIPTION(('caustic lens'),'2;1');");
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"FILE_NAME('{Escape(productName)}','{timestamp.UtcDateTime:yyyy-MM-ddTHH:mm:ss}',(''),(''),'caustic-generator','caustic-generator','');"));
            writer.WriteLine("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));");
            writer.WriteLine("ENDSEC;");
            writer.WriteLine("DATA;");
        }

        private void WriteFooter()
        {
            writer.WriteLine("ENDSEC;");
            writer.WriteLine("END-ISO-10303-21;");
        }

        private int ReserveControlPoints()
        {
            int start = _nextId;
            _nextId += _uCount * _vCount;

            return start;
        }

        private int ControlPointId(int u, int v) => _controlPointBase + (u * _vCount) + v;

        private void WriteControlPoints()
        {
            for (int u = 0; u < _uCount; u++)
            {
                for (int v = 0; v < _vCount; v++)
                {
                    WritePoint(ControlPointId(u, v), surface.ControlPoints[u, v]);
                }
            }
        }

        private void WriteSurface()
        {
            _surfaceId = _nextId++;

            writer.Write(string.Create(CultureInfo.InvariantCulture, $"#{_surfaceId}=B_SPLINE_SURFACE_WITH_KNOTS('',{Degree},{Degree},("));

            for (int u = 0; u < _uCount; u++)
            {
                if (u > 0)
                {
                    writer.Write(',');
                }

                writer.Write('(');
                for (int v = 0; v < _vCount; v++)
                {
                    if (v > 0)
                    {
                        writer.Write(',');
                    }

                    writer.Write(string.Create(CultureInfo.InvariantCulture, $"#{ControlPointId(u, v)}"));
                }

                writer.Write(')');
            }

            writer.Write("),.UNSPECIFIED.,.F.,.F.,.F.,");
            WriteMultiplicities(_uCount);
            writer.Write(',');
            WriteMultiplicities(_vCount);
            writer.Write(',');
            WriteDistinctKnots(surface.UKnots, _uCount);
            writer.Write(',');
            WriteDistinctKnots(surface.VKnots, _vCount);
            writer.WriteLine(",.UNSPECIFIED.);");
        }

        /// STEP lists distinct knots with multiplicities rather than the raw vector. A clamped
        /// vector with averaged interior knots is Degree + 1 at each end and single in between.
        private void WriteMultiplicities(int controlPointCount)
        {
            int interior = controlPointCount - Degree - 1;

            writer.Write('(');
            writer.Write(Degree + 1);
            for (int i = 0; i < interior; i++)
            {
                writer.Write(",1");
            }

            writer.Write(',');
            writer.Write(Degree + 1);
            writer.Write(')');
        }

        private void WriteDistinctKnots(double[] knots, int controlPointCount)
        {
            writer.Write('(');
            writer.Write(Real(0.0));

            for (int i = Degree + 1; i <= controlPointCount - 1; i++)
            {
                writer.Write(',');
                writer.Write(Real(knots[i]));
            }

            writer.Write(',');
            writer.Write(Real(1.0));
            writer.Write(')');
        }

        private void WriteBoilerplate()
        {
            writer.WriteLine($"#{ApplicationContext}=APPLICATION_CONTEXT('automotive design');");
            writer.WriteLine($"#{ProtocolDefinition}=APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{ApplicationContext});");
            writer.WriteLine($"#{ProductContext}=MECHANICAL_CONTEXT('',#{ApplicationContext},'mechanical');");
            writer.WriteLine($"#{Product}=PRODUCT('{Escape(productName)}','{Escape(productName)}','',(#{ProductContext}));");
            writer.WriteLine($"#{DefinitionFormation}=PRODUCT_DEFINITION_FORMATION('','',#{Product});");
            writer.WriteLine($"#{DefinitionContext}=DESIGN_CONTEXT('',#{ApplicationContext},'design');");
            writer.WriteLine($"#{Definition}=PRODUCT_DEFINITION('design','',#{DefinitionFormation},#{DefinitionContext});");
            writer.WriteLine($"#{DefinitionShape}=PRODUCT_DEFINITION_SHAPE('','',#{Definition});");
            writer.WriteLine($"#{ShapeDefinition}=SHAPE_DEFINITION_REPRESENTATION(#{DefinitionShape},#{ShapeRepresentation});");
            writer.WriteLine($"#{ShapeRepresentation}=ADVANCED_BREP_SHAPE_REPRESENTATION('',(#{WorldPlacement},#{Solid}),#{GeometricContext});");

            // Units are declared rather than left implicit so the importer never has to guess the scale
            writer.WriteLine($"#{GeometricContext}=(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{Uncertainty}))GLOBAL_UNIT_ASSIGNED_CONTEXT((#{MillimetreUnit},#{RadianUnit},#{SteradianUnit}))REPRESENTATION_CONTEXT('',''));");
            writer.WriteLine($"#{MillimetreUnit}=(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.));");
            writer.WriteLine($"#{RadianUnit}=(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.));");
            writer.WriteLine($"#{SteradianUnit}=(NAMED_UNIT(*)SOLID_ANGLE_UNIT()SI_UNIT($,.STERADIAN.));");
            writer.WriteLine($"#{Uncertainty}=UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE({Real(1e-6)}),#{MillimetreUnit},'distance_accuracy_value','confusion accuracy');");

            WritePoint(WorldOrigin, default);
            writer.WriteLine($"#{WorldPlacement}=AXIS2_PLACEMENT_3D('',#{WorldOrigin},{Direction(0, 0, 1)},{Direction(1, 0, 0)});");
        }

        private void WriteVertices()
        {
            Vec3[] topCorners = Corners();

            _topVertices = new int[4];
            _bottomVertices = new int[4];

            for (int corner = 0; corner < 4; corner++)
            {
                _topVertices[corner] = WriteVertex(topCorners[corner]);
            }

            for (int corner = 0; corner < 4; corner++)
            {
                Vec3 top = topCorners[corner];
                _bottomVertices[corner] = WriteVertex(new Vec3(top.X, top.Y, bottomZ));
            }
        }

        /// The four surface corners. A clamped fit interpolates its corner control points exactly,
        /// so these are the surface's own corners and the vertices sit on it.
        private Vec3[] Corners()
            =>
            [
                surface.ControlPoints[0, 0],
                surface.ControlPoints[_uCount - 1, 0],
                surface.ControlPoints[_uCount - 1, _vCount - 1],
                surface.ControlPoints[0, _vCount - 1],
            ];

        private void WriteEdges()
        {
            // Top edges run along the surface's own boundary isocurves, so the skirt faces below
            // meet the surface exactly rather than within a tolerance
            _topEdges =
            [
                WriteBoundaryEdge(surface.VBoundaryControlPoints(atEnd: false), surface.UKnots, _uCount, _topVertices[0], _topVertices[1]),
                WriteBoundaryEdge(surface.UBoundaryControlPoints(atEnd: true), surface.VKnots, _vCount, _topVertices[1], _topVertices[2]),
                WriteBoundaryEdge(surface.VBoundaryControlPoints(atEnd: true), surface.UKnots, _uCount, _topVertices[3], _topVertices[2]),
                WriteBoundaryEdge(surface.UBoundaryControlPoints(atEnd: false), surface.VKnots, _vCount, _topVertices[0], _topVertices[3]),
            ];

            Vec3[] corners = Corners();

            _verticalEdges = new int[4];
            for (int corner = 0; corner < 4; corner++)
            {
                Vec3 top = corners[corner];
                _verticalEdges[corner] = WriteLineEdge(
                    new Vec3(top.X, top.Y, bottomZ),
                    top,
                    _bottomVertices[corner],
                    _topVertices[corner]);
            }

            // Each bottom edge runs in the same direction as the top edge it pairs with across a
            // skirt face, so sides 2 and 3 run against the corner cycle
            (int From, int To)[] bottomSpans = [(0, 1), (1, 2), (3, 2), (0, 3)];

            _bottomEdges = new int[4];
            for (int side = 0; side < 4; side++)
            {
                (int from, int to) = bottomSpans[side];

                _bottomEdges[side] = WriteLineEdge(
                    new Vec3(corners[from].X, corners[from].Y, bottomZ),
                    new Vec3(corners[to].X, corners[to].Y, bottomZ),
                    _bottomVertices[from],
                    _bottomVertices[to]);
            }
        }

        private void WriteFaces()
        {
            int topFace = WriteBSplineFace();
            int bottomFace = WriteBottomFace();

            int[] skirtFaces =
            [
                WriteSkirtFace(0, Direction(0, -1, 0), Direction(1, 0, 0)),
                WriteSkirtFace(1, Direction(1, 0, 0), Direction(0, 1, 0)),
                WriteSkirtFace(2, Direction(0, 1, 0), Direction(0, 0, 1)),
                WriteSkirtFace(3, Direction(-1, 0, 0), Direction(0, 1, 0)),
            ];

            writer.WriteLine(
                $"#{Shell}=CLOSED_SHELL('',(#{topFace},#{bottomFace},#{string.Join(",#", skirtFaces)}));");
            writer.WriteLine($"#{Solid}=MANIFOLD_SOLID_BREP('{Escape(productName)}',#{Shell});");
        }

        /// The lens surface. Its parametrisation runs u along x and v along y, so the surface
        /// normal already points away from the solid and the face agrees with its sense.
        private int WriteBSplineFace()
        {
            int loop = WriteEdgeLoop(
            [
                (_topEdges[0], true),
                (_topEdges[1], true),
                (_topEdges[2], false),
                (_topEdges[3], false),
            ]);

            return WriteAdvancedFace(loop, _surfaceId);
        }

        private int WriteBottomFace()
        {
            int planeId = WritePlane(
                new Vec3(0, 0, bottomZ),
                Direction(0, 0, -1),
                Direction(0, 1, 0));

            int loop = WriteEdgeLoop(
            [
                (_bottomEdges[3], true),
                (_bottomEdges[2], true),
                (_bottomEdges[1], false),
                (_bottomEdges[0], false),
            ]);

            return WriteAdvancedFace(loop, planeId);
        }

        /// One side of the skirt: the surface's boundary curve on top, the back face's straight
        /// edge below, and a vertical line at each end. PinBoundariesToRectangle has put the
        /// boundary control row exactly on this plane, so the curve lies in it.
        private int WriteSkirtFace(int side, string outwardNormal, string reference)
        {
            Vec3[] corners = Corners();
            int next = NextCorner[side];

            int planeId = WritePlane(
                new Vec3(corners[side].X, corners[side].Y, bottomZ),
                outwardNormal,
                reference);

            // Sides 0 and 1 traverse the rectangle forwards, sides 2 and 3 backwards, so that every
            // edge is used once in each direction across the whole shell
            (int Edge, bool Forward)[] loop = side is 0 or 1
                ?
                [
                    (_bottomEdges[side], true),
                    (_verticalEdges[next], true),
                    (_topEdges[side], false),
                    (_verticalEdges[side], false),
                ]
                :
                [
                    (_topEdges[side], true),
                    (_verticalEdges[side], false),
                    (_bottomEdges[side], false),
                    (_verticalEdges[next], true),
                ];

            return WriteAdvancedFace(WriteEdgeLoop(loop), planeId);
        }

        private int WriteAdvancedFace(int loop, int geometry)
        {
            int bound = _nextId++;
            writer.WriteLine($"#{bound}=FACE_OUTER_BOUND('',#{loop},.T.);");

            int face = _nextId++;
            writer.WriteLine($"#{face}=ADVANCED_FACE('',(#{bound}),#{geometry},.T.);");

            return face;
        }

        private int WriteEdgeLoop((int Edge, bool Forward)[] edges)
        {
            int[] oriented = new int[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                oriented[i] = _nextId++;
                writer.WriteLine(
                    $"#{oriented[i]}=ORIENTED_EDGE('',*,*,#{edges[i].Edge},{(edges[i].Forward ? ".T." : ".F.")});");
            }

            int loop = _nextId++;
            writer.WriteLine($"#{loop}=EDGE_LOOP('',(#{string.Join(",#", oriented)}));");

            return loop;
        }

        private int WritePlane(Vec3 origin, string normal, string reference)
        {
            int originId = _nextId++;
            WritePoint(originId, origin);

            int placement = _nextId++;
            writer.WriteLine($"#{placement}=AXIS2_PLACEMENT_3D('',#{originId},{normal},{reference});");

            int plane = _nextId++;
            writer.WriteLine($"#{plane}=PLANE('',#{placement});");

            return plane;
        }

        /// Emits the boundary isocurve as a B_SPLINE_CURVE_WITH_KNOTS over the same control points
        /// and knots the surface uses along that boundary, which is what makes the shell watertight
        /// by construction rather than by tolerance.
        private int WriteBoundaryEdge(Vec3[] controlPoints, double[] knots, int count, int startVertex, int endVertex)
        {
            int[] pointIds = new int[controlPoints.Length];
            for (int i = 0; i < controlPoints.Length; i++)
            {
                pointIds[i] = _nextId++;
                WritePoint(pointIds[i], controlPoints[i]);
            }

            int curve = _nextId++;
            writer.Write(string.Create(CultureInfo.InvariantCulture, $"#{curve}=B_SPLINE_CURVE_WITH_KNOTS('',{Degree},(#{string.Join(",#", pointIds)}),.UNSPECIFIED.,.F.,.F.,"));
            WriteMultiplicities(count);
            writer.Write(',');
            WriteDistinctKnots(knots, count);
            writer.WriteLine(",.UNSPECIFIED.);");

            return WriteEdgeCurve(curve, startVertex, endVertex);
        }

        private int WriteLineEdge(Vec3 from, Vec3 to, int startVertex, int endVertex)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            int originId = _nextId++;
            WritePoint(originId, from);

            int vector = _nextId++;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{vector}=VECTOR('',{Direction(dx / length, dy / length, dz / length)},{Real(length)});"));

            int line = _nextId++;
            writer.WriteLine($"#{line}=LINE('',#{originId},#{vector});");

            return WriteEdgeCurve(line, startVertex, endVertex);
        }

        /// Every edge is built so that its start-to-end direction matches its curve's
        /// parametrisation, which is why same_sense is always true here.
        private int WriteEdgeCurve(int geometry, int startVertex, int endVertex)
        {
            int edge = _nextId++;
            writer.WriteLine($"#{edge}=EDGE_CURVE('',#{startVertex},#{endVertex},#{geometry},.T.);");

            return edge;
        }

        private int WriteVertex(Vec3 point)
        {
            int pointId = _nextId++;
            WritePoint(pointId, point);

            int vertex = _nextId++;
            writer.WriteLine($"#{vertex}=VERTEX_POINT('',#{pointId});");

            return vertex;
        }

        private void WritePoint(int id, Vec3 point)
            => writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{id}=CARTESIAN_POINT('',({Real(point.X)},{Real(point.Y)},{Real(point.Z)}));"));

        private string Direction(double x, double y, double z)
        {
            int id = _nextId++;
            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"#{id}=DIRECTION('',({Real(x)},{Real(y)},{Real(z)}));"));

            return $"#{id}";
        }

        private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        /// STEP reals must carry a decimal point, which neither "G" formatting nor exponent
        /// notation guarantees on its own.
        private static string Real(double value)
        {
            string text = value.ToString("G15", CultureInfo.InvariantCulture);

            int exponent = text.IndexOf('E', StringComparison.Ordinal);
            if (exponent < 0)
            {
                return text.Contains('.', StringComparison.Ordinal) ? text : text + ".";
            }

            return text.AsSpan(0, exponent).Contains('.')
                ? text
                : string.Concat(text.AsSpan(0, exponent), ".", text.AsSpan(exponent));
        }
    }
}
