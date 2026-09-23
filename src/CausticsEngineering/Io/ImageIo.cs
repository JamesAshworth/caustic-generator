using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CausticsEngineering.Io;

public static class ImageIo
{
    /// Loads an image as a greyscale field indexed [x, y] with values in 0..1.
    /// Rec. 601 luma weights match the greyscale conversion used by the reference implementation.
    public static double[,] LoadGreyscale(string filename)
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(filename);

        double[,] field = new double[image.Width, image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgb24 pixel = row[x];
                    field[x, y] = (0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B) / 255.0;
                }
            }
        });

        return field;
    }

    /// Renders a signed field as an image: positive values blue, negative values red.
    public static void SaveSignedField(double[,] field, string filename)
    {
        int width = field.GetLength(0);
        int height = field.GetLength(1);

        using Image<Rgb24> image = new(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                double value = field[x, y];
                byte red = ToByte(value < 0 ? -value : 0);
                byte blue = ToByte(value > 0 ? value : 0);
                image[x, y] = new Rgb24(red, 0, blue);
            }
        }

        image.Save(filename);
    }

    private static byte ToByte(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
