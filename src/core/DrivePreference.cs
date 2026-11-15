using System.Text.Json.Serialization;

namespace CodexAccountDesk;

internal sealed class DrivePreference
{
    public string Root { get; set; } = "";
    public string Label { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FolderPreference[]? Folders { get; set; }
}

internal sealed record FolderPreference
{
    public string Path { get; init; } = "";
    public int Color { get; init; }
}
