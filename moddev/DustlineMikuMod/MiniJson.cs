using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DustlineMikuMod
{
    /// <summary>
    /// 轻量 JSON 解析器：只实现 glTF 需要的子集（对象/数组/字符串/数字/布尔/null）。
    /// 避免依赖 BepInEx 未提供的 JSON 库。
    /// </summary>
    internal sealed class JsonValue
    {
        public enum ValueKind { Null, Bool, Number, String, Array, Object }

        public ValueKind Kind;
        public bool BoolValue;
        public double NumberValue;
        public string StringValue;
        public List<JsonValue> ArrayValue;
        public Dictionary<string, JsonValue> ObjectValue;

        public static readonly JsonValue Null = new JsonValue { Kind = ValueKind.Null };

        public bool IsNull => Kind == ValueKind.Null;
        public bool IsArray => Kind == ValueKind.Array;
        public bool IsObject => Kind == ValueKind.Object;

        public JsonValue this[int index]
        {
            get
            {
                if (Kind != ValueKind.Array || index < 0 || index >= ArrayValue.Count) return Null;
                return ArrayValue[index];
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                if (Kind == ValueKind.Object && ObjectValue.TryGetValue(key, out JsonValue v)) return v;
                return Null;
            }
        }

        public int Count
        {
            get
            {
                if (Kind == ValueKind.Array) return ArrayValue.Count;
                if (Kind == ValueKind.Object) return ObjectValue.Count;
                return 0;
            }
        }

        public bool Has(string key) => Kind == ValueKind.Object && ObjectValue.ContainsKey(key);

        public float AsFloat(float fallback = 0f) => Kind == ValueKind.Number ? (float)NumberValue : fallback;
        public int AsInt(int fallback = 0) => Kind == ValueKind.Number ? (int)Math.Round(NumberValue) : fallback;
        public string AsString(string fallback = null) => Kind == ValueKind.String ? StringValue : fallback;
        public bool AsBool(bool fallback = false)
        {
            if (Kind == ValueKind.Bool) return BoolValue;
            if (Kind == ValueKind.Number) return Math.Abs(NumberValue) > double.Epsilon;
            return fallback;
        }

        public static JsonValue Parse(string text)
        {
            int index = 0;
            JsonValue value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            return value;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return Null;
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Kind = ValueKind.String, StringValue = ParseString(s, ref i) };
                case 't':
                    i += 4; return new JsonValue { Kind = ValueKind.Bool, BoolValue = true };
                case 'f':
                    i += 5; return new JsonValue { Kind = ValueKind.Bool, BoolValue = false };
                case 'n':
                    i += 4; return Null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            JsonValue result = new JsonValue
            {
                Kind = ValueKind.Object,
                ObjectValue = new Dictionary<string, JsonValue>(StringComparer.Ordinal)
            };
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                JsonValue value = ParseValue(s, ref i);
                result.ObjectValue[key] = value;
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return result;
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            JsonValue result = new JsonValue
            {
                Kind = ValueKind.Array,
                ArrayValue = new List<JsonValue>()
            };
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (i < s.Length)
            {
                result.ArrayValue.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return result;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            StringBuilder sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length) break;
                char esc = s[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(esc); break;
                }
            }
            return sb.ToString();
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string raw = s.Substring(start, i - start);
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                return new JsonValue { Kind = ValueKind.Number, NumberValue = d };
            }
            return Null;
        }
    }
}