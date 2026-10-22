using Windows.Graphics.Imaging;

namespace OneMusic.Core;

/// <summary>앨범아트의 대표색을 뽑아 어두운 배경 위에서 잘 보이는 강조색으로 다듬는다 (테마의 toAccent 와 같은 규칙)</summary>
public static class AlbumColor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static readonly Color Fallback = Color.FromArgb(255, 0xe5, 0xe5, 0xea);

    public static async Task<Color> FromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return Fallback;
        try
        {
            var bytes = await Http.GetByteArrayAsync(Fmt.Thumb(url, 60));
            using var ras = new MemoryStream(bytes).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var transform = new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24, InterpolationMode = BitmapInterpolationMode.Fant };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return ToAccent(Dominant(data.DetachPixelData()));
        }
        catch (Exception e)
        {
            Log.Warn($"앨범 색 추출 실패: {e.Message}");
            return Fallback;
        }
    }

    /// <summary>색상(hue)을 24칸으로 나눠, 채도 · 밝기가 있는 픽셀이 가장 많이 몰린 칸의 평균색</summary>
    private static Color Dominant(byte[] px)
    {
        const int bins = 24;
        var weight = new double[bins];
        var r = new double[bins];
        var g = new double[bins];
        var b = new double[bins];
        double gr = 0, gg = 0, gb = 0;
        var n = 0;
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            double B = px[i] / 255.0, G = px[i + 1] / 255.0, R = px[i + 2] / 255.0;
            gr += R; gg += G; gb += B; n++;
            var (h, s, l) = ToHsl(R, G, B);
            if (s < 0.15 || l < 0.08 || l > 0.95) continue;
            var w = s * (1 - Math.Abs(l - 0.5) * 1.2);
            var k = (int)(h / 360 * bins) % bins;
            weight[k] += w; r[k] += R * w; g[k] += G * w; b[k] += B * w;
        }
        var best = Array.IndexOf(weight, weight.Max());
        if (weight[best] < 0.8 || n == 0)
            return n == 0 ? Fallback : C(gr / n, gg / n, gb / n); // 거의 무채색 앨범
        return C(r[best] / weight[best], g[best] / weight[best], b[best] / weight[best]);
    }

    public static Color ToAccent(Color c)
    {
        var (h, s, l) = ToHsl(c.R / 255.0, c.G / 255.0, c.B / 255.0);
        var sat = s < 0.12 ? s : Math.Max(s, 0.85);
        var light = Math.Clamp(l, 0.52, 0.62);
        return FromHsl(h, sat, light);
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color C(double r, double g, double b) =>
        Color.FromArgb(255, (byte)Math.Clamp(r * 255, 0, 255), (byte)Math.Clamp(g * 255, 0, 255), (byte)Math.Clamp(b * 255, 0, 255));

    public static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        if (s < 1e-6) return C(l, l, l);
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        var hh = h / 360;
        return C(Hue(p2, q2, hh + 1.0 / 3), Hue(p2, q2, hh), Hue(p2, q2, hh - 1.0 / 3));
    }
}
