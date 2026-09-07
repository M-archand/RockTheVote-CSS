using System.Text.Json;
using System.Text.Json.Serialization;

namespace cs2_rockthevote
{
    /// Reads a config key as a list of strings, accepting either a JSON array
    /// (["mapmenu", "mm"]) or a single comma separated string ("mapmenu,mm") so
    /// configs written against either shape keep loading. Always writes an array.
    public class StringListConverter : JsonConverter<List<string>>
    {
        // Without this, System.Text.Json assigns a bare null for "Key": null
        public override bool HandleNull => true;

        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = new List<string>();

            if (reader.TokenType == JsonTokenType.String)
            {
                AddSplit(values, reader.GetString());
                return values;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                        AddSplit(values, reader.GetString());
                    else
                        reader.Skip();
                }

                return values;
            }

            if (reader.TokenType == JsonTokenType.Null)
                return values;

            throw new JsonException($"Expected a string or an array of strings, got {reader.TokenType}.");
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (string entry in value)
                writer.WriteStringValue(entry);
            writer.WriteEndArray();
        }

        // Entries stay comma splittable inside an array too, so ["mapmenu,mm"] works.
        private static void AddSplit(List<string> values, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                values.Add(part);
        }
    }
}
