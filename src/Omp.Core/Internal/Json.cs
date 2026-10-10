using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Omp.Core.Internal;

/// <summary>JSON parsing and loosely typed field access matching JavaScript semantics for decoded wire frames.</summary>
internal static class Json
{
    private const double MaxSafeInteger = 9007199254740991d;

    /// <summary>Parses exactly one JSON value; strings that look like dates stay strings.</summary>
    public static JToken Parse(string text)
    {
        using (var reader = new JsonTextReader(new StringReader(text)))
        {
            reader.DateParseHandling = DateParseHandling.None;
            reader.FloatParseHandling = FloatParseHandling.Double;
            reader.MaxDepth = 512;
            var token = JToken.ReadFrom(reader);
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.Comment)
                {
                    throw new JsonReaderException("Unexpected content after the JSON value");
                }
            }

            return token;
        }
    }

    public static string Serialize(JToken token) => token.ToString(Formatting.None);

    public static JToken? Get(JToken? token, string key) => token is JObject obj ? obj[key] : null;

    public static string? Str(JToken? token) => token is JValue value && value.Type == JTokenType.String ? (string?)value.Value : null;

    public static string? Str(JToken? token, string key) => Str(Get(token, key));

    public static bool IsNumber(JToken? token) => token is not null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);

    /// <summary>
    /// Attempts to parse a JSON token as a double-precision floating-point number using the invariant culture, returning null if the token is not a valid number.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The double? result.</returns>
    public static double? Num(JToken? token) => IsNumber(token) ? Convert.ToDouble(((JValue)token!).Value, System.Globalization.CultureInfo.InvariantCulture) : (double?)null;

    /// <summary>
    /// Extracts and parses a numeric value from a specified key within a JSON token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="key">The key.</param>
    /// <returns>The double? result.</returns>
    public static double? Num(JToken? token, string key) => Num(Get(token, key));

    /// <summary>
    /// Extracts a long integer value from the specified key within a JToken, returning null if the token or key is not found or cannot be converted.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="key">The key.</param>
    /// <returns>The long? result.</returns>
    public static long? Long(JToken? token, string key)
    {
        var value = Num(token, key);

        return value.HasValue ? (long)value.Value : (long?)null;
    }

    /// <summary>JavaScript <c>Number.isSafeInteger</c>.</summary>
    public static bool IsSafeInteger(JToken? token)
    {
        var value = Num(token);

        return value.HasValue && !double.IsNaN(value.Value) && Math.Floor(value.Value) == value.Value && Math.Abs(value.Value) <= MaxSafeInteger;
    }

    /// <summary>
    /// Attempts to cast the specified JToken to a boolean value, returning null if the token is not a boolean JValue.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The bool? result.</returns>
    public static bool? Bool(JToken? token) => token is JValue value && value.Type == JTokenType.Boolean ? (bool)value.Value! : (bool?)null;

    public static bool? Bool(JToken? token, string key) => Bool(Get(token, key));

    /// <summary>JavaScript truthiness of a JSON value.</summary>
    public static bool Truthy(JToken? token)
    {
        if (token is null)
        {
            return false;
        }

        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return false;

            case JTokenType.Boolean:
                return (bool)token;

            case JTokenType.String:
                return ((string?)token)!.Length > 0;

            case JTokenType.Integer:
            case JTokenType.Float:
                var number = Num(token)!.Value;
                return number != 0 && !double.IsNaN(number);

            default:
                return true;
        }
    }

    /// <summary>
    /// Retrieves a specified child token from the provided JToken as a JArray.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="key">The key.</param>
    /// <returns>The jarray? result.</returns>
    public static JArray? Array(JToken? token, string key) => Get(token, key) as JArray;

    /// <summary>
    /// Extracts a read-only list of non-null strings from a JSON array token, returning an empty list if the token is not a JArray.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>A collection of iread only list items.</returns>
    public static IReadOnlyList<string> Strings(JToken? token) =>
        token is JArray array ? [.. array.Select(Str).Where(s => s != null).Select(s => s!)] : System.Array.Empty<string>();

    /// <summary>Text of <c>{ type: "text" }</c> blocks (or a bare string), joined by <paramref name="separator"/>.</summary>
    public static string TextOf(JToken? content, string separator = "\n")
    {
        var text = Str(content);
        if (text is not null)
        {
            return text;
        }

        if (!(content is JArray array))
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var block in array)
        {
            if (Str(block, "type") == "text")
            {
                var part = Str(block, "text");
                if (part is not null)
                {
                    parts.Add(part);
                }
            }
        }

        return string.Join(separator, parts);
    }
}
