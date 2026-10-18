using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static partial class Program
    {
        private static void IndicatorClicks()
        {
            var gesture = new IndicatorGesture();
            gesture.Begin(100, 100, 4, 4);
            Check(gesture.End(100, 100) && !gesture.IsPressed, "indicator stationary press opens details once");
            Check(!gesture.End(100, 100), "duplicate mouse-up cannot open details twice");
            gesture.Begin(100, 100, 4, 4); gesture.Move(102, 102);
            Check(gesture.End(103, 101), "small pointer jitter remains a click");
            gesture.Begin(100, 100, 4, 4); gesture.Move(104, 100);
            Check(gesture.IsDragging && !gesture.End(110, 100), "horizontal drag does not open details");
            gesture.Begin(100, 100, 4, 4); gesture.Move(100, 105);
            Check(!gesture.End(100, 105), "vertical drag does not become an accidental click");
            gesture.Begin(100, 100, 4, 4); gesture.Move(110, 100); gesture.Move(100, 100);
            Check(!gesture.End(100, 100), "returning a dragged pointer does not trigger a click");
            gesture.Begin(100, 100, 4, 4);
            Check(!gesture.End(110, 100), "movement detected only on mouse-up still suppresses the click");
            gesture.Begin(100, 100, 4, 4); gesture.Cancel();
            Check(!gesture.IsPressed && !gesture.End(100, 100), "lost capture cancels a press without opening details");
            gesture.Begin(100, 100, 6, 6); gesture.Move(105, 100);
            Check(gesture.End(105, 100), "scaled drag threshold permits the same physical click jitter");
            gesture.Begin(100, 100, 4, 4); gesture.Cancel(); gesture.Begin(200, 200, 4, 4);
            Check(gesture.End(200, 200), "clicks recover after interrupted mouse capture");
        }
    }
}
