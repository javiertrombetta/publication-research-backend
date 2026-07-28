using System.Net.Http.Json;
using System.Text.Json;

namespace PublicationSite.IntegrationTests.Infrastructure;

public record ApiEnvelope<T>(bool Success, T? Data, string? Message, List<string>? Errors);

public static class HttpClientExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<(System.Net.HttpStatusCode StatusCode, ApiEnvelope<T>? Body)> PostAsync<T>(
        this HttpClient client, string url, object payload)
    {
        var response = await client.PostAsJsonAsync(url, payload, JsonOptions);
        var body = await SafeReadAsync<T>(response);
        return (response.StatusCode, body);
    }

    public static async Task<(System.Net.HttpStatusCode StatusCode, ApiEnvelope<T>? Body)> PutAsync<T>(
        this HttpClient client, string url, object payload)
    {
        var response = await client.PutAsJsonAsync(url, payload, JsonOptions);
        var body = await SafeReadAsync<T>(response);
        return (response.StatusCode, body);
    }

    public static async Task<(System.Net.HttpStatusCode StatusCode, ApiEnvelope<T>? Body)> GetAsync<T>(
        this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await SafeReadAsync<T>(response);
        return (response.StatusCode, body);
    }

    private static async Task<ApiEnvelope<T>?> SafeReadAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ApiEnvelope<T>>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<(System.Net.HttpStatusCode StatusCode, ApiEnvelope<T>? Body)> PostFileAsync<T>(
        this HttpClient client, string url, string fieldName, string fileName, byte[] content, IDictionary<string, string>? extraFields = null)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, fieldName, fileName);

        if (extraFields is not null)
        {
            foreach (var (key, value) in extraFields)
            {
                form.Add(new StringContent(value), key);
            }
        }

        var response = await client.PostAsync(url, form);
        var body = await SafeReadAsync<T>(response);
        return (response.StatusCode, body);
    }

    public static void AuthenticateWith(this HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
}
