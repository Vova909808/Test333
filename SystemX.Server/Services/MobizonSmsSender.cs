
using System.Net.Http;
using System.Text.Json;

namespace SystemX.Server.Services;

public class MobizonSmsSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    private const string BaseUrl = "https://api.mobizon.kz/service/";

    public MobizonSmsSender(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    private string? ApiKey =>
        _configuration["MOBIZON_API_KEY"];

    public async Task<(bool Success, string Message)> CheckConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return (false, "API-ключ Mobizon не настроен.");
        }

        try
        {
            string url =
                $"{BaseUrl}user/getOwnBalance" +
                $"?output=json&api=v1&apiKey={Uri.EscapeDataString(ApiKey)}";

            using var response = await _httpClient.GetAsync(
                url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "Mobizon вернул HTTP-ошибку.");
            }

            string content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            using var json = JsonDocument.Parse(content);

            int code = json.RootElement
                .GetProperty("code")
                .GetInt32();

            if (code != 0)
            {
                return (false, "Mobizon отклонил запрос. Проверь API-ключ и настройки доступа.");
            }

            return (true, "Подключение к Mobizon успешно.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Превышено время ожидания ответа Mobizon.");
        }
        catch (HttpRequestException)
        {
            return (false, "Не удалось установить соединение с Mobizon.");
        }
        catch (JsonException)
        {
            return (false, "Mobizon вернул некорректный ответ.");
        }
        catch (Exception)
        {
            return (false, "Ошибка проверки подключения Mobizon.");
        }
    }

    public async Task<(bool Success, string Message)> SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return (false, "API-ключ Mobizon не настроен.");
        }

        string recipient = new string(
            phoneNumber.Where(char.IsDigit).ToArray());

        if (recipient.Length < 10 || recipient.Length > 15)
        {
            return (false, "Некорректный номер телефона.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return (false, "Текст SMS не может быть пустым.");
        }

        try
        {
            string url =
                $"{BaseUrl}message/sendSmsMessage" +
                $"?output=json&api=v1&apiKey={Uri.EscapeDataString(ApiKey)}";

            var form = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["recipient"] = recipient,
                    ["text"] = message
                });

            using var response = await _httpClient.PostAsync(
                url, form, cancellationToken);

            string content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, "Ошибка HTTP при отправке SMS через Mobizon.");
            }

            using var json = JsonDocument.Parse(content);

            int code = json.RootElement
                .GetProperty("code")
                .GetInt32();

            string apiMessage = json.RootElement.TryGetProperty(
                "message", out var messageElement)
                ? messageElement.GetString() ?? ""
                : "";

            if (code != 0)
            {
                return (false, string.IsNullOrWhiteSpace(apiMessage)
                    ? $"Mobizon вернул ошибку с кодом {code}."
                    : $"Mobizon: {apiMessage}");
            }

            return (true, "SMS принято сервисом Mobizon.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Превышено время ожидания отправки SMS.");
        }
        catch (HttpRequestException)
        {
            return (false, "Не удалось подключиться к Mobizon.");
        }
        catch (JsonException)
        {
            return (false, "Mobizon вернул некорректный ответ.");
        }
        catch (Exception)
        {
            return (false, "Неизвестная ошибка при отправке SMS.");
        }
    }
}
