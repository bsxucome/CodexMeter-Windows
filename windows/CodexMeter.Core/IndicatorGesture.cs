using System;

namespace CodexMeter.Core
{
    public sealed class IndicatorGesture
    {
        private double startX, startY, thresholdX, thresholdY;
        public bool IsPressed { get; private set; }
        public bool IsDragging { get; private set; }
        public void Begin(double x, double y, double horizontalThreshold, double verticalThreshold)
        {
            startX = x; startY = y; thresholdX = Math.Max(1, horizontalThreshold); thresholdY = Math.Max(1, verticalThreshold);
            IsPressed = true; IsDragging = false;
        }
        public void Move(double x, double y)
        {
            if (!IsPressed) return;
            if (Math.Abs(x - startX) >= thresholdX || Math.Abs(y - startY) >= thresholdY) IsDragging = true;
        }
        public bool End(double x, double y)
        {
            if (!IsPressed) return false;
            Move(x, y); bool click = !IsDragging; Cancel(); return click;
        }
        public void Cancel() { IsPressed = false; IsDragging = false; }
    }
}
