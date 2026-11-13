using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaIsland;

public partial class MainWindow
{
    private void UpdateAlbumAccent(BitmapSource artwork)
    {
        try
        {
            var scaled = new TransformedBitmap(
                artwork,
                new ScaleTransform(24.0 / Math.Max(1, artwork.PixelWidth), 24.0 / Math.Max(1, artwork.PixelHeight)));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            var accent = Services.ArtworkPalette.Extract(pixels);
            albumAccentColor = Color.FromRgb(accent.R, accent.G, accent.B);
            albumAccentBrush = new SolidColorBrush(albumAccentColor);
            ApplyAlbumAccentToUi();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Album accent extraction: " + ex.Message);
            ResetAlbumAccent();
        }
    }

}
