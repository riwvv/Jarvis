using Windows.Devices.Geolocation;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Net.Http;
using Jarvis.Models;

namespace Jarvis.Plugins;

public class WeatherPlugin {
    private static readonly CultureInfo _invariantCulture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;

    public WeatherPlugin(IHttpClientFactory factory) {
        _httpClient = factory.CreateClient("Open-Meteo");
    }

    [KernelFunction]
    [Description("Поиск погоды в конкретном городе")]
    public async Task<string> GetWeatherInCity([Description("Название города")] string cityName) {
        try {
            string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1&language=ru&format=json";
            var geoResponse = await _httpClient.GetAsync(geoUrl);

            string geoJson = await geoResponse.Content.ReadAsStringAsync();
            var geoData = JsonSerializer.Deserialize<GeoResponse>(geoJson, options);

            if (geoData?.results == null || geoData.results.Count == 0)
                return $"Город '{cityName}' не найден.";

            var bestMatch = geoData.results[0];

            if (bestMatch.latitude == 0 && bestMatch.longitude == 0)
                return $"Не удалось определить координаты города '{cityName}'";

            var lat = bestMatch.latitude.ToString(_invariantCulture);
            var lon = bestMatch.longitude.ToString(_invariantCulture);
            var foundName = bestMatch.name;

            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true&timezone=auto";
            var weatherResponse = await _httpClient.GetAsync(weatherUrl);

            string weatherJson = await weatherResponse.Content.ReadAsStringAsync();
            var weatherData = JsonSerializer.Deserialize<WeatherResponse>(weatherJson, options);

            if (weatherData == null || weatherData.current_weather == null)
                return $"Погода для города {cityName} не найдена системой. API вернул: {weatherJson}";

            var temp = weatherData.current_weather.temperature;
            var weatherCode = weatherData.current_weather.weathercode;
            var description = GetWeatherDescription(weatherCode);

            return $"Сейчас в городе {foundName} {temp:F0} градусов, {description}.";
        }
        catch (JsonException ex) {
            return $"Ошибка обработки данных погоды: {ex.Message}";
        }
        catch (Exception ex) {
            return $"Не удалось получить погоду. Ошибка: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Погода по текущей геолокации, когда название города не указано")]
    public async Task<string> GetWeatherInCurrentLocation() {
        try {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
                return "Доступ к геолокации запрещён. Включите его в настройках Windows.";

            var geolocator = new Geolocator {
                DesiredAccuracy = PositionAccuracy.High,
                ReportInterval = 2000
            };

            var position = await geolocator.GetGeopositionAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));

            var lat = position.Coordinate.Point.Position.Latitude;
            var lon = position.Coordinate.Point.Position.Longitude;

            return await GetWeatherByCoordinates(lat, lon);
        }
        catch (UnauthorizedAccessException) {
            return "Нет доступа к геолокации. Разрешите доступ в настройках Windows.";
        }
        catch (Exception ex) {
            return $"Не удалось получить погоду: {ex.Message}";
        }
    }

    private async Task<string> GetWeatherByCoordinates(double lat, double lon) {
        try {
            var latStr = lat.ToString(_invariantCulture);
            var lonStr = lon.ToString(_invariantCulture);

            string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current_weather=true&timezone=auto";
            var weatherResponse = await _httpClient.GetAsync(weatherUrl);
            string weatherJson = await weatherResponse.Content.ReadAsStringAsync();

            var weatherData = JsonSerializer.Deserialize<WeatherResponse>(weatherJson, options);

            if (weatherData?.current_weather == null)
                return $"Не удалось получить погоду по вашему местоположению.";

            var temp = weatherData.current_weather.temperature;
            var weatherCode = weatherData.current_weather.weathercode;
            var description = GetWeatherDescription(weatherCode);

            return $"Сейчас в вашем районе {temp:F0} градусов, {description}.";
        }
        catch (HttpRequestException) {
            return "Не удалось подключиться к сервису погоды. Проверьте интернет-соединение.";
        }
        catch (JsonException ex) {
            return $"Ошибка обработки данных: {ex.Message}";
        }
        catch (Exception ex) {
            return $"Не удалось получить погоду: {ex.Message}";
        }
    }

    private static string GetWeatherDescription(int weathercode) {
        return weathercode switch {
            0 => "ясно",
            1 => "преимущественно ясно",
            2 => "переменная облачность",
            3 => "пасмурно",
            45 => "туман",
            48 => "туман с изморозью",
            51 => "морось",
            53 => "умеренная морось",
            55 => "сильная морось",
            56 => "ледяная морось",
            57 => "сильная ледяная морось",
            61 => "дождь",
            63 => "умеренный дождь",
            65 => "сильный дождь",
            66 => "ледяной дождь",
            67 => "сильный ледяной дождь",
            71 => "снегопад",
            73 => "умеренный снегопад",
            75 => "сильный снегопад",
            77 => "снежная крупа",
            80 => "ливень",
            81 => "умеренный ливень",
            82 => "сильный ливень",
            85 => "снегопад",
            86 => "сильный снегопад",
            95 => "гроза",
            96 => "гроза с градом",
            99 => "сильная гроза с градом",
            _ => "без осадков"
        };
    }
}
