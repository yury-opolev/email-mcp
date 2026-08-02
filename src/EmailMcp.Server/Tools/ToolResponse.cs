using System.Text.Encodings.Web;
using System.Text.Json;

namespace EmailMcp.Server.Tools;

/// <summary>
/// Shared JSON shaping for tool responses.
/// </summary>
internal static class ToolResponse
{
    // This text goes to an MCP client, never embedded in HTML, so the relaxed encoder avoids
    // needlessly escaping apostrophes and other punctuation that shows up in plain-English
    // error messages.
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialises a successful result.</summary>
    public static string Json(object value) => JsonSerializer.Serialize(value, options);

    /// <summary>Serialises a failure in the shape tools already use.</summary>
    public static string Error(string message) => Json(new { Success = false, Error = message });
}
