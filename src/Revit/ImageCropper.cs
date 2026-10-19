using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace EgRoomTracker.Revit;

/// <summary>
/// Recadrage des marges blanches d'une image de pièce, comme <c>ImageExporter.CropWhitespace</c> de l'application TRC :
/// les bords « proches du blanc » (chaque composante ≥ 255 − tolérance, tolérance 10) sont retirés, l'image est
/// réenregistrée en JPEG au même emplacement. Le balayage lit les lignes en bloc (<c>LockBits</c>) au lieu de
/// <c>GetPixel</c> : même résultat, bien plus rapide sur des images de 8 000 px.
/// <para>
/// <c>System.Drawing.Common</c> est fournie par Revit, pas par le bundle : si elle n'est pas chargeable, l'appel lève et
/// l'appelant garde l'image non recadrée (comportement de TRC en cas d'échec).
/// </para>
/// </summary>
internal static class ImageCropper
{
    public const int DefaultTolerance = 10;

    /// <summary>Recadre le fichier JPEG ; l'image entièrement blanche est laissée telle quelle. Retourne vrai si le fichier a été réécrit.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CropWhitespace(string path, int tolerance = DefaultTolerance)
    {
        Rectangle bounds;
        byte[] cropped;
        using (var source = new Bitmap(path))
        {
            if (FindContent(source, tolerance) is not { } content)
            {
                return false;
            }

            bounds = content;
            if (bounds.Width == source.Width && bounds.Height == source.Height)
            {
                return false;
            }

            using var clone = source.Clone(bounds, source.PixelFormat);
            using var stream = new MemoryStream();
            clone.Save(stream, ImageFormat.Jpeg);
            cropped = stream.ToArray();
        }

        // L'image source est libérée avant la réécriture du même fichier.
        File.WriteAllBytes(path, cropped);
        return true;
    }

    /// <summary>Plus petit rectangle contenant tous les pixels qui ne sont pas proches du blanc ; null si l'image est blanche.</summary>
    private static Rectangle? FindContent(Bitmap bitmap, int tolerance)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        int left = width, top = height, right = -1, bottom = -1;
        var limit = (byte)(255 - tolerance);

        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 3; // BGR
                    if (row[offset] >= limit && row[offset + 1] >= limit && row[offset + 2] >= limit)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = y;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return right < left || bottom < top ? null : new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
}
