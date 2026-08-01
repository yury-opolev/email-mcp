using System.Text.Json;

namespace EmailMcp.Server.Tools;

/// <summary>
/// Shared JSON shaping for tool responses.
/// </summary>
internal static class ToolResponse
{
    private static readonly JsonSerializerOptions options = new() { WriteIndented = true };

    /// <summary>Serialises a successful result.</summary>
    public static string Json(object value) => JsonSerializer.Serialize(value, options);

    /// <summary>Serialises a failure in the shape tools already use.</summary>
    public static string Error(string message) => Json(new { Success = false, Error = message });
}
