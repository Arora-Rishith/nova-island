namespace NovaIsland.Services;

internal static class IslandBounds
{
    internal static bool Contains(double x, double y, double width, double height, double radius)
    {
        if (x < 0 || y < 0 || x > width || y > height) return false;
        double dx = x - Math.Clamp(x, radius, width - radius);
        double dy = y - Math.Clamp(y, radius, height - radius);
        return dx * dx + dy * dy <= radius * radius;
    }
}
