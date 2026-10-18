using System;
using System.Collections.Generic;
using System.Globalization;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace CodexMeter.Core
{
    // Keep protocol shapes independent of either runtime's JSON object model.
    public static class Json
    {
        public static Dictionary<string, object> Parse(string value)
        {
#if NETFRAMEWORK
            try
            {
                return new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 64 }
                    .Deserialize<Dictionary<string, object>>(value);
            }
            catch (ArgumentException) { throw InvalidJson(); }
            catch (InvalidOperationException) { throw InvalidJson(); }
#else
            try
            {
                using (var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 64 }))
                    return ConvertElement(document.RootElement) as Dictionary<string, object>;
            }
            catch (JsonException) { throw InvalidJson(); }
#endif
        }

        // Include derived reader exceptions without leaking input or runtime-specific types.
        private static FormatException InvalidJson() { return new FormatException("Invalid JSON data."); }

        public static string Encode(object value)
        {
#if NETFRAMEWORK
            return new JavaScriptSerializer { MaxJsonLength = 1048576 }.Serialize(value);
#else
            return JsonSerializer.Serialize(value);
#endif
        }

#if !NETFRAMEWORK
        private static object ConvertElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject()) map[property.Name] = ConvertElement(property.Value);
                    return map;
                case JsonValueKind.Array:
                    var items = new List<object>();
                    foreach (var item in element.EnumerateArray()) items.Add(ConvertElement(item));
                    return items.ToArray();
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number: return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
#endif
        public static object Get(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }
        public static Dictionary<string, object> Object(Dictionary<string, object> map, string key)
        { return Get(map, key) as Dictionary<string, object>; }
        public static string Text(Dictionary<string, object> map, string key)
        { return Get(map, key) as string; }
        public static double? Number(Dictionary<string, object> map, string key)
        {
            var value = Get(map, key);
            if (value == null || value is string || value is bool) return null;
            try
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsInfinity(number) || double.IsNaN(number) ? (double?)null : number;
            }
            catch (Exception ex)
            { if (!(ex is FormatException || ex is InvalidCastException || ex is OverflowException)) throw; return null; }
        }
    }
}
