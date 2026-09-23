using CausticsEngineering.Geometry;
using NUnit.Framework;

namespace CausticsEngineering.Tests;

[TestFixture]
public class GeometryTests
{
    [Test]
    public void TriangleArea_RightAngledTriangle_ReturnsHalfBaseTimesHeight()
    {
        // GIVEN a right-angled triangle with legs of 3 and 4
        Point3D p1 = new(0, 0, 0, 0, 0);
        Point3D p2 = new(3, 0, 0, 0, 0);
        Point3D p3 = new(0, 4, 0, 0, 0);

        // WHEN the area is calculated
        double area = Point3D.TriangleArea(p1, p2, p3);

        // THEN it is half base times height
        Assert.That(area, Is.EqualTo(6.0).Within(1e-9));
    }

    [Test]
    public void TriangleArea_IgnoresZ_BecauseAreaIsMeasuredInThePlane()
    {
        // GIVEN two triangles identical in XY but differing in Z
        Point3D[] flat = [new(0, 0, 0, 0, 0), new(2, 0, 0, 0, 0), new(0, 2, 0, 0, 0)];
        Point3D[] raised = [new(0, 0, 5, 0, 0), new(2, 0, -7, 0, 0), new(0, 2, 3, 0, 0)];

        // WHEN both areas are calculated
        double flatArea = Point3D.TriangleArea(flat[0], flat[1], flat[2]);
        double raisedArea = Point3D.TriangleArea(raised[0], raised[1], raised[2]);

        // THEN they agree
        Assert.That(raisedArea, Is.EqualTo(flatArea).Within(1e-9));
    }

    [Test]
    public void Distance_TwoPoints_IgnoresZ()
    {
        // GIVEN two points three and four apart in X and Y, and far apart in Z
        Point3D p1 = new(1, 1, 0, 0, 0);
        Point3D p2 = new(4, 5, 100, 0, 0);

        // WHEN the distance is measured
        double distance = Point3D.Distance(p1, p2);

        // THEN it is the planar distance
        Assert.That(distance, Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void Centroid_ThreePoints_AveragesEachAxis()
    {
        // GIVEN three points
        Point3D p1 = new(0, 0, 0, 0, 0);
        Point3D p2 = new(3, 0, 6, 0, 0);
        Point3D p3 = new(0, 3, 3, 0, 0);

        // WHEN the centroid is taken
        Point3D centroid = Point3D.Centroid(p1, p2, p3);

        // THEN each axis is the mean of that axis
        Assert.Multiple(() =>
        {
            Assert.That(centroid.X, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(centroid.Y, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(centroid.Z, Is.EqualTo(3.0).Within(1e-9));
        });
    }

    [Test]
    public void Midpoint_TwoPoints_LiesHalfway()
    {
        // GIVEN two points
        Point3D p1 = new(0, 0, 0, 0, 0);
        Point3D p2 = new(4, 8, 2, 0, 0);

        // WHEN the midpoint is taken
        Point3D midpoint = Point3D.Midpoint(p1, p2);

        // THEN it is halfway along each axis
        Assert.Multiple(() =>
        {
            Assert.That(midpoint.X, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(midpoint.Y, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(midpoint.Z, Is.EqualTo(1.0).Within(1e-9));
        });
    }
}
