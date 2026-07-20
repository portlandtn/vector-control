public sealed class NotifierOptions
{
    public const string Section = "Notifier";
    public string ApiKey { get; set; } = "";
    public string TimeZone { get; set; } = "America/Chicago";
    public TimeOnly QuietHoursStart { get; set; } = new(22, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(7, 0);
    public bool CriticalBypassesQuietHours { get; set; } = true;
    public string QuietHoursBehavior { get; set; } = "queue";
    public int MaximumQueuedNotifications { get; set; } = 10;
    public int DedupeRetentionHours { get; set; } = 168;
    public int StateRetentionDays { get; set; } = 90;
    public int DefaultCooldownMinutes { get; set; } = 30;
    public bool HumorousLinesEnabled { get; set; } = true;
    public int SnarkLevel { get; set; } = 1;
    public bool PackageShipmentAnnouncementsEnabled { get; set; }
    public bool MeetingImminentRemindersEnabled { get; set; } = true;
    public int SlipboxBacklogThreshold { get; set; } = 10;
    public Dictionary<string, bool> Events { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EmotionAnimations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WirePodOptions WirePod { get; set; } = new();
}

public sealed class WirePodOptions
{
    public string BaseUrl { get; set; } = "http://192.168.50.205:8080";
    public string Serial { get; set; } = "007064c9";
    public int AssumeDelayMilliseconds { get; set; } = 300;
    public int SpeechDelayMilliseconds { get; set; } = 1000;
}
