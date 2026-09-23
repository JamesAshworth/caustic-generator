using System.Globalization;
using CausticsEngineering.Geometry;

namespace CausticsEngineering.Io;

/// Wavefront OBJ mesh IO. STL is the default output format — see `StlWriter` — but OBJ keeps
/// vertex sharing and the grid dimensions, so it round-trips back into a `Mesh` and STL does not.
public static class ObjWriter
{
    /// Writes the mesh in Wavefront OBJ format, with a trailing non-standard `dims` line
    /// recording the grid size so the mesh can be reloaded as a grid rather than a soup.
    public static void Save(
        Mesh mesh,
        string filename,
        double scale = 1.0,
        double scaleZ = 1.0,
        bool reverse = false,
        bool flipXy = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        using StreamWriter writer = new(filename);

        foreach (Point3D vertex in mesh.Nodes)
        {
            (double first, double second) = flipXy
                ? (vertex.Y * scale, vertex.X * scale)
                : (vertex.X * scale, vertex.Y * scale);

            writer.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"v {first} {second} {vertex.Z * scaleZ}"));
        }

        foreach (Triangle face in mesh.Triangles)
        {
            writer.WriteLine(reverse
                ? $"f {face.Pt3} {face.Pt2} {face.Pt1}"
                : $"f {face.Pt1} {face.Pt2} {face.Pt3}");
        }

        writer.WriteLine($"dims {mesh.Width} {mesh.Height}");
    }

    /// Reads back a mesh saved by Save, including its `dims` line.
    public static Mesh Load(string filename)
    {
        string[] lines = File.ReadAllLines(filename);

        List<Point3D> nodes = [];
        List<Triangle> triangles = [];
        int width = 0;
        int height = 0;

        foreach (string line in lines)
        {
            string[] elements = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (elements.Length == 0)
            {
                continue;
            }

            switch (elements[0])
            {
                case "v":
                    nodes.Add(new Point3D(
                        double.Parse(elements[1], CultureInfo.InvariantCulture),
                        double.Parse(elements[2], CultureInfo.InvariantCulture),
                        double.Parse(elements[3], CultureInfo.InvariantCulture) * 10,
                        0,
                        0));
                    break;

                case "f":
                    triangles.Add(new Triangle(
                        int.Parse(elements[1], CultureInfo.InvariantCulture),
                        int.Parse(elements[2], CultureInfo.InvariantCulture),
                        int.Parse(elements[3], CultureInfo.InvariantCulture)));
                    break;

                case "dims":
                    width = int.Parse(elements[1], CultureInfo.InvariantCulture);
                    height = int.Parse(elements[2], CultureInfo.InvariantCulture);
                    break;
            }
        }

        Point3D[,] nodeArray = new Point3D[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                nodeArray[x, y] = nodes[y * width + x];
            }
        }

        return new Mesh([.. nodes], nodeArray, [.. triangles], width, height);
    }
}
