using CausticsEngineering.Io;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CausticsEngineering.Tests;

[TestFixture]
public class ImageIoTests
{
    private string _file = string.Empty;

    [SetUp]
    public void SetUp() => _file = Path.Combine(Path.GetTempPath(), $"caustics-{Guid.NewGuid()}.png");

    [TearDown]
    public void TearDown() => File.Delete(_file);

    [Test]
    public void LoadGreyscale_NoResize_KeepsTheImageAtItsOwnSize()
    {
        // GIVEN a 40x20 image
        WriteImage(40, 20);

        // WHEN it is loaded without a resize
        double[,] field = ImageIo.LoadGreyscale(_file);

        // THEN the field matches the image's dimensions, indexed [x, y]
        Assert.Multiple(() =>
        {
            Assert.That(field.GetLength(0), Is.EqualTo(40));
            Assert.That(field.GetLength(1), Is.EqualTo(20));
        });
    }

    [Test]
    public void LoadGreyscale_ResizeOnARectangularImage_CapsTheLongEdgeAndPreservesAspect()
    {
        // GIVEN a 2:1 image larger than the cap
        WriteImage(64, 32);

        // WHEN it is loaded with a long edge of 16
        double[,] field = ImageIo.LoadGreyscale(_file, resizeTo: 16);

        // THEN the long edge is capped and the aspect ratio survives
        Assert.Multiple(() =>
        {
            Assert.That(field.GetLength(0), Is.EqualTo(16));
            Assert.That(field.GetLength(1), Is.EqualTo(8));
        });
    }

    [Test]
    public void LoadGreyscale_ResizeOnATallImage_CapsTheLongEdgeWhicheverAxisItIs()
    {
        // GIVEN a portrait image larger than the cap
        WriteImage(32, 64);

        // WHEN it is loaded with a long edge of 16
        double[,] field = ImageIo.LoadGreyscale(_file, resizeTo: 16);

        // THEN the height is what gets capped
        Assert.Multiple(() =>
        {
            Assert.That(field.GetLength(0), Is.EqualTo(8));
            Assert.That(field.GetLength(1), Is.EqualTo(16));
        });
    }

    [Test]
    public void LoadGreyscale_ImageAlreadyWithinTheCap_IsNotUpscaled()
    {
        // GIVEN an image smaller than the cap
        WriteImage(24, 12);

        // WHEN it is loaded with a long edge of 512
        double[,] field = ImageIo.LoadGreyscale(_file, resizeTo: 512);

        // THEN it is left alone rather than blown up
        Assert.Multiple(() =>
        {
            Assert.That(field.GetLength(0), Is.EqualTo(24));
            Assert.That(field.GetLength(1), Is.EqualTo(12));
        });
    }

    [Test]
    public void LoadGreyscale_ColouredImage_UsesRec601LumaAndNormalisesToZeroOne()
    {
        // GIVEN a single-pixel image of pure red
        using (Image<Rgb24> image = new(1, 1))
        {
            image[0, 0] = new Rgb24(255, 0, 0);
            image.Save(_file);
        }

        // WHEN it is loaded
        double[,] field = ImageIo.LoadGreyscale(_file);

        // THEN the value is red's Rec. 601 luma weight
        Assert.That(field[0, 0], Is.EqualTo(0.299).Within(1e-6));
    }

    [Test]
    public void LoadGreyscale_Image_IsIndexedByColumnThenRow()
    {
        // GIVEN a 2x1 image, black on the left and white on the right
        using (Image<Rgb24> image = new(2, 1))
        {
            image[0, 0] = new Rgb24(0, 0, 0);
            image[1, 0] = new Rgb24(255, 255, 255);
            image.Save(_file);
        }

        // WHEN it is loaded
        double[,] field = ImageIo.LoadGreyscale(_file);

        // THEN the first index is the column
        Assert.Multiple(() =>
        {
            Assert.That(field[0, 0], Is.EqualTo(0));
            Assert.That(field[1, 0], Is.EqualTo(1.0).Within(1e-6));
        });
    }

    private void WriteImage(int width, int height)
    {
        using Image<Rgb24> image = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = (byte)(x * 255 / Math.Max(1, width - 1));
                image[x, y] = new Rgb24(value, value, value);
            }
        }

        image.Save(_file);
    }
}
