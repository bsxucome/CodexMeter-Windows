using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodexMeter.Core
{
    public sealed class IndicatorAppearance
    {
        public double RingSize { get; set; }
        public double OuterStroke { get; set; }
        public double InnerStroke { get; set; }
        public double RingGap { get; set; }
        public double FontSize { get; set; }
        public double CaptionSize { get; set; }
        public double Spacing { get; set; }
        public double BarWidth { get; set; }
        public bool ShowCaption { get; set; }
        public string TimeColor { get; set; }
        public IndicatorAppearance() { RingSize = 18; OuterStroke = 3; InnerStroke = 2; RingGap = 5; FontSize = 13; CaptionSize = 9; Spacing = 4; BarWidth = 28; ShowCaption = true; TimeColor = "Blue"; }
        private static double Bound(double number, double min, double max, double fallback)
        { return double.IsNaN(number) || double.IsInfinity(number) ? fallback : Math.Max(min, Math.Min(max, number)); }
        public IndicatorAppearance Copy() { return (IndicatorAppearance)MemberwiseClone(); }
        public IndicatorAppearance Bounded()
        {
            var value = Copy();
            value.RingSize = Bound(RingSize, 16, 24, 18); value.OuterStroke = Bound(OuterStroke, 1, 4, 3);
            value.InnerStroke = Bound(InnerStroke, 1, 3, 2);
            value.RingGap = Bound(RingGap, 2, Math.Min(6, (value.RingSize - value.OuterStroke) / 2 - value.InnerStroke / 2 - 1), 5);
            value.FontSize = Bound(FontSize, 10, 15, 13); value.CaptionSize = Bound(CaptionSize, 8, 11, 9);
            value.CaptionSize = Math.Min(value.CaptionSize, 23 - value.FontSize);
            value.Spacing = Bound(Spacing, 0, 8, 4); value.BarWidth = Bound(BarWidth, 16, 48, 28);
            value.TimeColor = new[] { "Blue", "Cyan", "Purple" }.Contains(TimeColor) ? TimeColor : "Blue";
            return value;
        }
        public static IndicatorAppearance Preset(string name)
        {
            if (name == "Compact") return new IndicatorAppearance { RingSize = 16, OuterStroke = 2.5, InnerStroke = 1.5, RingGap = 4, FontSize = 11, CaptionSize = 8, Spacing = 2, BarWidth = 22 };
            if (name == "Bold") return new IndicatorAppearance { RingSize = 22, OuterStroke = 4, InnerStroke = 3, RingGap = 5, FontSize = 14, Spacing = 5, BarWidth = 34 };
            return new IndicatorAppearance();
        }
        public Dictionary<string, object> ToJson()
        {
            var a = Bounded(); return new Dictionary<string, object> {
                { "ringSize", a.RingSize }, { "outerStroke", a.OuterStroke }, { "innerStroke", a.InnerStroke }, { "ringGap", a.RingGap },
                { "fontSize", a.FontSize }, { "captionSize", a.CaptionSize }, { "spacing", a.Spacing }, { "barWidth", a.BarWidth },
                { "showCaption", a.ShowCaption }, { "timeColor", a.TimeColor }
            };
        }
        public static IndicatorAppearance FromJson(Dictionary<string, object> data)
        {
            return new IndicatorAppearance {
                RingSize = Json.Number(data, "ringSize") ?? 18, OuterStroke = Json.Number(data, "outerStroke") ?? 3,
                InnerStroke = Json.Number(data, "innerStroke") ?? 2, RingGap = Json.Number(data, "ringGap") ?? 5,
                FontSize = Json.Number(data, "fontSize") ?? 13, CaptionSize = Json.Number(data, "captionSize") ?? 9,
                Spacing = Json.Number(data, "spacing") ?? 4, BarWidth = Json.Number(data, "barWidth") ?? 28,
                ShowCaption = !object.Equals(Json.Get(data, "showCaption"), false), TimeColor = Json.Text(data, "timeColor") ?? "Blue"
            }.Bounded();
        }
        public string Export()
        { return Json.Encode(new Dictionary<string, object> { { "format", "CodexMeter.Appearance" }, { "version", 1 }, { "appearance", ToJson() } }); }
        public static IndicatorAppearance Import(string text)
        {
            if (text == null || text.Length > 16384) throw new InvalidDataException("Appearance file is too large.");
            var json = Json.Parse(text);
            if (Json.Text(json, "format") != "CodexMeter.Appearance" || Json.Number(json, "version") != 1 || Json.Object(json, "appearance") == null)
                throw new InvalidDataException("Unsupported appearance format.");
            return FromJson(Json.Object(json, "appearance"));
        }
    }
}
