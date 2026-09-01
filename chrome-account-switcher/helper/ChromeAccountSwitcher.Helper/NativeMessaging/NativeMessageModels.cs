using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ChromeAccountSwitcher.Helper.NativeMessaging;

public class TabItemDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public class NativeMessageRequest
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("slot")]
    public int? Slot { get; set; }

    [JsonPropertyName("profileDirectory")]
    public string? ProfileDirectory { get; set; }

    [JsonPropertyName("copyTabs")]
    public bool? CopyTabs { get; set; }

    [JsonPropertyName("sourceProfile")]
    public string? SourceProfile { get; set; }

    [JsonPropertyName("tabs")]
    public List<TabItemDto>? Tabs { get; set; }
}

public class NativeMessageResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("sourceProfile")]
    public string? SourceProfile { get; set; }

    [JsonPropertyName("targetProfile")]
    public string? TargetProfile { get; set; }

    [JsonPropertyName("tabsCopied")]
    public int TabsCopied { get; set; }

    [JsonPropertyName("tabsSkipped")]
    public int TabsSkipped { get; set; }

    [JsonPropertyName("windowHandle")]
    public long? WindowHandle { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
