using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReflectionTimer.Core;

public record SheetReply(bool Success, string ErrorKind, string DisplayMessage, string Tab = "", string Target = "");

public sealed class SheetsClient : IDisposable
{
    private readonly HttpClient client;
    public SheetsClient(HttpMessageHandler? handler = null) => client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(25) };
    public static string? Validate(ConnectionSettings settings)
    {
        if (!Uri.TryCreate(settings.WebAppUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "script.google.com"
            || !Regex.IsMatch(url.AbsolutePath, @"^/macros/s/[^/]+/exec/?$") || url.UserInfo.Length != 0 || url.Port != 443)
            return "Enter the deployed Apps Script URL ending in /exec, not the editor URL.";
        if (!Regex.IsMatch(settings.SheetUrl, @"^https://docs\.google\.com/spreadsheets/d/[A-Za-z0-9_-]{20,}(?:/|$)")) return "Enter a valid Google Sheets URL.";
        if (settings.ApiToken.Trim().Length < 16) return "Enter the Reflection API token from your existing extension settings.";
        if (settings.SheetMode != "date" && settings.SheetMode != "fixed") return "Choose automatic dates or a fixed tab.";
        if (settings.SheetMode == "fixed" && (string.IsNullOrWhiteSpace(settings.SheetName) || settings.SheetName.Length > 100)) return "Enter the fixed tab name.";
        return null;
    }
    public Task<SheetReply> Ping(ConnectionSettings settings, CancellationToken cancellation = default) => Send(settings, null, cancellation);
    public Task<SheetReply> Upload(ConnectionSettings settings, OutboxItem item, CancellationToken cancellation = default) => Send(settings, item, cancellation);
    private async Task<SheetReply> Send(ConnectionSettings settings, OutboxItem? item, CancellationToken cancellation)
    {
        var invalid = Validate(settings);
        if (invalid is not null) return new(false, "settings_required", invalid);
        var submitted = item?.SubmittedAt ?? DateTimeOffset.Now;
        var body = JsonSerializer.Serialize(new {
            action = item is null ? "ping" : "appendReflection", token = settings.ApiToken.Trim(),
            sheetUrl = item?.SheetUrl ?? settings.SheetUrl, sheetMode = item?.SheetMode ?? settings.SheetMode,
            sheetName = item?.SheetName ?? settings.SheetName, submittedAt = submitted.UtcDateTime.ToString("O"),
            timezoneOffsetMinutes = -(int)submitted.Offset.TotalMinutes, durationSeconds = item?.DurationSeconds ?? 0,
            isTest = item?.IsTest ?? false, message = item?.Message ?? "", requestId = item?.Id.ToString()
        });
        try
        {
            var uri = new Uri(settings.WebAppUrl);
            var method = HttpMethod.Post;
            for (var redirects = 0; redirects <= 5; redirects++)
            {
                using var request = new HttpRequestMessage(method, uri);
                if (method == HttpMethod.Post) request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                using var response = await client.SendAsync(request, cancellation);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    var next = response.Headers.Location is { } location ? new Uri(uri, location) : null;
                    if (next is null || next.Scheme != "https" || next.Port != 443 || next.UserInfo.Length != 0
                        || (next.Host != "script.googleusercontent.com" && next.Host != "script.google.com"))
                        return new(false, "invalid_response", "The receiver returned an unexpected redirect. Check your deployment URL.");
                    if ((int)response.StatusCode is 301 or 302 or 303) method = HttpMethod.Get;
                    uri = next;
                    continue;
                }
                var text = await response.Content.ReadAsStringAsync(cancellation);
                using var data = JsonDocument.Parse(text);
                var root = data.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new(false, "invalid_response", "The receiver did not return the expected JSON object.");
                if (response.IsSuccessStatusCode && root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
                    return new(true, "", "Connected.", Read(root, "sheet"), Read(root, "target"));
                // Raw server responses can contain arbitrary reflection text or
                // credentials. Never send them to diagnostics or persisted errors.
                return new(false, "rejected", "Google Sheets rejected the request. Check the connection settings, token, and destination tab.");
            }
            return new(false, "invalid_response", "Too many receiver redirects.");
        }
        catch (OperationCanceledException) { return new(false, "timeout", "The request timed out. Check the Sheet before retrying a reflection—it may already have arrived."); }
        catch (HttpRequestException) { return new(false, "network", "The network request failed. Check the Sheet before retrying a reflection."); }
        catch (JsonException) { return new(false, "invalid_response", "The receiver did not return valid JSON. Check that the URL is a deployed /exec web app."); }
    }
    private static string Read(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public void Dispose() => client.Dispose();
}
