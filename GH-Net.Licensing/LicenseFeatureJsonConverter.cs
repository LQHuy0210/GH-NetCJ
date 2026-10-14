using System.Text.Json;
using System.Text.Json.Serialization;

namespace GH_Net.Licensing;

/// <summary>Keeps signed legacy All claims from acquiring newly added capabilities.</summary>
internal sealed class LicenseFeatureJsonConverter : JsonConverter<LicenseFeature>
{
    public override LicenseFeature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int mask))
            return (LicenseFeature)mask;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Invalid license features.");

        LicenseFeature features = LicenseFeature.None;
        foreach (string part in reader.GetString()!.Split(','))
        {
            string name = part.Trim();
            // GHNet-Base is the authoritative pre-Scanner contract: All = 15.
            if (name.Equals(nameof(LicenseFeature.All), StringComparison.OrdinalIgnoreCase))
                features |= LicenseFeature.Printer | LicenseFeature.Camera | LicenseFeature.Export | LicenseFeature.Plc;
            else if (Enum.TryParse(name, ignoreCase: true, out LicenseFeature feature))
                features |= feature;
            else
                throw new JsonException("Unknown license feature.");
        }
        return features;
    }

    public override void Write(Utf8JsonWriter writer, LicenseFeature value, JsonSerializerOptions options)
    {
        if ((value & ~LicenseFeature.All) != 0)
        {
            writer.WriteNumberValue((int)value);
            return;
        }
        // Always emit individual names so adding future features cannot expand a token.
        string[] names = Enum.GetValues<LicenseFeature>()
            .Where(feature => feature is not LicenseFeature.None and not LicenseFeature.All && (value & feature) == feature)
            .Select(feature => feature.ToString()).ToArray();
        writer.WriteStringValue(names.Length == 0 ? nameof(LicenseFeature.None) : string.Join(", ", names));
    }
}
