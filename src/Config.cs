namespace SocialTapXboxActivity;

internal static class Config
{
    /// <summary>
    /// Public OAuth client ID from the SocialTap developer dashboard.
    /// Public clients have no secret, so this value is safe to ship in the app.
    /// </summary>
    public const string ClientId = "stu_91zLooq0VqpCI95IGpiXBUQtSojjrFyglkYDPprfSJI";

    public const string BaseUrl = "https://www.socialtap.social";

    /// <summary>Loopback callback registered on the client. Must match exactly.</summary>
    public const int CallbackPort = 53682;
    public const string RedirectUri = "http://127.0.0.1:53682/callback";

    /// <summary>Every scope the client was created with.</summary>
    public static readonly string[] Scopes =
    {
        "chat.read", "chat.send", "chat.manage", "chat.folders", "bridge.conversations",
        "themes.read", "themes.manage", "status.read", "integration.status", "status.manage",
        "presence.write", "voice.call", "voice.audio", "account.read", "account.manage",
        "friends.read", "friends.manage", "shop.read", "shop.purchase", "inventory.read", "inventory.manage",
    };

    public static readonly TimeSpan GameScanInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(60);
}
