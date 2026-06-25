using Windows.Devices.Geolocation;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Net.Http;
using Jarvis.Models;

namespace Jarvis.Plugins;

public class WeatherPlugin(IHttpClientFactory factory) {
    private static readonly CultureInfo _invariantCulture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions _options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient = factory.CreateClient("Open-Meteo");

    [KernelFunction]
    [Description("Поиск погоды в конкретном городе")]
    public async Task<string> GetWeatherInCity([Description("Название города")] string cityName) {
        var coords = await GetCoordinatesByCityName(cityName);
        if (!coords.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = cityName,
                description = $"Город '{cityName}' не найден"
            });
        }

        var weather = await GetCurrentWeatherByCoordinates(coords.Value.Lat, coords.Value.Lon);
        if (!weather.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "weather_api",
                description = $"Не удалось получить погоду для города {cityName}"
            });
        }

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Сейчас в городе {coords.Value.CityName} {weather.Value.Temp:F0} градусов, {weather.Value.Description}",
            city = coords.Value.CityName,
            temperature = weather.Value.Temp,
            description = weather.Value.Description
        });
    }

    [KernelFunction]
    [Description("Погода по текущей геолокации, когда название города не указано")]
    public async Task<string> GetWeatherAtCurrentLocation() {
        var coordsResult = await GetCurrentCoordinates();
        if (!coordsResult.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "geolocation",
                description = coordsResult?.ErrorMessage ?? "Не удалось определить местоположение"
            });
        }

        var (lat, lon, _) = coordsResult.Value;
        var weather = await GetCurrentWeatherByCoordinates(lat, lon);
        if (!weather.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "weather_api",
                description = "Не удалось получить погоду по вашему местоположению"
            });
        }

        var cityName = await ReverseGeocodeAsync(lat, lon) ?? "вашем районе";

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Сейчас в {cityName} {weather.Value.Temp:F0} градусов, {weather.Value.Description}",
            city = cityName,
            temperature = weather.Value.Temp,
            description = weather.Value.Description,
            latitude = lat,
            longitude = lon
        });
    }

    [KernelFunction]
    [Description("Прогноз погоды на завтра в указанном городе")]
    public async Task<string> GetTomorrowForecast([Description("Название города")] string cityName) {
        var coords = await GetCoordinatesByCityName(cityName);
        if (!coords.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = cityName,
                description = $"Город '{cityName}' не найден"
            });
        }

        var forecast = await GetDailyForecastByCoordinates(coords.Value.Lat, coords.Value.Lon, days: 2);
        if (!forecast.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "weather_api",
                description = $"Не удалось получить прогноз для города {cityName}"
            });
        }

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Завтра в городе {coords.Value.CityName} ожидается {forecast.Value.Description}, температура от {forecast.Value.TempMin:F0} до {forecast.Value.TempMax:F0} градусов",
            city = coords.Value.CityName,
            tempMin = forecast.Value.TempMin,
            tempMax = forecast.Value.TempMax,
            description = forecast.Value.Description
        });
    }

    [KernelFunction]
    [Description("Прогноз погоды на завтра по текущему местоположению")]
    public async Task<string> GetTomorrowForecastAtCurrentLocation() {
        var coordsResult = await GetCurrentCoordinates();
        if (!coordsResult.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "geolocation",
                description = coordsResult?.ErrorMessage ?? "Не удалось определить местоположение"
            });
        }

        var (lat, lon, _) = coordsResult.Value;
        var forecast = await GetDailyForecastByCoordinates(lat, lon, days: 2);
        if (!forecast.HasValue) {
            return JsonSerializer.Serialize(new {
                status = "ERROR",
                cause = "weather_api",
                description = "Не удалось получить прогноз для вашего местоположения"
            });
        }

        var cityName = await ReverseGeocodeAsync(lat, lon) ?? "вашем районе";

        return JsonSerializer.Serialize(new {
            status = "DONE",
            message = $"Завтра в {cityName} ожидается {forecast.Value.Description}, температура от {forecast.Value.TempMin:F0} до {forecast.Value.TempMax:F0} градусов",
            city = cityName,
            tempMin = forecast.Value.TempMin,
            tempMax = forecast.Value.TempMax,
            description = forecast.Value.Description,
            latitude = lat,
            longitude = lon
        });
    }

    private async Task<(double TempMax, double TempMin, string Description)?> GetDailyForecastByCoordinates(double lat, double lon, int days = 2) {
        try {
            var latStr = lat.ToString(_invariantCulture);
            var lonStr = lon.ToString(_invariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&daily=temperature_2m_max,temperature_2m_min,weathercode&timezone=auto&forecast_days={days}";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<DailyForecastResponse>(response, _options);

            if (data?.Daily == null || data.Daily.Time.Length < 2)
                return null;

            var tempMax = data.Daily.TemperatureMax[1];
            var tempMin = data.Daily.TemperatureMin[1];
            var weatherCode = data.Daily.WeatherCode[1];
            var description = GetWeatherDescription(weatherCode);

            return (tempMax, tempMin, description);
        }
        catch {
            return null;
        }
    }

    private async Task<(double Lat, double Lon, string CityName)?> GetCoordinatesByCityName(string cityName) {
        try {
            string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1&language=ru&format=json";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<GeoResponse>(response, _options);

            if (data?.results == null || data.results.Count == 0)
                return null;

            var match = data.results[0];
            return (match.latitude, match.longitude, match.name);
        }
        catch {
            return null;
        }
    }

    private async Task<string?> ReverseGeocodeAsync(double lat, double lon) {
        try {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?latitude={lat.ToString(_invariantCulture)}&longitude={lon.ToString(_invariantCulture)}&count=1&language=ru&format=json";
            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<GeoResponse>(response, _options);
            return data?.results?.FirstOrDefault()?.name;
        }
        catch {
            return null;
        }
    }

    private static async Task<(double Lat, double Lon, string? ErrorMessage)?> GetCurrentCoordinates() {
        try {
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
                return (0, 0, "Доступ к геолокации запрещён. Включите его в настройках Windows.");

            var geolocator = new Geolocator {
                DesiredAccuracy = PositionAccuracy.High,
                ReportInterval = 2000
            };

            var position = await geolocator.GetGeopositionAsync(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            return (position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude, null);
        }
        catch (UnauthorizedAccessException) {
            return (0, 0, "Нет доступа к геолокации. Разрешите доступ в настройках Windows.");
        }
        catch (Exception ex) {
            return (0, 0, $"Ошибка геолокации: {ex.Message}");
        }
    }

    private async Task<(double Temp, string Description)?> GetCurrentWeatherByCoordinates(double lat, double lon) {
        try {
            var latStr = lat.ToString(_invariantCulture);
            var lonStr = lon.ToString(_invariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current_weather=true&timezone=auto";

            var response = await _httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<WeatherResponse>(response, _options);

            if (data?.current_weather == null)
                return null;

            var temp = data.current_weather.temperature;
            var description = GetWeatherDescription(data.current_weather.weathercode);
            return (temp, description);
        }
        catch (HttpRequestException) {
            return null;
        }
        catch (JsonException) {
            return null;
        }
        catch {
            return null;
        }
    }

    private static string GetWeatherDescription(int weathercode) => weathercode switch {
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
