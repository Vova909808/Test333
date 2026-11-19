
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SystemX.Server.Services;

public class MobizonSmsSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MobizonSmsSender> _logger;

    private const string BaseUrl = "https://api.mobizon.kz/service/";

    public MobizonSmsSender(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MobizonSmsSender> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

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

            string content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Mobizon balance request returned HTTP {StatusCode}. Response: {Response}",
                    (int)response.StatusCode,
                    LimitResponse(content));

                return (false, $"Mobizon HTTP {(int)response.StatusCode}.");
            }

            return ParseMobizonResponse(content, "Проверка баланса");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Mobizon balance request timed out.");
            return (false, "Превышено время ожидания ответа Mobizon.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Mobizon connection error.");
            return (false, "Не удалось установить соединение с Mobizon.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Mobizon balance error.");
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
            _logger.LogError("MOBIZON_API_KEY is not configured.");
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

            using var form = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["recipient"] = recipient,
                    ["text"] = message
                });

            using var response = await _httpClient.PostAsync(
                url, form, cancellationToken);

            string content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            _logger.LogInformation(
                "Mobizon SMS response: HTTP {StatusCode}. Body: {Response}",
                (int)response.StatusCode,
                LimitResponse(content));

            if (!response.IsSuccessStatusCode)
            {
                return (false,
                    $"Mobizon вернул HTTP {(int)response.StatusCode}.");
            }

            return ParseMobizonResponse(content, "Отправка SMS");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Mobizon SMS request timed out.");
            return (false, "Превышено время ожидания отправки SMS.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Mobizon HTTP request failed.");
            return (false, "Не удалось подключиться к Mobizon.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Mobizon returned invalid JSON.");
            return (false, "Mobizon вернул некорректный JSON-ответ.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Mobizon SMS error.");
            return (false, "Неизвестная ошибка при отправке SMS.");
        }
    }

    private (bool Success, string Message) ParseMobizonResponse(
        string content,
        string operation)
    {
        try
        {
            using var json = JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("code", out var codeElement))
            {
                _logger.LogWarning(
                    "Mobizon response does not contain 'code'. Operation: {Operation}. Body: {Response}",
                    operation,
                    LimitResponse(content));

                return (false, "Ответ Mobizon не содержит код результата.");
            }

            int code;

            if (codeElement.ValueKind == JsonValueKind.Number &&
                codeElement.TryGetInt32(out int numericCode))
            {
                code = numericCode;
            }
            else if (codeElement.ValueKind == JsonValueKind.String &&
                     int.TryParse(
                         codeElement.GetString(),
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out int stringCode))
            {
                code = stringCode;
            }
            else
            {
                _logger.LogWarning(
                    "Mobizon returned an unsupported code format. Operation: {Operation}",
                    operation);

                return (false, "Некорректный формат ответа Mobizon.");
            }

            string apiMessage = "";

            if (json.RootElement.TryGetProperty("message", out var messageElement))
            {
                apiMessage = messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString() ?? ""
                    : messageElement.ToString();
            }

            if (code != 0)
            {
                _logger.LogWarning(
                    "Mobizon rejected operation {Operation}. Code: {Code}. Message: {Message}",
                    operation,
                    code,
                    apiMessage);

                return (false,
                    string.IsNullOrWhiteSpace(apiMessage)
                        ? $"Mobizon вернул код ошибки {code}."
                        : $"Mobizon: {apiMessage} (код {code}).");
            }

            _logger.LogInformation(
                "Mobizon operation {Operation} completed successfully.",
                operation);

            return (true, "Mobizon успешно обработал запрос.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Cannot parse Mobizon response. Operation: {Operation}. Body: {Response}",
                operation,
                LimitResponse(content));

            return (false, "Mobizon вернул некорректный JSON-ответ.");
        }
    }

    private static string LimitResponse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "<empty>";

        const int maxLength = 2000;

        return content.Length <= maxLength
            ? content
            : content[..maxLength] + "...";
    }
}