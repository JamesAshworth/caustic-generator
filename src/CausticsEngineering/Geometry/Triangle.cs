namespace CausticsEngineering.Geometry;

/// Vertex indices are 1-based to match the Wavefront OBJ face convention used on save.
public readonly record struct Triangle(int Pt1, int Pt2, int Pt3);
