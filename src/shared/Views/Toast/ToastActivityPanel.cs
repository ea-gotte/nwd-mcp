#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Bimwright.Nwd.Shared.Localization;

namespace Bimwright.Nwd.Shared.Views.Toast
{
    /// <summary>Three-row live view: follow the tail, or preserve the reader's older-call scroll position.</summary>
    internal sealed class ToastActivityPanel : Border
    {
        internal const int VisibleEntryCount = 3;
        internal const double EntryHeight = 42;
        private readonly ListBox _list;
        private readonly TextBlock _heading;
        private readonly Func<bool> _motionEnabled;
        private ScrollViewer _scrollViewer;
        private ScrollBar _verticalBar;
        private DispatcherTimer _scrollFadeTimer;
        private bool _scrollExpanded;
        private int _scrollFadeGeneration;

        public ToastActivityPanel(Func<bool> motionEnabled = null)
        {
            _motionEnabled = motionEnabled ?? (() => SystemParameters.ClientAreaAnimation);
            Margin = new Thickness(24, 6, 0, 0);
            Padding = new Thickness(0, 6, 0, 0);
            BorderBrush = McpToastTheme.ThumbnailBackground;
            BorderThickness = new Thickness(0, 1, 0, 0);
            Cursor = Cursors.Arrow;
            Focusable = false;
            var content = new StackPanel();
            _heading = new TextBlock
            {
                FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = McpToastTheme.TextSecondary, Margin = new Thickness(0, 0, 0, 4)
            };
            content.Children.Add(_heading);
            var styles = ToastScrollBarStyles.Create();
            _list = new ListBox
            {
                Style = (Style)styles["ToastActivityList"], Height = 0,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemContainerStyle = ContainerStyle(), ItemTemplate = EntryTemplate()
            };
            _list.Resources.MergedDictionaries.Add(styles);
            content.Children.Add(_list);
            Child = content;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewMouseMove += OnPointerMove;
            PreviewMouseWheel += OnMouseWheel;
        }

        internal IReadOnlyList<ToastActivityEntry> Entries { get; private set; } = ToastActivityLog.Freeze(null);

        internal void SetEntries(IReadOnlyList<ToastActivityEntry> entries)
        {
            Entries = entries;
            _heading.Text = L.T("toast.activity.recent", ("count", entries.Count));
            _list.ItemsSource = entries;
            _list.Height = Math.Min(VisibleEntryCount, entries.Count) * EntryHeight;
            if (entries.Count == 0) StopScrollCue();
            InvalidateMeasure();
        }

        internal void UpdateEntries(IReadOnlyList<ToastActivityEntry> entries)
        {
            if (Entries.Count == entries.Count && (entries.Count == 0
                || ReferenceEquals(Entries[Entries.Count - 1], entries[entries.Count - 1])))
                return;
            EnsureScrollViewer();
            _list.UpdateLayout(); // commit a user's pending wheel/thumb command before deciding to follow
            var offset = _scrollViewer?.VerticalOffset ?? 0;
            var followTail = _scrollViewer == null || _scrollViewer.ScrollableHeight - offset <= 0.5;
            SetEntries(entries);
            _list.UpdateLayout();
            if (followTail)
                ScrollToLatest();
            else
            {
                _scrollViewer.ScrollToVerticalOffset(offset);
                _list.UpdateLayout();
            }
        }

        /// <summary>Start a new hover at the tail, or continue following after new results.</summary>
        internal void ScrollToLatest()
        {
            if (Entries.Count == 0) return;
            _list.ApplyTemplate();
            _list.UpdateLayout();
            EnsureScrollViewer();
            _scrollViewer?.ScrollToEnd();
            _list.UpdateLayout();
        }

        internal void StopScrollCue()
        {
            _scrollFadeTimer?.Stop();
            _scrollFadeTimer = null;
            _scrollFadeGeneration++;
            _scrollExpanded = false;
            if (_verticalBar != null)
            {
                _verticalBar.BeginAnimation(OpacityProperty, null);
                _verticalBar.Opacity = 0.22;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => EnsureScrollViewer();
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopScrollCue();
            if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
            _verticalBar = null;
        }

        private void EnsureScrollViewer()
        {
            if (_scrollViewer != null) return;
            _list.ApplyTemplate();
            _scrollViewer = Find<ScrollViewer>(_list);
            if (_scrollViewer == null) return;
            _scrollViewer.ApplyTemplate();
            _verticalBar = Find<ScrollBar>(_scrollViewer);
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 && Entries.Count > VisibleEntryCount) RevealScrollCue();
        }
        private void OnMouseWheel(object sender, MouseWheelEventArgs e) => RevealScrollCue();
        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            // Same 28 DIP proximity zone as RVT Settings/History, not generic card hover.
            if (_list.IsMouseOver && e.GetPosition(_list).X >= _list.ActualWidth - 28)
                RevealScrollCue();
        }

        private void RevealScrollCue()
        {
            if (Entries.Count <= VisibleEntryCount || _verticalBar == null) return;
            FadeScrollBar(true);
            if (_scrollFadeTimer == null)
            {
                _scrollFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                _scrollFadeTimer.Tick += (_, __) =>
                {
                    if (_verticalBar != null && (_verticalBar.IsMouseCaptureWithin
                        || (_list.IsMouseOver && Mouse.GetPosition(_list).X >= _list.ActualWidth - 28)))
                        return;
                    _scrollFadeTimer.Stop();
                    FadeScrollBar(false);
                };
            }
            _scrollFadeTimer.Stop();
            _scrollFadeTimer.Start();
        }

        private void FadeScrollBar(bool expanded)
        {
            if (_verticalBar == null || _scrollExpanded == expanded) return;
            _scrollExpanded = expanded;
            var bar = _verticalBar;
            var target = expanded ? 1.0 : 0.22;
            var generation = ++_scrollFadeGeneration;
            if (!_motionEnabled())
            {
                bar.BeginAnimation(OpacityProperty, null);
                bar.Opacity = target;
                return;
            }
            var fade = new DoubleAnimation(bar.Opacity, target, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, __) =>
            {
                if (generation != _scrollFadeGeneration) return;
                bar.BeginAnimation(OpacityProperty, null);
                bar.Opacity = target;
            };
            bar.BeginAnimation(OpacityProperty, fade);
        }

        private static Style ContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(FocusableProperty, false));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding());
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty,
                new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            style.Setters.Add(new Setter(Control.TemplateProperty,
                new ControlTemplate(typeof(ListBoxItem)) { VisualTree = presenter }));
            return style;
        }

        private static DataTemplate EntryTemplate()
        {
            var row = new FrameworkElementFactory(typeof(Border));
            // 38 DIP card + 4 DIP gap: title/time and one result line, no empty tall rows.
            row.SetValue(HeightProperty, EntryHeight - 4);
            row.SetValue(MarginProperty, new Thickness(0, 0, 0, 4));
            row.SetValue(PaddingProperty, new Thickness(6, 2, 6, 2));
            row.SetValue(BorderThicknessProperty, new Thickness(1));
            row.SetValue(BorderBrushProperty, McpToastTheme.ActivityBorder);
            row.SetValue(CornerRadiusProperty, new CornerRadius(4));
            row.SetValue(BackgroundProperty, Brushes.White);
            row.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Title"));
            row.SetBinding(System.Windows.Automation.AutomationProperties.HelpTextProperty, new Binding("Body"));
            var content = new FrameworkElementFactory(typeof(StackPanel));
            row.AppendChild(content);
            var line = new FrameworkElementFactory(typeof(DockPanel));
            content.AppendChild(line);
            var glyph = Text(11, McpToastTheme.Primary);
            glyph.SetValue(TextBlock.FontFamilyProperty, McpToastTheme.IconFont);
            glyph.SetValue(MarginProperty, new Thickness(0, 1, 6, 0));
            glyph.SetValue(DockPanel.DockProperty, Dock.Left);
            glyph.SetBinding(TextBlock.TextProperty, new Binding("Success") { Converter = new OutcomeConverter(false) });
            glyph.SetBinding(TextBlock.ForegroundProperty, new Binding("Success") { Converter = new OutcomeConverter(true) });
            glyph.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
                new Binding("Success") { Converter = new OutcomeConverter(false), ConverterParameter = "status" });
            line.AppendChild(glyph);
            var time = Text(10, McpToastTheme.TextSecondary);
            time.SetValue(DockPanel.DockProperty, Dock.Right);
            time.SetValue(MarginProperty, new Thickness(6, 1, 0, 0));
            time.SetBinding(TextBlock.TextProperty, new Binding("LocalTimeText"));
            line.AppendChild(time);
            var title = Text(12, McpToastTheme.Text);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetBinding(ToolTipProperty, new Binding("Title"));
            line.AppendChild(title);
            var body = Text(11, McpToastTheme.TextSecondary);
            body.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            body.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            body.SetValue(MaxHeightProperty, 16.0);
            body.SetValue(MarginProperty, new Thickness(17, 1, 0, 0));
            body.SetBinding(TextBlock.TextProperty, new Binding("Body"));
            body.SetBinding(ToolTipProperty, new Binding("Body"));
            content.AppendChild(body);
            return new DataTemplate(typeof(ToastActivityEntry)) { VisualTree = row };
        }

        private static FrameworkElementFactory Text(double size, Brush color)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.FontSizeProperty, size);
            text.SetValue(TextBlock.ForegroundProperty, color);
            return text;
        }

        private sealed class OutcomeConverter : IValueConverter
        {
            private readonly bool _brush;
            internal OutcomeConverter(bool brush) { _brush = brush; }
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var success = value is bool ok && ok;
                if (Equals(parameter, "status")) return L.T(success ? "toast.activity.success" : "toast.activity.failed");
                return _brush ? (object)(success ? McpToastTheme.Primary : McpToastTheme.Error)
                    : success ? "\uE73E" : "\uE783";
            }
            public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => Binding.DoNothing;
        }

        private static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var found = Find<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
