using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Windows.Devices.Geolocation;

namespace NovaIsland;

public partial class MainWindow
{
    private sealed record WeatherSnapshot(bool Available, string Temperature, string Icon, string Condition, DateTimeOffset Updated);

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("current")] public OpenMeteoCurrent? Current { get; init; }
    }

    private sealed class OpenMeteoCurrent
    {
        [JsonPropertyName("temperature_2m")] public double Temperature { get; init; }
        [JsonPropertyName("weather_code")] public int WeatherCode { get; init; }
    }

    private static readonly HttpClient WeatherClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer weatherTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private WeatherSnapshot weatherSnapshot = new(false, "--°", "cloud", "Weather unavailable", DateTimeOffset.MinValue);
    private bool weatherRefreshing;
    private double? weatherLatitude;
    private double? weatherLongitude;

    private void InitializeWeather()
    {
        weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        weatherTimer.Start();
        _ = RefreshWeatherAsync();
    }

    private async Task RefreshWeatherAsync()
    {
        if (weatherRefreshing || isClosed) return;
        weatherRefreshing = true;
        try
        {
            if (!weatherLatitude.HasValue || !weatherLongitude.HasValue)
            {
                (double Latitude, double Longitude)? location = await ResolveWeatherLocationAsync();
                if (location is null) return;
                weatherLatitude = location.Value.Latitude;
                weatherLongitude = location.Value.Longitude;
            }

            string latitude = weatherLatitude.Value.ToString("0.####", CultureInfo.InvariantCulture);
            string longitude = weatherLongitude.Value.ToString("0.####", CultureInfo.InvariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&timezone=auto";
            OpenMeteoResponse? response = await WeatherClient.GetFromJsonAsync<OpenMeteoResponse>(url);
            if (response?.Current is not { } current) return;

            var description = DescribeWeather(current.WeatherCode);
            weatherSnapshot = new(
                true,
                $"{Math.Round(current.Temperature):0}°",
                description.Icon,
                description.Condition,
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Weather refresh: " + ex.Message);
        }
        finally
        {
            weatherRefreshing = false;
            PublishWebState();
        }
    }

    private static async Task<(double Latitude, double Longitude)?> ResolveWeatherLocationAsync()
    {
        string? configuredLatitude = Environment.GetEnvironmentVariable("NOVA_WEATHER_LATITUDE");
        string? configuredLongitude = Environment.GetEnvironmentVariable("NOVA_WEATHER_LONGITUDE");
        if (double.TryParse(configuredLatitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) &&
            double.TryParse(configuredLongitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) &&
            latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
            return (latitude, longitude);

        try
        {
            GeolocationAccessStatus access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return null;
            var locator = new Geolocator { DesiredAccuracyInMeters = 1000 };
            Geoposition position = await locator.GetGeopositionAsync(
                TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            return (position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Weather location: " + ex.Message);
            return null;
        }
    }

    private static (string Icon, string Condition) DescribeWeather(int code) => code switch
    {
        0 => ("sun", "Clear"),
        1 or 2 or 3 => ("cloud", "Cloudy"),
        45 or 48 => ("cloud", "Foggy"),
        51 or 53 or 55 or 56 or 57 => ("rain", "Drizzle"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("rain", "Rain"),
        71 or 73 or 75 or 77 or 85 or 86 => ("snow", "Snow"),
        95 or 96 or 99 => ("storm", "Storm"),
        _ => ("cloud", "Weather")
    };
}
