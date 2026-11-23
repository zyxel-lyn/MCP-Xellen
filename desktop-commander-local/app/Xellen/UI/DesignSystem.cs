using System.Drawing;
using McpXellen.Core;

namespace McpXellen.UI;

/// <summary>Official MCP Xellen palette + semantic status colors (PRD §6). Central tokens only —
/// individual forms must not hardcode ad-hoc colors.</summary>
public static class DesignSystem
{
    public static readonly Color Slate = ColorTranslator.FromHtml("#5D737E");      // secondary surface
    public static readonly Color Orange = ColorTranslator.FromHtml("#FF8811");     // primary action / active
    public static readonly Color Ice = ColorTranslator.FromHtml("#C0FDFB");        // info / connection highlight
    public static readonly Color Sand = ColorTranslator.FromHtml("#F4D06F");       // warning / attention
    public static readonly Color NearBlack = ColorTranslator.FromHtml("#020402");  // dark foundation

    // Semantic status colors — kept accessible against the palette backgrounds.
    public static readonly Color Success = Color.FromArgb(34, 139, 84);
    public static readonly Color Error = Color.FromArgb(192, 57, 43);
    public static readonly Color Neutral = Color.FromArgb(96, 125, 139);

    public static Color StatusColor(ActivityStatus s) => s switch
    {
        ActivityStatus.Success => Success,
        ActivityStatus.Failed => Error,
        ActivityStatus.Timeout => Error,
        ActivityStatus.Running => Orange,
        ActivityStatus.Queued => Neutral,
        ActivityStatus.Cancelled => Neutral,
        _ => Neutral,
    };

    public static Font Heading { get; } = new("Segoe UI", 16f, FontStyle.Bold);
    public static Font Body { get; } = new("Segoe UI", 9f, FontStyle.Regular);
    public static Font Mono { get; } = new("Consolas", 8.5f, FontStyle.Regular);
}
