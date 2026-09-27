using System.Text.Json.Nodes;

namespace DeEarthX.Platform;

internal static class JsonExtensions
{
    public static string? AsString(this JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s))
            {
                return s;
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i.ToString();
            }

            if (value.TryGetValue<long>(out var l))
            {
                return l.ToString();
            }

            if (value.TryGetValue<bool>(out var b))
            {
                return b.ToString();
            }
        }

        return node.ToJsonString();
    }

    public static long? AsLong(this JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<long>(out var l))
            {
                return l;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (long)d;
            }
        }

        return long.TryParse(node.ToString(), out var parsed) ? parsed : null;
    }
}
