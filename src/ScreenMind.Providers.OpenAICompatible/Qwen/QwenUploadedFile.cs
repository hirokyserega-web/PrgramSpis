using System.Text.Json.Serialization;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed record QwenUploadedFile
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("fileId")]
    public string FileId { get; init; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; }

    [JsonConstructor]
    public QwenUploadedFile(string id, string fileId, string filePath, string name, string url, long size, string type)
    {
        Id = id;
        FileId = fileId;
        FilePath = filePath;
        Name = name;
        Url = url;
        Size = size;
        Type = type;
    }
}
