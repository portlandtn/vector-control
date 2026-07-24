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
    public PlayModeOptions PlayMode { get; set; } = new();
}

public sealed class WirePodOptions
{
    public string BaseUrl { get; set; } = "http://192.168.50.205:8080";
    public string Serial { get; set; } = "007064c9";
    public int AssumeDelayMilliseconds { get; set; } = 300;
    public int SpeechDelayMilliseconds { get; set; } = 1000;
}

public sealed class PlayModeOptions
{
    public bool Enabled { get; set; } = true;
    public int SessionMinutes { get; set; } = 30;
    public int PromptIntervalSeconds { get; set; } = 90;
    public string IntentToken { get; set; } = "";
    public List<string> WelcomePhrases { get; set; } =
    [
        "Hi Izzy! Let's play. Can you show me your biggest smile?",
        "Izzy! Play mode activated. What is your favorite color today?",
        "Hi Izzy! I am ready to play. Can you do one big stretch?"
    ];
    public List<string> Prompts { get; set; } =
    [
        "Izzy, can you find something blue?",
        "Can you count to five with me? One, two, three, four, five!",
        "What sound does a cow make?",
        "Can you hop three times?",
        "Show me your silliest dance move!",
        "Can you find something shaped like a circle?",
        "What is your favorite animal?",
        "Take a big breath in, and slowly blow it out.",
        "Can you clap your hands four times?",
        "Tell me one thing that made you happy today.",
        "Can you pretend to be as tall as a giraffe?",
        "What color would you paint a friendly dragon?"
    ];
}
