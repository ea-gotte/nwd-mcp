#nullable disable
using System;

namespace Bimwright.Nwd.Shared.Config
{
    /// <summary>Immutable, host-relative DIP preferences shared across Navisworks years.</summary>
    public sealed class ToastPositionOptions
    {
        public ToastPositionOptions(bool right = false, bool bottom = false, bool dragEnabled = false,
            double? offsetX = null, double? offsetY = null)
        {
            Right = right;
            Bottom = bottom;
            DragEnabled = dragEnabled;
            if (offsetX.HasValue && offsetY.HasValue && Finite(offsetX.Value) && Finite(offsetY.Value))
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
            }
        }
        public bool Right { get; }
        public bool Bottom { get; }
        public bool DragEnabled { get; }
        public double? OffsetX { get; }
        public double? OffsetY { get; }
        public bool HasOffset => OffsetX.HasValue;
        public ToastPositionOptions WithCorner(bool right, bool bottom) => new ToastPositionOptions(right, bottom, DragEnabled,
            right == Right && bottom == Bottom ? OffsetX : null,
            right == Right && bottom == Bottom ? OffsetY : null);
        public ToastPositionOptions WithDrag(bool enabled) => new ToastPositionOptions(Right, Bottom, enabled, OffsetX, OffsetY);
        public ToastPositionOptions WithOffset(double? x, double? y) => new ToastPositionOptions(Right, Bottom, DragEnabled, x, y);
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
