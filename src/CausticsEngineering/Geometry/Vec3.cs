namespace CausticsEngineering.Geometry;

/// Immutable coordinate triple. Point3D is a mutable node carrying its grid position, which suits
/// the marching solver but not the control nets and knot solves here.
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator *(double s, Vec3 a) => new(s * a.X, s * a.Y, s * a.Z);
}
