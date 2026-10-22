using Microsoft.UI.Xaml.Markup;
using Windows.UI.Text;

namespace OneMusic.UI;

/// <summary>One UI 토큰 (pear-desktop 테마 style.css 맨 위 토큰과 같은 값)</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(255, 0x17, 0x17, 0x1a);
    public static readonly Color OnSurface = Colors.White;
    public static readonly Color Secondary = Color.FromArgb(255, 160, 160, 164);
    public static readonly Color Chip = Color.FromArgb(26, 255, 255, 255);
    public static readonly Color ChipHover = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color Selected = Color.FromArgb(56, 255, 255, 255);
    public static readonly Color Divider = Color.FromArgb(31, 255, 255, 255);
    public static readonly Color GlassTint = Color.FromArgb(255, 36, 36, 40);

    public static readonly FontFamily Font = new("Pretendard Variable, Pretendard, Segoe UI Variable Text, Malgun Gothic");

    public const double RadiusArt = 16;
    public const double RadiusThumb = 10;
    public const double RadiusCard = 26;
    public const double FloatGap = 12;
    public const double BarHeight = 96;

    /// <summary>같은 색 붓은 하나를 같이 쓴다 (붓 색을 바꾸는 곳이 없어서 안전 — 수천 줄 목록에서 붓 수천 개를 아낀다)</summary>
    private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();

    public static SolidColorBrush B(Color c)
    {
        if (Brushes.TryGetValue(c, out var b)) return b;
        if (Brushes.Count > 512) Brushes.Clear();
        return Brushes[c] = new SolidColorBrush(c);
    }

    public static SolidColorBrush B(byte a, byte r, byte g, byte b) => B(Color.FromArgb(a, r, g, b));
    public static Color WithAlpha(this Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>
    /// 앱 안의 유리 — 뒤 내용을 크게 흐리고(블러 36 · 채도 1.4) 반투명 색을 덮는다.
    /// tintOpacity 는 CSS rgb(.. / a) 의 a 와 같다 (pear --oneui-glass 0.6 · --oneui-glass-strong 0.86)
    /// </summary>
    public static Brush Glass(double tintOpacity = 0.55, Color? tint = null, float blur = 36) => new BlurBrush
    {
        Blur = blur,
        Tint = (tint ?? GlassTint).WithAlpha(tintOpacity),
        FallbackColor = (tint ?? GlassTint).WithAlpha(0.94),
    };

    /// <summary>흰빛 유리 (검색창 · 버튼 — pear button-glass: 흰 12% + 블러)</summary>
    public static Brush GlassLight(double whiteOpacity, float blur = 30) => new BlurBrush
    {
        Blur = blur,
        Tint = Colors.White.WithAlpha(whiteOpacity),
        FallbackColor = Colors.White.WithAlpha(whiteOpacity),
    };

    public static TextBlock Text(string text, double size, FontWeight weight, Color color, int maxLines = 1) => new()
    {
        Text = maxLines == 1 ? text : KeepAll(text),
        FontFamily = Font,
        FontSize = size,
        FontWeight = weight,
        Foreground = B(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = maxLines == 1 ? TextWrapping.NoWrap : TextWrapping.WrapWholeWords,
        MaxLines = maxLines,
        IsTextSelectionEnabled = false,
    };

    /// <summary>
    /// 한글 음절 사이는 줄바꿈 자리라서 WrapWholeWords 여도 '트랙' 이 '트 / 랙' 으로 잘린다 —
    /// 붙은 음절 사이에 단어 이음표(U+2060, 폭 0)를 넣어 띄어쓰기에서만 넘어가게 (CSS word-break: keep-all)
    /// </summary>
    public static string KeepAll(string text)
    {
        static bool Hangul(char c) => c is >= '가' and <= '힣';
        var sb = new System.Text.StringBuilder(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && Hangul(text[i]) && Hangul(text[i - 1])) sb.Append('⁠');
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>SVG path 문자열 → XAML 도형 (도형은 요소마다 새로 만들어야 한다)</summary>
    public static Geometry Geo(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    // YouTube Cairo 아이콘 (fonts.gstatic.com/s/i/short-term/release/youtube_outline|youtube_fill/svg/thumb_up_cairo_24px.svg).
    // 싫어요는 YouTube 처럼 같은 모양을 180° 돌린다
    private const string ThumbOutline = "M14.813 5.018 14.41 6.5 14 8h5.192c.826 0 1.609.376 2.125 1.022.711.888.794 2.125.209 3.101L21 13l.165.413c.519 1.296.324 2.769-.514 3.885l-.151.202v.5c0 1.657-1.343 3-3 3H5c-1.105 0-2-.895-2-2v-8c0-1.105.895-2 2-2h2v.282c0-.834.26-1.647.745-2.325L12 1l1.1.472c1.376.59 2.107 2.103 1.713 3.546ZM7 10.5H5c-.276 0-.5.224-.5.5v8c0 .276.224.5.5.5h2v-9Zm10.5 9h-9V9.282c0-.521.163-1.03.466-1.453l3.553-4.975c.682.298 1.043 1.051.847 1.77l-.813 2.981c-.123.451-.029.934.255 1.305.284.372.725.59 1.192.59h5.192c.37 0 .722.169.954.459.32.399.357.954.094 1.393l-.526.876c-.241.402-.28.894-.107 1.33l.165.412c.324.81.203 1.73-.32 2.428l-.152.202c-.195.26-.3.575-.3.9v.5c0 .828-.672 1.5-1.5 1.5Z";
    private const string ThumbFill = "M8 21V9.282a4 4 0 01.745-2.325L13 1l.551.33a3 3 0 011.351 3.363L14 8h5.192a2.722 2.722 0 012.334 4.123L21 13l.165.413a4 4 0 01-.514 3.885l-.151.202v.5a3 3 0 01-3 3H8ZM4.5 9A1.5 1.5 0 003 10.5v9A1.5 1.5 0 004.5 21H7V9H4.5Z";

    /// <summary>'@이름' 표시의 아이콘이면 채움 도형으로 (filled 면 꽉 찬 모양)</summary>
    private static FrameworkElement? Special(string data, double size, Color color, bool filled)
    {
        if (data is not ("@thumb_up" or "@thumb_down")) return null;
        var path = new XPath { Data = Geo(filled ? ThumbFill : ThumbOutline), Fill = B(color) };
        if (data == "@thumb_down") path.RenderTransform = new RotateTransform { Angle = 180, CenterX = 12, CenterY = 12 };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>24×24 좌표계 아이콘 (SVG path). stroke 면 선, 아니면 채움</summary>
    public static FrameworkElement Icon(string data, double size, Color color, bool stroke = false, double strokeWidth = 1.8)
    {
        if (Special(data, size, color, filled: false) is { } special) return special;
        var path = new XPath { Data = Geo(data) };
        if (stroke)
        {
            path.Stroke = B(color);
            path.StrokeThickness = strokeWidth;
            path.StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
            path.StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
            path.StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round;
        }
        else path.Fill = B(color);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>채우고 테두리도 그리는 아이콘 (pear liked · disliked: fill + stroke 1.7 round)</summary>
    public static FrameworkElement IconFilledStroke(string data, double size, Color color)
    {
        if (Special(data, size, color, filled: true) is { } special) return special;
        var path = new XPath
        {
            Data = Geo(data),
            Fill = B(color),
            Stroke = B(color),
            StrokeThickness = 1.7,
            StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round,
            StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
            StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>여러 조각(선 / 채움)으로 된 24×24 아이콘</summary>
    public static FrameworkElement Icon(double size, Color color, params (string Data, bool Stroke)[] parts)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var (data, stroke) in parts)
        {
            var path = new XPath { Data = Geo(data) };
            if (stroke)
            {
                path.Stroke = B(color);
                path.StrokeThickness = 1.7;
                path.StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
                path.StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
                path.StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round;
            }
            else path.Fill = B(color);
            canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>
    /// 썸네일 주소를 크기에 맞게 고치고, 동영상 썸네일(hqdefault 등 4:3 — 위아래 검은 띠)은 띠 없는 16:9(hq720) 로.
    /// fallback 은 hq720 이 없을 때 쓸 mqdefault
    /// </summary>
    public static (string? Url, string? Fallback) ImageUrl(string? url, int decodeWidth)
    {
        var u = Core.Fmt.Thumb(url, Math.Clamp(decodeWidth * 3 / 2, 120, 1000)); // 너무 큰 그림은 받는 데 오래 걸린다
        string? fallback = null;
        if (u != null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(u, @"^https?://i\d?\.ytimg\.com/vi(?:_webp)?/([^/]+)/");
            if (m.Success)
            {
                fallback = $"https://i.ytimg.com/vi/{m.Groups[1].Value}/mqdefault.jpg";
                u = $"https://i.ytimg.com/vi/{m.Groups[1].Value}/hq720.jpg";
            }
        }
        return (string.IsNullOrEmpty(u) ? null : u, fallback);
    }

    public static BitmapImage Image(string? url, int decodeWidth)
    {
        var bmp = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        var (u, fallback) = ImageUrl(url, decodeWidth);
        if (u != null && Uri.TryCreate(u, UriKind.Absolute, out var uri)) bmp.UriSource = uri;
        if (fallback != null)
            bmp.ImageFailed += (_, _) =>
            {
                if (bmp.UriSource?.AbsoluteUri != fallback) bmp.UriSource = new Uri(fallback);
            };
        return bmp;
    }
}

/// <summary>아이콘 모양 (24×24, One UI 선 아이콘)</summary>
public static class Icons
{
    public const string Play = "M7.5 4.8 L19.2 12 L7.5 19.2 Z";
    public const string Pause = "M6.5 5 L10.3 5 L10.3 19 L6.5 19 Z M13.7 5 L17.5 5 L17.5 19 L13.7 19 Z";
    public const string Previous = "M4.8 5.2h2.2v13.6H4.8z M19.2 6.7v10.6c0 .9-1 1.4-1.7.9l-7.6-5.3c-.6-.4-.6-1.4 0-1.8l7.6-5.3c.7-.5 1.7 0 1.7.9z";
    public const string Next = "M17 5.2h2.2v13.6H17z M4.8 6.7v10.6c0 .9 1 1.4 1.7.9l7.6-5.3c.6-.4.6-1.4 0-1.8L6.5 5.8c-.7-.5-1.7 0-1.7.9z";
    public const string Speaker = "M3.3 9.6v4.8c0 .7.5 1.2 1.2 1.2h2.5l4.1 3.5c.7.6 1.7.1 1.7-.8V5.7c0-.9-1-1.4-1.7-.8L7 8.4H4.5c-.7 0-1.2.5-1.2 1.2z";
    public const string Waves = "M15.7 8.9a4.4 4.4 0 0 1 0 6.2 M18.1 6.5a7.8 7.8 0 0 1 0 11";
    public const string MuteX = "M15.9 9.6l4.8 4.8 M20.7 9.6l-4.8 4.8";
    public const string Repeat = "M7.4 5.2h9.2a3.8 3.8 0 0 1 3.8 3.8v6a3.8 3.8 0 0 1-3.8 3.8H7.4a3.8 3.8 0 0 1-3.8-3.8V9a3.8 3.8 0 0 1 3.8-3.8z M14.2 2.6 16.8 5.2 14.2 7.8";
    public const string RepeatOne = "M10.9 10.4l1.4-1v5.2";
    public const string Shuffle = "M3.6 18.6H6.4c4.8 0 6.4-13.2 11.2-13.2h2.8 M3.6 5.4H6.4c4.8 0 6.4 13.2 11.2 13.2h2.8 M17.8 2.8l2.6 2.6-2.6 2.6 M17.8 16l2.6 2.6-2.6 2.6";
    public const string ChevronDown = "M6 9.4 12 15.4l6-6";
    public const string ChevronLeft = "M14.6 6 8.6 12l6 6";
    public const string PlayNextIcon = "M4 6h10 M4 11h7 M4 16h7 M15 11v8l5-4z";
    public const string QueueAdd = "M4 6h12 M4 11h12 M4 16h7 M17 14v6 M14 17h6";
    public const string Album = "M12 3.5a8.5 8.5 0 1 1 0 17a8.5 8.5 0 1 1 0-17z M12 9.5a2.5 2.5 0 1 1 0 5a2.5 2.5 0 1 1 0-5z";
    public const string Person = "M12 4.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7z M5 19.5c.8-3.4 3.6-5.5 7-5.5s6.2 2.1 7 5.5";
    public const string Download = "M12 4v11 M7.5 10.5 12 15l4.5-4.5 M5 19.5h14";
    /// <summary>⋮ — sesl-androidx 원본 경로 (pear icons.ts more)</summary>
    public const string More = "M12,16.75C12.868,16.75 13.5,17.382 13.5,18.25C13.5,19.118 12.868,19.75 12,19.75C11.211,19.75 10.5,19.118 10.5,18.25C10.5,17.382 11.132,16.75 12,16.75ZM12,10.5C12.868,10.5 13.5,11.132 13.5,12C13.5,12.868 12.868,13.5 12,13.5C11.211,13.5 10.5,12.868 10.5,12C10.5,11.132 11.132,10.5 12,10.5ZM12,4.25C12.789,4.25 13.5,4.882 13.5,5.75C13.5,6.618 12.868,7.25 12,7.25C11.211,7.25 10.5,6.618 10.5,5.75C10.5,4.882 11.132,4.25 12,4.25Z";
    /// <summary>pear icons.ts THUMB (좋아요). 싫어요는 180° 돌린 것</summary>
    /// <summary>좋아요 · 싫어요 — YouTube 현재(Cairo) 아이콘. 채움 도형이라 Theme.Icon 이 이 표시를 보고 따로 그린다</summary>
    public const string ThumbUp = "@thumb_up";
    public const string ThumbDown = "@thumb_down";
    public const string MinusCircle = "M12 3.6a8.4 8.4 0 1 1 0 16.8a8.4 8.4 0 1 1 0-16.8z M8 12h8";
    public const string Share = "M14 5l6 6-6 6 M20 11h-7.5c-4 0-7.5 3-8 8";
    public const string Check = "M6 12.5l4 4 8-9";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Info = "M12 3.6a8.4 8.4 0 1 1 0 16.8a8.4 8.4 0 1 1 0-16.8z M12 11v5.5 M12 7.6v.6";
    public const string Star = "M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.7L12 16.8l-5.1 2.7 1-5.7-4.1-4 5.7-.8z";
    public const string QueueClose = "M4 6.5h12 M4 11.5h12 M4 16.5h7 M15.5 15l4.5 4.5 M20 15l-4.5 4.5";
    public const string Stats = "M5 4.5h14v15H5z M8.5 16v-4 M12 16V8 M15.5 16v-6";
    public const string LibraryAdd = "M4.5 6h11 M4.5 10.5h11 M4.5 15h6 M17.5 13v7 M14 16.5h7";
    public const string LibrarySaved = "M4.5 6h11 M4.5 10.5h11 M4.5 15h6 M13.5 17l2.5 2.5 4.5-5";
    public const string PlaylistAddPath = "M4.5 6.5h12M4.5 11.5h12M4.5 16.5h7M17.5 13.5v7M14 17h7";
    public const string Flag = "M6 20.5V4.5 M6 5h10.5l-2 4 2 4H6";
    public const string Pin = "M14.5 4.5l5 5-3 1.5-3.5 3.5.5 3.5-1.5 1.5-8-8 1.5-1.5 3.5.5 3.5-3.5z M4.5 19.5l4-4";
    public const string Moon = "M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z";
    public const string Bell = "M6 16.5V11a6 6 0 0 1 12 0v5.5l1.5 1.5h-15z M10 20.5h4";
    public const string Keyboard = "M4 6.5h16v11H4z M7.5 10h1 M11.5 10h1 M15.5 10h1 M8 14h8";
    public const string SortLines = "M4 7h16 M4 12h11 M4 17h6";
    public const string History = "M4.5 12a7.5 7.5 0 1 0 2.2-5.3 M4.5 4.5v3h3 M12 8v4.2l2.8 1.8";
    public const string ChevronRight = "M9.4 6l6 6-6 6";
    public const string Link = "M10.2 13.8a3.6 3.6 0 0 0 5.1 0l3-3a3.6 3.6 0 0 0-5.1-5.1l-1 1 M13.8 10.2a3.6 3.6 0 0 0-5.1 0l-3 3a3.6 3.6 0 0 0 5.1 5.1l1-1";
    public const string Radio = "M12 10.6a1.4 1.4 0 1 1 0 2.8a1.4 1.4 0 1 1 0-2.8z M8.6 8.6a4.8 4.8 0 0 0 0 6.8 M15.4 8.6a4.8 4.8 0 0 1 0 6.8 M5.8 5.8a8.8 8.8 0 0 0 0 12.4 M18.2 5.8a8.8 8.8 0 0 1 0 12.4";
    public const string ChevronUp = "M6 14.6 12 8.6l6 6";
    /// <summary>미니 플레이어 (큰 네모 안 오른쪽 아래 작은 네모)</summary>
    /// <summary>미니 플레이어 — 재생 바 다른 아이콘과 같은 크기(가로 4 ~ 20 · 세로 4.8 ~ 19.2, 가운데 12)</summary>
    public const string Pip = "M5.5 4.8h13a1.5 1.5 0 0 1 1.5 1.5v11.4a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.7V6.3a1.5 1.5 0 0 1 1.5-1.5z M12.4 12h4.6v3.8h-4.6z";
    /// <summary>재생 속도 (계기판)</summary>
    public const string Speed = "M4.2 17.5a8.5 8.5 0 1 1 15.6 0 M12 13.2l4-4.2 M12 13.2a1 1 0 1 0 0.01 0";
    public const string Search = "M10.8 4.2a6.6 6.6 0 1 1 0 13.2 6.6 6.6 0 0 1 0-13.2z M15.6 15.6 20 20";
    public const string Home = "M4.5 10.2 12 4.2l7.5 6v8.4c0 .7-.5 1.2-1.2 1.2H14v-5.4h-4v5.4H5.7c-.7 0-1.2-.5-1.2-1.2z";
    public const string Trash = "M5 7h14 M10 7V5.2h4V7 M6.8 7l.9 11.6a1.6 1.6 0 0 0 1.6 1.4h5.4a1.6 1.6 0 0 0 1.6-1.4L17.2 7 M10.2 10.5v6 M13.8 10.5v6";
    public const string Fullscreen = "M4.5 9V4.5H9 M15 4.5h4.5V9 M19.5 15v4.5H15 M9 19.5H4.5V15";
    public const string FullscreenExit = "M9 4.5V9H4.5 M15 4.5V9h4.5 M19.5 15H15v4.5 M4.5 15H9v4.5";
    public const string Close = "M6.5 6.5l11 11 M17.5 6.5l-11 11";
    // 계정 메뉴 — 줄마다 다른 모양 (예전엔 스피커 하나를 네 줄이 같이 써서 구분이 안 됐다)
    /// <summary>출력 장치 (스피커 + 소리 물결)</summary>
    public const string Output = Speaker + " " + Waves;
    /// <summary>지수 볼륨 (축 + 휘는 곡선)</summary>
    public const string Curve = "M4.5 4.5v15h15 M7.5 16.5c5.5 0 9-3.5 11-10";
    /// <summary>높은 음질 (이퀄라이저 막대)</summary>
    public const string Bars = "M6 15v-3 M10 18V6 M14 16V8 M18 14v-4";
    /// <summary>음량 표준화 (위아래 한계선 사이의 고른 막대)</summary>
    public const string Level = "M4 5.5h16 M4 18.5h16 M8 10v4 M12 9v6 M16 10v4";
    public const string Storage = "M5 5h14v14H5z M5 13h14 M15 16h1.5";
    public const string Folder = "M3.5 7A1.5 1.5 0 0 1 5 5.5h4l2 2h8A1.5 1.5 0 0 1 20.5 9v8.5A1.5 1.5 0 0 1 19 19H5a1.5 1.5 0 0 1-1.5-1.5z";

    // ── pear-desktop One UI 테마(icons.ts)와 같은 아이콘 ──
    public static readonly (string, bool)[] HomeOutline = [("M4.2 10.3 11.2 4.7c.5-.4 1.1-.4 1.6 0l7 5.6c.3.2.4.6.4 1v7.9c0 .8-.6 1.4-1.4 1.4h-3.6v-5.1c0-.6-.4-1-1-1h-4.4c-.6 0-1 .4-1 1v5.1H5.2c-.8 0-1.4-.6-1.4-1.4v-7.9c0-.4.1-.8.4-1z", true)];
    public static readonly (string, bool)[] HomeFilled = [("M3.4 10.3 10.8 4.4c.7-.6 1.7-.6 2.4 0l7.4 5.9c.4.3.6.8.6 1.3v7.6c0 1.2-1 2.1-2.1 2.1h-3.7c-.4 0-.8-.4-.8-.8v-4.7c0-.6-.4-1-1-1h-3.2c-.6 0-1 .4-1 1v4.7c0 .4-.4.8-.8.8H4.9c-1.2 0-2.1-1-2.1-2.1v-7.6c0-.5.2-1 .6-1.3z", false)];
    public static readonly (string, bool)[] ExploreOutline = [("M12 3.6a8.4 8.4 0 1 1 0 16.8a8.4 8.4 0 1 1 0-16.8z", true), ("M15.4 8.6l-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z", false)];
    public static readonly (string, bool)[] ExploreFilled = [("F0 M12 2.8a9.2 9.2 0 1 0 0 18.4 9.2 9.2 0 0 0 0-18.4z M15.4 8.6l-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z", false)];
    public static readonly (string, bool)[] LibraryOutline = [
        ("M5.2 4.4h1.8a1.2 1.2 0 0 1 1.2 1.2v12.8a1.2 1.2 0 0 1-1.2 1.2H5.2a1.2 1.2 0 0 1-1.2-1.2V5.6a1.2 1.2 0 0 1 1.2-1.2z", true),
        ("M11.2 4.4h1.8a1.2 1.2 0 0 1 1.2 1.2v12.8a1.2 1.2 0 0 1-1.2 1.2h-1.8a1.2 1.2 0 0 1-1.2-1.2V5.6a1.2 1.2 0 0 1 1.2-1.2z", true),
        ("M14.8 5.6l2.3-.7c.6-.2 1.3.2 1.4.8l3 12.3c.1.6-.2 1.2-.8 1.4l-2.3.7c-.6.2-1.3-.2-1.4-.8l-3-12.3c-.1-.6.2-1.2.8-1.4z", true)];
    public static readonly (string, bool)[] LibraryFilled = [
        ("M4.8 3.6h2.4a1.6 1.6 0 0 1 1.6 1.6v13.6a1.6 1.6 0 0 1-1.6 1.6H4.8a1.6 1.6 0 0 1-1.6-1.6V5.2a1.6 1.6 0 0 1 1.6-1.6z", false),
        ("M11.2 3.6h2.4a1.6 1.6 0 0 1 1.6 1.6v13.6a1.6 1.6 0 0 1-1.6 1.6h-2.4a1.6 1.6 0 0 1-1.6-1.6V5.2a1.6 1.6 0 0 1 1.6-1.6z", false),
        ("M13.6 5.4l2.6-.8c.8-.2 1.7.3 1.9 1.1l3 12.4c.2.8-.3 1.7-1.1 1.9l-2.6.8c-.8.2-1.7-.3-1.9-1.1l-3-12.4c-.2-.8.3-1.7 1.1-1.9z", false)];
    public static readonly (string, bool)[] Headphones = [
        ("M4.6 14.6v-2.4a7.4 7.4 0 0 1 14.8 0v2.4", true),
        ("M5.6 13.4H6.2a2 2 0 0 1 2 2V18.4a2 2 0 0 1-2 2H5.6a2 2 0 0 1-2-2V15.4a2 2 0 0 1 2-2z", false),
        ("M17.8 13.4H18.4a2 2 0 0 1 2 2V18.4a2 2 0 0 1-2 2H17.8a2 2 0 0 1-2-2V15.4a2 2 0 0 1 2-2z", false)];
    public static readonly (string, bool)[] Video = [
        ("M6.4 5.4H17.6a3 3 0 0 1 3 3V15.6a3 3 0 0 1-3 3H6.4a3 3 0 0 1-3-3V8.4a3 3 0 0 1 3-3z", true),
        ("M10.2 9.3v5.4c0 .6.6.9 1.1.6l4.2-2.7c.4-.3.4-.9 0-1.2l-4.2-2.7c-.5-.3-1.1 0-1.1.6z", false)];
    public static readonly (string, bool)[] Drawer = [("M4.5 7h15M4.5 12h15M4.5 17h15", true)];
    public static readonly (string, bool)[] SearchSesl = [("M10.625,5C7.518,5 5,7.519 5,10.625C5,13.731 7.518,16.25 10.625,16.25C13.732,16.25 16.25,13.731 16.25,10.625C16.25,7.519 13.732,5 10.625,5ZM3.5,10.625C3.5,6.69 6.69,3.5 10.625,3.5C14.56,3.5 17.75,6.69 17.75,10.625C17.75,12.322 17.156,13.881 16.166,15.105L20.705,19.645C20.998,19.937 20.998,20.412 20.705,20.705C20.412,20.998 19.937,20.998 19.645,20.705L15.105,16.165C13.881,17.156 12.322,17.75 10.625,17.75C6.69,17.75 3.5,14.56 3.5,10.625Z", false)];
    public static readonly (string, bool)[] PlaylistAdd = [("M4.5 6.5h12M4.5 11.5h12M4.5 16.5h7M17.5 13.5v7M14 17h7", true)];
    public static readonly (string, bool)[] Settings = [
        ("M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6z", true),
        ("M10.3 3.6h3.4l.5 2.4 1.6.9 2.3-.8 1.7 2.9-1.8 1.7v1.8l1.8 1.7-1.7 2.9-2.3-.8-1.6.9-.5 2.4h-3.4l-.5-2.4-1.6-.9-2.3.8-1.7-2.9 1.8-1.7v-1.8L4.2 8.9l1.7-2.9 2.3.8 1.6-.9z", true)];
}
