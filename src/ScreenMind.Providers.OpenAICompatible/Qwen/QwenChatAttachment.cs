using System.Text.Json.Serialization;

namespace ScreenMind.Providers.OpenAICompatible.Qwen;

public sealed record QwenChatAttachment
{
    [JsonPropertyName("id")] public string Id { get; init; }
    [JsonPropertyName("fileId")] public string FileId { get; init; }
    [JsonPropertyName("file_id")] public string FileIdSnakeCase { get; init; }
    [JsonPropertyName("file_path")] public string FilePath { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; }
    [JsonPropertyName("url")] public string Url { get; init; }
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; }

    public QwenChatAttachment(string id, string fileId, string fileIdSnakeCase, string filePath, string name, string url, long size, string type)
    {
        Id = id;
        FileId = fileId;
        FileIdSnakeCase = fileIdSnakeCase;
        FilePath = filePath;
        Name = name;
        Url = url;
        Size = size;
        Type = type;
    }

    public static QwenChatAttachment FromUpload(QwenUploadedFile upload)
    {
        ArgumentNullException.ThrowIfNull(upload);
        return new(upload.Id, upload.FileId, upload.FileId, upload.FilePath, upload.Name, upload.Url, upload.Size, "image");
    }
}
