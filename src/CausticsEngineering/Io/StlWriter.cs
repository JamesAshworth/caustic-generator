using CausticsEngineering.Geometry;

namespace CausticsEngineering.Io;

public static class StlWriter
{
    private const int HeaderBytes = 80;

    /// Writes the mesh as binary STL. STL carries no vertex sharing and no grid dimensions, so each
    /// triangle is emitted standalone with its own facet normal, computed from the winding order.
    public static void Save(
        Mesh mesh,
        string filename,
        double scale = 1.0,
        double scaleZ = 1.0,
        bool reverse = false,
        bool flipXy = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        using FileStream stream = File.Create(filename);
        using BinaryWriter writer = new(stream);

        writer.Write(new byte[HeaderBytes]);
        writer.Write((uint)mesh.Triangles.Length);

        foreach (Triangle face in mesh.Triangles)
        {
            (int i1, int i2, int i3) = reverse
                ? (face.Pt3, face.Pt2, face.Pt1)
                : (face.Pt1, face.Pt2, face.Pt3);

            (float X, float Y, float Z) v1 = Transform(mesh.Node(i1), scale, scaleZ, flipXy);
            (float X, float Y, float Z) v2 = Transform(mesh.Node(i2), scale, scaleZ, flipXy);
            (float X, float Y, float Z) v3 = Transform(mesh.Node(i3), scale, scaleZ, flipXy);

            WriteVertex(writer, Normal(v1, v2, v3));
            WriteVertex(writer, v1);
            WriteVertex(writer, v2);
            WriteVertex(writer, v3);

            // Attribute byte count, unused
            writer.Write((ushort)0);
        }
    }

    private static (float X, float Y, float Z) Transform(Point3D vertex, double scale, double scaleZ, bool flipXy)
    {
        (double first, double second) = flipXy
            ? (vertex.Y * scale, vertex.X * scale)
            : (vertex.X * scale, vertex.Y * scale);

        return ((float)first, (float)second, (float)(vertex.Z * scaleZ));
    }

    /// Unit normal of the triangle, or zero for a degenerate facet — slicers accept a zero normal
    /// and recompute it from the winding.
    private static (float X, float Y, float Z) Normal(
        (float X, float Y, float Z) v1,
        (float X, float Y, float Z) v2,
        (float X, float Y, float Z) v3)
    {
        double ax = v2.X - v1.X;
        double ay = v2.Y - v1.Y;
        double az = v2.Z - v1.Z;

        double bx = v3.X - v1.X;
        double by = v3.Y - v1.Y;
        double bz = v3.Z - v1.Z;

        double nx = ay * bz - az * by;
        double ny = az * bx - ax * bz;
        double nz = ax * by - ay * bx;

        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (length == 0)
        {
            return (0, 0, 0);
        }

        return ((float)(nx / length), (float)(ny / length), (float)(nz / length));
    }

    private static void WriteVertex(BinaryWriter writer, (float X, float Y, float Z) vertex)
    {
        writer.Write(vertex.X);
        writer.Write(vertex.Y);
        writer.Write(vertex.Z);
    }
}
