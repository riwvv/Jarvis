using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Diagnostics;

namespace Jarvis.Plugins;

public class BrowserPlugin
{
    private readonly Dictionary<string, string> _popularServices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://youtube.com",
        ["ютуб"] = "https://youtube.com",
        ["github"] = "https://github.com",
        ["гитхаб"] = "https://github.com",
        ["gmail"] = "https://gmail.com",
        ["почта"] = "https://gmail.com",
        ["mail"] = "https://mail.google.com",
        ["maps"] = "https://maps.google.com",
        ["карты"] = "https://maps.google.com",
        ["google карты"] = "https://maps.google.com",
        ["яндекс карты"] = "https://yandex.ru/maps",
        ["яндекс"] = "https://yandex.ru",
        ["yandex"] = "https://yandex.ru",
        ["translate"] = "https://translate.google.com",
        ["переводчик"] = "https://translate.google.com",
        ["drive"] = "https://drive.google.com",
        ["диск"] = "https://drive.google.com",
        ["google диск"] = "https://drive.google.com",
        ["яндекс диск"] = "https://disk.yandex.ru",
        ["docs"] = "https://docs.google.com",
        ["документы"] = "https://docs.google.com",
        ["sheets"] = "https://sheets.google.com",
        ["таблицы"] = "https://sheets.google.com",
        ["calendar"] = "https://calendar.google.com",
        ["календарь"] = "https://calendar.google.com",
        ["whatsapp"] = "https://web.whatsapp.com",
        ["ватсап"] = "https://web.whatsapp.com",
        ["telegram"] = "https://web.telegram.org",
        ["телеграм"] = "https://web.telegram.org",
        ["vk"] = "https://vk.com",
        ["вк"] = "https://vk.com",
        ["instagram"] = "https://instagram.com",
        ["инстаграм"] = "https://instagram.com",
        ["twitch"] = "https://twitch.tv",
        ["твич"] = "https://twitch.tv",
        ["spotify"] = "https://open.spotify.com",
        ["спотифай"] = "https://open.spotify.com",
        ["netflix"] = "https://netflix.com",
        ["нетфликс"] = "https://netflix.com",
        ["kinopoisk"] = "https://hd.kinopoisk.ru",
        ["кинопоиск"] = "https://hd.kinopoisk.ru",
        ["ozon"] = "https://ozon.ru",
        ["озон"] = "https://ozon.ru",
        ["wildberries"] = "https://wildberries.ru",
        ["вайлдберриз"] = "https://wildberries.ru",
        ["вб"] = "https://wildberries.ru"
    };

    [KernelFunction]
    [Description("Ищет информацию в Google и открывает результаты в браузере")]
    public string SearchGoogle(
        [Description("Поисковый запрос или URL")] string query)
    {
        try
        {
            query = query.Trim();

            // Если это уже URL - открываем напрямую
            if (Uri.IsWellFormedUriString(query, UriKind.Absolute))
            {
                OpenUrl(query);
                return $"Открыт URL: {query}";
            }

            // Иначе ищем в Google
            string encodedQuery = Uri.EscapeDataString(query);
            string searchUrl = $"https://www.google.com/search?q={encodedQuery}";

            OpenUrl(searchUrl);
            return $"Выполнен поиск Google: \"{query}\"";
        }
        catch (Exception ex)
        {
            return $"Ошибка при поиске: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает популярный сервис (YouTube, GitHub, Gmail, Maps, VK, Telegram и др.)")]
    public string OpenService(
        [Description("Название сервиса (например: youtube, gmail, github, vk, telegram, whatsapp)")] string serviceName)
    {
        try
        {
            serviceName = serviceName.Trim().ToLower();

            // Проверяем есть ли сервис в словаре
            if (_popularServices.TryGetValue(serviceName, out string? url))
            {
                OpenUrl(url);
                return $"Открыт сервис: {serviceName} ({url})";
            }

            // Если сервис не найден - ищем в Google
            return SearchGoogle(serviceName);
        }
        catch (Exception ex)
        {
            return $"Ошибка при открытии сервиса: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Открывает указанный URL в браузере")]
    public string OpenUrl(
        [Description("Полный URL адрес (например: https://github.com)")] string url)
    {
        try
        {
            url = url.Trim();

            // Добавляем https:// если нет протокола
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            return $"Открыт URL: {url}";
        }
        catch (Exception ex)
        {
            return $"Ошибка при открытии URL: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Ищет видео на YouTube")]
    public string SearchYouTube(
        [Description("Поисковый запрос для YouTube")] string query)
    {
        try
        {
            string encodedQuery = Uri.EscapeDataString(query.Trim());
            string url = $"https://www.youtube.com/results?search_query={encodedQuery}";

            OpenUrl(url);
            return $"Поиск YouTube: \"{query}\"";
        }
        catch (Exception ex)
        {
            return $"Ошибка при поиске на YouTube: {ex.Message}";
        }
    }

}

