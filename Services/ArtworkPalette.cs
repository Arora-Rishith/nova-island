namespace NovaIsland.Services;

internal static class ArtworkPalette
{
    // Select a dominant hue rather than averaging opposing colours into grey.
    // Input is the small, decoded BGRA thumbnail; transparent/neutral pixels
    // should not drown out the album's characteristic colour.
    internal static (byte R, byte G, byte B) Extract(ReadOnlySpan<byte> pixels)
    {
        var bins = new (double R, double G, double B, double Weight)[12];
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            double alpha = pixels[i + 3] / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            if (alpha < .2 || max < 20 || delta < 18) continue;
            double hue = max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            hue = (hue * 60 + 360) % 360;
            int bin = (int)Math.Round(hue / 30) % bins.Length;
            double saturation = delta / max;
            double weight = alpha * saturation * saturation * Math.Sqrt(max / 255);
            bins[bin].R += r * weight;
            bins[bin].G += g * weight;
            bins[bin].B += b * weight;
            bins[bin].Weight += weight;
        }
        var winner = bins.MaxBy(bin => bin.Weight);
        if (winner.Weight <= 0) return (225, 225, 235);
        double red = winner.R / winner.Weight, green = winner.G / winner.Weight, blue = winner.B / winner.Weight;
        double lift = Math.Max(1, 160 / Math.Max(red, Math.Max(green, blue)));
        return ((byte)Math.Clamp(Math.Round(red * lift), 0, 255),
            (byte)Math.Clamp(Math.Round(green * lift), 0, 255),
            (byte)Math.Clamp(Math.Round(blue * lift), 0, 255));
    }
}
