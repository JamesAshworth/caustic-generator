namespace CausticsEngineering.Geometry;

public static class MeshBuilder
{
    /// A square mesh of width * height nodes, with node coordinates running 1..width and 1..height.
    public static Mesh Square(int width, int height)
    {
        Point3D[] nodes = new Point3D[width * height];
        Point3D[,] nodeArray = new Point3D[width, height];

        int count = 0;
        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                Point3D point = new(x, y, 0, x, y);
                nodes[count++] = point;
                nodeArray[x - 1, y - 1] = point;
            }
        }

        Triangle[] triangles = new Triangle[(width - 1) * (height - 1) * 2];
        count = 0;
        for (int y = 1; y <= height - 1; y++)
        {
            for (int x = 1; x <= width - 1; x++)
            {
                // x and y establish the column of squares we are in
                int indexUpperLeft = (y - 1) * width + x;
                int indexUpperRight = indexUpperLeft + 1;

                int indexLowerLeft = y * width + x;
                int indexLowerRight = indexLowerLeft + 1;

                triangles[count++] = new Triangle(indexUpperLeft, indexLowerLeft, indexUpperRight);
                triangles[count++] = new Triangle(indexLowerRight, indexUpperRight, indexLowerLeft);
            }
        }

        return new Mesh(nodes, nodeArray, triangles, width, height);
    }

    /// Lifts a scalar field onto a flat square mesh as node heights.
    public static Mesh FromMatrix(double[,] matrix)
    {
        int width = matrix.GetLength(0);
        int height = matrix.GetLength(1);

        Mesh mesh = Square(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mesh.NodeArray[x, y].Z = matrix[x, y];
            }
        }

        return mesh;
    }

    /// Closes an open surface into a printable solid: a flat bottom at bottomZ, the input
    /// surface on top, and skirt triangles joining the two around all four edges.
    /// The bottom is built from the undistorted node grid, so the back face stays a clean
    /// rectangle however far the top surface's nodes have marched. gridScale converts a grid
    /// step into the units the input surface is already in.
    public static Mesh Solidify(Mesh inputMesh, double bottomZ, double gridScale = 1.0)
    {
        int width = inputMesh.Width;
        int height = inputMesh.Height;
        int totalNodes = width * height * 2;
        int halfNodes = totalNodes / 2;

        Point3D[] nodes = new Point3D[totalNodes];
        Point3D[,] nodeArrayTop = new Point3D[width, height];
        Point3D[,] nodeArrayBottom = new Point3D[width, height];

        int edgeNodes = width * 2 + (height - 2) * 2;
        int trianglesPerSurface = (width - 1) * (height - 1) * 2;
        int totalTriangles = trianglesPerSurface * 2 + edgeNodes * 2;

        int count = 0;
        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                Point3D point = new((x - 1) * gridScale, (y - 1) * gridScale, bottomZ, x, y);
                nodes[count++] = point;
                nodeArrayBottom[x - 1, y - 1] = point;
            }
        }

        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                Point3D node = inputMesh.NodeArray[x - 1, y - 1];
                Point3D copied = new(node.X, node.Y, node.Z, node.Ix, node.Iy);
                nodes[count++] = copied;
                nodeArrayTop[x - 1, y - 1] = copied;
            }
        }

        Triangle[] triangles = new Triangle[totalTriangles];
        count = 0;

        // Bottom surface, wound so its normals face away from the top surface
        for (int y = 1; y <= height - 1; y++)
        {
            for (int x = 1; x <= width - 1; x++)
            {
                int indexUpperLeft = (y - 1) * width + x;
                int indexLowerLeft = y * width + x;

                triangles[count++] = new Triangle(indexUpperLeft, indexLowerLeft, indexUpperLeft + 1);
                triangles[count++] = new Triangle(indexLowerLeft + 1, indexUpperLeft + 1, indexLowerLeft);
            }
        }

        // Top surface
        for (int y = 1; y <= height - 1; y++)
        {
            for (int x = 1; x <= width - 1; x++)
            {
                int indexUpperLeft = (y - 1) * width + x + halfNodes;
                int indexLowerLeft = y * width + x + halfNodes;

                triangles[count++] = new Triangle(indexUpperLeft, indexUpperLeft + 1, indexLowerLeft);
                triangles[count++] = new Triangle(indexLowerLeft + 1, indexLowerLeft, indexUpperLeft + 1);
            }
        }

        // Left edge
        for (int y = 1; y <= height - 1; y++)
        {
            int lowerLeft = (y - 1) * width + 1;
            int upperLeft = lowerLeft + halfNodes;
            int lowerRight = y * width + 1;
            int upperRight = lowerRight + halfNodes;

            triangles[count++] = new Triangle(lowerLeft, upperLeft, upperRight);
            triangles[count++] = new Triangle(upperRight, lowerRight, lowerLeft);
        }

        // Right edge
        for (int y = 1; y <= height - 1; y++)
        {
            int lowerLeft = (y - 1) * width + width;
            int upperLeft = lowerLeft + halfNodes;
            int lowerRight = y * width + width;
            int upperRight = lowerRight + halfNodes;

            triangles[count++] = new Triangle(lowerLeft, upperRight, upperLeft);
            triangles[count++] = new Triangle(upperRight, lowerLeft, lowerRight);
        }

        // Top edge
        for (int x = 2; x <= width; x++)
        {
            int lowerLeft = x;
            int upperLeft = lowerLeft + halfNodes;
            int lowerRight = x - 1;
            int upperRight = lowerRight + halfNodes;

            triangles[count++] = new Triangle(lowerLeft, upperLeft, upperRight);
            triangles[count++] = new Triangle(upperRight, lowerRight, lowerLeft);
        }

        // Bottom edge
        for (int x = 2; x <= width; x++)
        {
            int lowerLeft = (height - 1) * width + x;
            int upperLeft = lowerLeft + halfNodes;
            int lowerRight = (height - 1) * width + (x - 1);
            int upperRight = lowerRight + halfNodes;

            triangles[count++] = new Triangle(lowerLeft, upperRight, upperLeft);
            triangles[count++] = new Triangle(upperRight, lowerLeft, lowerRight);
        }

        // NodeArray is the bottom surface, matching the reference implementation: the solid is
        // only ever saved to OBJ, and the save path walks Nodes rather than NodeArray.
        return new Mesh(nodes, nodeArrayBottom, triangles, width, height);
    }
}
