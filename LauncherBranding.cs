namespace L2TribeLauncher;

// Edit this file to change launcher copy, colors and public links.
internal static class LauncherBranding
{
    public const string WindowTitle = "L2 Tribe";
    public const string BrandName = "L2 TRIBE";
    public const string BrandTagline = "INTERLUDE COMMUNITY";
    public const string Chronicle = "INTERLUDE";
    public const string Rate = "x30";

    public const string HeroResource = "L2TribeLauncher.Assets.launcher-hero.jpg";
    public const string DiscordIconResource = "L2TribeLauncher.Assets.social.discord.png";

    public const string DiscordServerId = "1514807146695495690";
    public const string DiscordUrl = "https://discord.gg/N9wfNUC";

    public static readonly Color Canvas = FromHex("#1D2026");
    public static readonly Color Surface = FromHex("#252930");
    public static readonly Color SurfaceRaised = FromHex("#2E323A");
    public static readonly Color Accent = FromHex("#EBAA39");
    public static readonly Color Text = FromHex("#F6F1E5");
    public static readonly Color MutedText = FromHex("#AEB3B8");

    private static Color FromHex(string value) => ColorTranslator.FromHtml(value);
}
