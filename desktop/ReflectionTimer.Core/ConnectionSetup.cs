using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReflectionTimer.Core;

// A setup code is a private convenience format, NOT encryption or a Google login.
public static class ConnectionSetup
{
    public const string CodePrefix = "reflection-timer:v1:";
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string SpreadsheetId(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "docs.google.com"
            || uri.UserInfo.Length > 0 || uri.Port != 443) throw new ArgumentException("Paste your own Google Sheets URL from the browser address bar.");
        var match = Regex.Match(uri.AbsolutePath, @"^/spreadsheets/d/([A-Za-z0-9_-]{20,})(?:/|$)");
        if (!match.Success) throw new ArgumentException("Paste a Google Sheets file URL, not a folder or Apps Script URL.");
        return match.Groups[1].Value;
    }
    public static string NormalizeSheetUrl(string url) => "https://docs.google.com/spreadsheets/d/" + SpreadsheetId(url) + "/edit";
    internal static bool HasSpreadsheet(string? value)
    {
        try { _ = SpreadsheetId(value); return true; }
        catch (ArgumentException) { return false; }
    }
    internal static bool IsReceiverUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "script.google.com" && uri.UserInfo.Length == 0 && uri.Port == 443
        && uri.Query.Length == 0 && uri.Fragment.Length == 0 && Regex.IsMatch(uri.AbsolutePath, @"^/macros/s/[A-Za-z0-9_-]+/exec/?$");
    internal static bool SameSpreadsheet(string first, string second)
    {
        try { return SpreadsheetId(first) == SpreadsheetId(second); }
        catch (ArgumentException) { return string.Equals(first?.Trim(), second?.Trim(), StringComparison.Ordinal); }
    }
    internal static bool SameReceiver(string first, string second)
    {
        static string Identity(string value) => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped).TrimEnd('/')
            : value?.Trim().TrimEnd('/') ?? "";
        return string.Equals(Identity(first), Identity(second), StringComparison.Ordinal);
    }
    public static string Export(ConnectionSettings connection)
    {
        var invalid = SheetsClient.Validate(connection);
        if (invalid is not null) throw new ArgumentException(invalid);
        return CodePrefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(connection));
    }
    public static ConnectionSettings Import(string code)
    {
        code = code.Trim();
        if (!code.StartsWith(CodePrefix, StringComparison.Ordinal) || code.Length > 16000)
            throw new ArgumentException("Paste a private Reflection Timer setup code copied from your other PC.");
        try {
            var value = JsonSerializer.Deserialize<ConnectionSettings>(Convert.FromBase64String(code[CodePrefix.Length..]));
            if (value is null || value.SheetUrl is null || value.WebAppUrl is null || value.ApiToken is null || value.SheetMode is null || value.SheetName is null)
                throw new ArgumentException();
            _ = SpreadsheetId(value.SheetUrl);
            if (SheetsClient.Validate(value) is not null) throw new ArgumentException();
            return value;
        } catch (Exception e) when (e is JsonException or FormatException or ArgumentException) {
            throw new ArgumentException("That setup code is incomplete or invalid. Copy it again from the other PC.");
        }
    }
    public static string BuildScript(string receiverSource, ConnectionSettings draft)
    {
        var id = SpreadsheetId(draft.SheetUrl);
        if (!Regex.IsMatch(draft.ApiToken, "^[a-f0-9]{64}$")) throw new ArgumentException("Generate a new setup token before copying the script.");
        // JSON escaping prevents input from becoming JavaScript source.
        return receiverSource + "\n\n// Private setup: do not publish this personalized script or share it with other users.\n"
            + "// Run once from your OWN spreadsheet's Apps Script editor, then deploy as a web app.\n"
            + "function setupReflectionTimer() {\n  return initializeReflectionTimer_("
            + JsonSerializer.Serialize(id) + ", " + JsonSerializer.Serialize(draft.ApiToken) + ");\n}\n";
    }
}
