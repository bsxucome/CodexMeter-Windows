using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.IO;
using System.Text;

namespace CodexMeter.Core
{
    public sealed class Settings
    {
        public string CliPath { get; set; }
        public string SelectedWindow { get; set; }
        public string Theme { get; set; }
        public bool TaskbarIndicator { get; set; }
        public double TaskbarPosition { get; set; }
        public string TaskbarDock { get; set; }
        public bool HistoryEnabled { get; set; }
        public int HistoryRetentionDays { get; set; }
        public string Language { get; set; }
        public string IndicatorStyle { get; set; }
        public bool ShowTokens { get; set; }
        public bool ShowTime { get; set; }
        public bool ShowPace { get; set; }
        public bool ShowReset { get; set; }
        public string[] HiddenWindows { get; set; }
        public string[] WindowOrder { get; set; }
        public bool ShowWeeklyHistory { get; set; }
        public IndicatorAppearance Appearance { get; set; }
        public bool ShowCredits { get; set; }
        public bool ShowResetCredits { get; set; }
        public string[] SectionOrder { get; set; }
        public bool Notifications { get; set; }
        public bool NotifyLowQuota { get; set; }
        public bool NotifyOverPace { get; set; }
        public int NotificationThreshold { get; set; }
        public int NotificationCooldownMinutes { get; set; }
        public bool LaunchAtLogin { get; set; }
        public string UpdateFeed { get; set; }
        public string UpdatePublicKey { get; set; }
        public string UpdateChannel { get; set; }
        public bool UpdateAutomatic { get; set; }
        public bool UpdateAutoInstall { get; set; }
        public static readonly string[] DefaultSections = { "Quota", "Credits", "Resets", "History", "Tokens" };
        public static string[] ValidSections(IEnumerable<string> values)
        { return (values ?? new string[0]).Where(DefaultSections.Contains).Concat(DefaultSections).Distinct().ToArray(); }
        public static string ValidStyle(string value)
        { return new[] { "Horizontal", "Stacked", "Percentage", "Progress" }.Contains(value) ? value : "Rings"; }
        public static string ValidLanguage(string value) { return value == "en" || value == "zh-TW" ? value : "zh-CN"; }
        public IList<QuotaWindow> VisibleWindows(IList<QuotaWindow> windows)
        {
            var order = WindowOrder ?? new string[0];
            return windows.Where(IsWindowVisible).OrderBy(w => {
                int index = Array.IndexOf(order, w.Id); return index < 0 ? int.MaxValue : index;
            }).ToList();
        }
        public bool IsWindowVisible(QuotaWindow window)
        {
            if ((HiddenWindows ?? new string[0]).Contains(window.Id)) return false;
            bool reserve = (window.Bucket ?? "").IndexOf("reserve", StringComparison.OrdinalIgnoreCase) >= 0 && window.DurationMinutes == 10080;
            return !reserve || (WindowOrder ?? new string[0]).Contains(window.Id);
        }
        public static string ResolveDock(string value, double position)
        {
            if (value == "Auto" || value == "StartLeft" || value == "StartRight" || value == "Custom") return value;
            return position >= 0 && position < .5 ? "StartLeft" : position >= 0 ? "Custom" : "Auto";
        }
        public Settings() { CliPath = ""; SelectedWindow = ""; Theme = "System"; TaskbarIndicator = true; TaskbarPosition = -1; TaskbarDock = "Auto"; HistoryEnabled = true;
            Language = "zh-CN"; IndicatorStyle = "Rings"; ShowTokens = true; ShowTime = true; ShowPace = true; ShowReset = true;
            HiddenWindows = new string[0]; WindowOrder = new string[0]; ShowWeeklyHistory = true; Appearance = new IndicatorAppearance();
            ShowCredits = true; ShowResetCredits = true; SectionOrder = ValidSections(null);
            NotifyLowQuota = true; NotifyOverPace = true; NotificationThreshold = 20; NotificationCooldownMinutes = 30;
            UpdateFeed = WindowsReleaseSource.GitHubFeed; UpdatePublicKey = ""; UpdateChannel = "stable"; }
        public Settings Copy()
        {
            var copy = (Settings)MemberwiseClone();
            copy.HiddenWindows = (string[])(HiddenWindows ?? new string[0]).Clone();
            copy.WindowOrder = (string[])(WindowOrder ?? new string[0]).Clone();
            copy.SectionOrder = ValidSections(SectionOrder);
            copy.Appearance = (Appearance ?? new IndicatorAppearance()).Copy(); return copy;
        }
    }

    public sealed class SettingsStore
    {
        public string FilePath { get; private set; }
        public SettingsStore(string path) { FilePath = path; }
        public static SettingsStore Default()
        {
            return new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexMeter", "settings.json"));
        }
        public Settings Load(out string warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException();
                var json = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                var theme = Json.Text(json, "theme");
                return new Settings {
                    CliPath = Json.Text(json, "cliPath") ?? "",
                    SelectedWindow = Json.Text(json, "selectedWindow") ?? "",
                    Theme = theme == "Light" || theme == "Dark" ? theme : "System",
                    TaskbarIndicator = !object.Equals(Json.Get(json, "taskbarIndicator"), false),
                    TaskbarPosition = Math.Max(-1, Math.Min(1, Json.Number(json, "taskbarPosition") ?? -1)),
                    TaskbarDock = Settings.ResolveDock(Json.Text(json, "taskbarDock"), Json.Number(json, "taskbarPosition") ?? -1),
                    HistoryEnabled = !object.Equals(Json.Get(json, "historyEnabled"), false),
                    HistoryRetentionDays = Retention(json),
                    Language = Settings.ValidLanguage(Json.Text(json, "language")),
                    IndicatorStyle = Settings.ValidStyle(Json.Text(json, "indicatorStyle")),
                    ShowTokens = !object.Equals(Json.Get(json, "showTokens"), false),
                    ShowTime = !object.Equals(Json.Get(json, "showTime"), false),
                    ShowPace = !object.Equals(Json.Get(json, "showPace"), false),
                    ShowReset = !object.Equals(Json.Get(json, "showReset"), false),
                    HiddenWindows = Strings(json, "hiddenWindows"), WindowOrder = Strings(json, "windowOrder"),
                    ShowWeeklyHistory = !object.Equals(Json.Get(json, "showWeeklyHistory"), false),
                    ShowCredits = !object.Equals(Json.Get(json, "showCredits"), false),
                    ShowResetCredits = !object.Equals(Json.Get(json, "showResetCredits"), false),
                    SectionOrder = Settings.ValidSections(Strings(json, "sectionOrder")),
                    Notifications = object.Equals(Json.Get(json, "notifications"), true),
                    NotifyLowQuota = !object.Equals(Json.Get(json, "notifyLowQuota"), false),
                    NotifyOverPace = !object.Equals(Json.Get(json, "notifyOverPace"), false),
                    NotificationThreshold = (int)Math.Max(1, Math.Min(99, Json.Number(json, "notificationThreshold") ?? 20)),
                    NotificationCooldownMinutes = (int)Math.Max(1, Math.Min(1440, Json.Number(json, "notificationCooldownMinutes") ?? 30)),
                    LaunchAtLogin = object.Equals(Json.Get(json, "launchAtLogin"), true),
                    UpdateFeed = WindowsReleaseSource.DefaultFeed(Bounded(Json.Text(json, "updateFeed")), Bounded(Json.Text(json, "updatePublicKey"))), UpdatePublicKey = Bounded(Json.Text(json, "updatePublicKey")),
                    UpdateChannel = Json.Text(json, "updateChannel") == "beta" ? "beta" : "stable",
                    UpdateAutomatic = !string.IsNullOrWhiteSpace(Json.Text(json, "updateFeed")) && object.Equals(Json.Get(json, "updateAutomatic"), true),
                    UpdateAutoInstall = !string.IsNullOrWhiteSpace(Json.Text(json, "updateFeed")) && object.Equals(Json.Get(json, "updateAutoInstall"), true),
                    Appearance = IndicatorAppearance.FromJson(Json.Object(json, "appearance"))
                };
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                    ex is InvalidOperationException || ex is FormatException || ex.GetType().Name == "JsonException")) throw;
                warning = "设置文件无法读取，已使用默认设置。保存设置可修复此问题。";
                return new Settings();
            }
        }
        private static int Retention(Dictionary<string, object> json)
        {
            double number = Json.Number(json, "historyRetentionDays") ?? 0;
            return number == 7 || number == 30 || number == 90 || number == 365 ? (int)number : 0;
        }
        private static string Bounded(string value) { return value == null ? "" : value.Substring(0, Math.Min(value.Length, 4096)); }
        private static string[] Strings(Dictionary<string, object> json, string key)
        {
            var items = Json.Get(json, key) as IEnumerable;
            if (items == null || items is string || items is IDictionary) return new string[0];
            return items.Cast<object>().OfType<string>().Where(s => s.Length > 0 && s.Length <= 128).Distinct().Take(64).ToArray();
        }
        public void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temporary = FilePath + ".tmp";
            var content = Json.Encode(new Dictionary<string, object> {
                { "cliPath", settings.CliPath ?? "" }, { "selectedWindow", settings.SelectedWindow ?? "" },
                { "theme", settings.Theme ?? "System" }, { "taskbarIndicator", settings.TaskbarIndicator },
                { "taskbarPosition", Math.Max(-1, Math.Min(1, settings.TaskbarPosition)) },
                { "taskbarDock", Settings.ResolveDock(settings.TaskbarDock, settings.TaskbarPosition) },
                { "historyEnabled", settings.HistoryEnabled },
                { "historyRetentionDays", HistoryStore.ValidRetention(settings.HistoryRetentionDays) },
                { "language", Settings.ValidLanguage(settings.Language) }, { "indicatorStyle", Settings.ValidStyle(settings.IndicatorStyle) },
                { "showTokens", settings.ShowTokens }, { "showTime", settings.ShowTime }, { "showPace", settings.ShowPace }, { "showReset", settings.ShowReset },
                { "hiddenWindows", settings.HiddenWindows ?? new string[0] }, { "windowOrder", settings.WindowOrder ?? new string[0] },
                { "showWeeklyHistory", settings.ShowWeeklyHistory }, { "appearance", (settings.Appearance ?? new IndicatorAppearance()).ToJson() }
                , { "showCredits", settings.ShowCredits }, { "showResetCredits", settings.ShowResetCredits },
                { "sectionOrder", Settings.ValidSections(settings.SectionOrder) }, { "notifications", settings.Notifications },
                { "notifyLowQuota", settings.NotifyLowQuota }, { "notifyOverPace", settings.NotifyOverPace },
                { "notificationThreshold", Math.Max(1, Math.Min(99, settings.NotificationThreshold)) },
                { "notificationCooldownMinutes", Math.Max(1, Math.Min(1440, settings.NotificationCooldownMinutes)) },
                { "launchAtLogin", settings.LaunchAtLogin },
                { "updateFeed", Bounded(settings.UpdateFeed) }, { "updatePublicKey", Bounded(settings.UpdatePublicKey) },
                { "updateChannel", settings.UpdateChannel == "beta" ? "beta" : "stable" }, { "updateAutomatic", settings.UpdateAutomatic }, { "updateAutoInstall", settings.UpdateAutoInstall }
            });
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
    }
}
