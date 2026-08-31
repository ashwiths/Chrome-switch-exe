using System.Text.Json.Serialization;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public class NativeMessageRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("slot")]
    public int? Slot { get; set; }

    [JsonPropertyName("profileDirectory")]
    public string? ProfileDirectory { get; set; }
}

public class NativeMessageResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("windowHandle")]
    public long? WindowHandle { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
