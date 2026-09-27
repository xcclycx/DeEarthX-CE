using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeEarthX.Plugins;

/// <summary>
/// 依赖列表转换器：同时接受旧版字符串数组与新版对象数组。
/// <code>
/// "dependencies": ["a", "b"]
/// "dependencies": [{ "id": "a", "version": "&gt;=1.2.0", "optional": true }]
/// </code>
/// 序列化时统一输出对象数组（信息量更大且向后兼容——只检查 id 的旧读取者仍可用）。
/// </summary>
public sealed class PluginDependencyListConverter : JsonConverter<List<PluginDependency>>
{
    public override List<PluginDependency> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<PluginDependency>();

        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"dependencies 必须是数组，实际为 {reader.TokenType}");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return result;
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var id = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Add(new PluginDependency { Id = id });
                    }

                    break;
                }

                case JsonTokenType.StartObject:
                {
                    var dependency = ReadObject(ref reader);
                    if (!string.IsNullOrWhiteSpace(dependency.Id))
                    {
                        result.Add(dependency);
                    }

                    break;
                }

                default:
                    throw new JsonException($"dependencies 元素必须是字符串或对象，实际为 {reader.TokenType}");
            }
        }

        throw new JsonException("dependencies 数组未正常结束");
    }

    private static PluginDependency ReadObject(ref Utf8JsonReader reader)
    {
        var dependency = new PluginDependency();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dependency;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            switch (name?.ToLowerInvariant())
            {
                case "id":
                case "name":
                    dependency.Id = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? string.Empty : dependency.Id;
                    break;
                case "version":
                case "versionrange":
                case "range":
                    dependency.Version = reader.TokenType == JsonTokenType.String ? reader.GetString() : dependency.Version;
                    break;
                case "optional":
                    dependency.Optional = reader.TokenType switch
                    {
                        JsonTokenType.True => true,
                        JsonTokenType.False => false,
                        JsonTokenType.String => bool.TryParse(reader.GetString(), out var parsed) && parsed,
                        _ => dependency.Optional
                    };
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return dependency;
    }

    public override void Write(Utf8JsonWriter writer, List<PluginDependency> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var dependency in value)
        {
            writer.WriteStartObject();
            writer.WriteString("id", dependency.Id);

            if (!string.IsNullOrEmpty(dependency.Version))
            {
                writer.WriteString("version", dependency.Version);
            }

            if (dependency.Optional)
            {
                writer.WriteBoolean("optional", true);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
