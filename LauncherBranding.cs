namespace L2InterludeUpdater;

// Edit this file to change launcher copy, colors and public links.
internal static class LauncherBranding
{
    public const string WindowTitle = "L2 Hamburgo";
    public const string Chronicle = "INTERLUDE";
    public const string Rate = "x30";

    public const string LogoResource = "L2InterludeUpdater.Assets.l2-hamburgo-logo.png";
    public const string HeroResource = "L2InterludeUpdater.Assets.launcher-hero.jpg";
    public const string DiscordIconResource = "L2InterludeUpdater.Assets.social.discord.png";
    public const string InstagramIconResource = "L2InterludeUpdater.Assets.social.instagram.png";
    public const string FacebookIconResource = "L2InterludeUpdater.Assets.social.facebook.png";
    public const string TwitchIconResource = "L2InterludeUpdater.Assets.social.twitch.png";

    public const string WebsiteUrl = "https://l2hamburgo.surge.sh";
    public const string DiscordServerId = "1514807146695495690";
    public const string DiscordUrl = "https://discord.gg/N9wfNUC";
    public const string InstagramUrl = "https://www.instagram.com/l2hamburgo/";
    public const string FacebookUrl = "https://www.facebook.com/L2-Hamburgo-815982251869041/";
    public const string TwitchUrl = "https://twitch.tv/l2hamburgo";

    public static readonly Color Canvas = FromHex("#1D2026");
    public static readonly Color Surface = FromHex("#252930");
    public static readonly Color SurfaceRaised = FromHex("#2E323A");
    public static readonly Color Accent = FromHex("#EBAA39");
    public static readonly Color Text = FromHex("#F6F1E5");
    public static readonly Color MutedText = FromHex("#AEB3B8");

    private static Color FromHex(string value) => ColorTranslator.FromHtml(value);
}
