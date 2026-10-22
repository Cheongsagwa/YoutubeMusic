namespace OneMusic.Core;

/// <summary>YouTube 내부 API 응답(깊고 들쭉날쭉한 JSON)을 안전하게 읽는 도구</summary>
public static class J
{
    /// <summary>경로를 따라 내려간다. 문자열 = 키, 정수 = 배열 번호(음수면 뒤에서부터). 없으면 null</summary>
    public static JsonNode? P(this JsonNode? node, params object[] path)
    {
        var n = node;
        foreach (var p in path)
        {
            if (n == null) return null;
            switch (p)
            {
                case string key:
                    n = n is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;
                    break;
                case int idx:
                    if (n is JsonArray a && a.Count > 0)
                    {
                        var i = idx < 0 ? a.Count + idx : idx;
                        n = i >= 0 && i < a.Count ? a[i] : null;
                    }
                    else n = null;
                    break;
                default:
                    return null;
            }
        }
        return n;
    }

    public static string? Str(this JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<long>(out var l)) return l.ToString();
        }
        return null;
    }

    /// <summary>{ runs: [{text}] } 또는 { simpleText } 를 한 줄 글자로</summary>
    public static string Text(this JsonNode? textObj)
    {
        if (textObj == null) return "";
        var simple = textObj.P("simpleText").Str();
        if (simple != null) return simple;
        if (textObj.P("runs") is JsonArray runs)
            return string.Concat(runs.Select(r => r.P("text").Str() ?? ""));
        return "";
    }

    /// <summary>key 가 붙은 값을 트리 전체에서 모두 찾는다 (깊이 우선, 문서 순서)</summary>
    public static IEnumerable<JsonNode> FindAll(this JsonNode? node, string key, int maxDepth = 40)
    {
        if (node == null || maxDepth <= 0) yield break;
        if (node is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                if (v == null) continue;
                if (k == key) yield return v;
                foreach (var x in FindAll(v, key, maxDepth - 1)) yield return x;
            }
        }
        else if (node is JsonArray a)
        {
            foreach (var v in a)
                foreach (var x in FindAll(v, key, maxDepth - 1)) yield return x;
        }
    }

    public static JsonNode? FindFirst(this JsonNode? node, string key) => node.FindAll(key).FirstOrDefault();

    /// <summary>thumbnails 배열에서 가장 큰 썸네일 주소</summary>
    public static string? BestThumb(this JsonNode? node)
    {
        if (node.FindFirst("thumbnails") is not JsonArray arr || arr.Count == 0) return null;
        return arr[^1].P("url").Str();
    }
}
