using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bimwright.Nwd.Shared.Views.Toast;
using Bimwright.Nwd.Shared.Config;

internal static class ToastPreview
{
    // Sample data only: neither this preview nor its captures connect to Autodesk or write a call log.
    private static void Seed(ActivityAggregator activity)
    {
        activity.RecordResult("Get Document Info", "2 loaded models", true, false, true, durationMs: 90);
        activity.RecordResult("Find Items", "24 matching items", true, false, true, durationMs: 830);
        activity.RecordResult("Select Items By Search", "Selected 24 items", true, false, true, durationMs: 250);
        activity.RecordResult("Send Code", "Compile error: missing return value", false, false, true, durationMs: 123);
        activity.RecordResult("Hide Items", "Hidden 24 items", true, false, true, durationMs: 325);
    }

    internal static int Run()
    {
        var app = new Application();
        var activity = new ActivityAggregator(() => 60);
        var branding = false;
        var animations = true; // preview only: exercise motion even when RDP disables Windows animations
        var position = new ToastPositionOptions();
        var positionReadout = new TextBlock { Text = "Position: default corner (sample RAM only)", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        var manager = new McpToastManager(Dispatcher.CurrentDispatcher, activity,
            showBranding: () => branding, instanceIdentity: () => "nwd-mcp · sample preview",
            motionEnabled: () => animations, positionOptions: () => position,
            positionChanged: options =>
            {
                position = options;
                positionReadout.Text = "Saved sample offset: " + options.OffsetX + ", " + options.OffsetY + " DIP (RAM only)";
            });
        var controls = new StackPanel { Margin = new Thickness(18) };
        controls.Children.Add(new TextBlock
        {
            Text = "NWD hover preview", FontSize = 18, FontWeight = FontWeights.SemiBold
        });
        controls.Children.Add(new TextBlock
        {
            Text = "Sample data only. No Navisworks connection or call logs.\nHover the top-left card: latest 3 calls, local HH:mm:ss. New calls follow at the bottom; scrolling up keeps your reading position.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12)
        });
        var count = 0;
        AddButton(controls, "Show five sample calls", () =>
        {
            manager.DismissAllImmediate(); Seed(activity); manager.Render();
        });
        AddButton(controls, "Add a new result", () =>
        {
            activity.RecordResult("Find Items", "New result " + ++count + ": 12 matching items", true, false, true, durationMs: 180);
            manager.Render();
        });
        AddButton(controls, "Burst 20 calls", () =>
        {
            for (var i = 0; i < 20; i++)
                activity.RecordResult("Find Items", "Burst result " + ++count, true, false, true, durationMs: 75);
            manager.Render();
        });
        AddButton(controls, "Burst 1,000 calls (test scrolling/virtualization)", () =>
        {
            for (var i = 0; i < 1000; i++)
                activity.RecordResult("Find Items", "Stress result " + ++count, true, false, true, durationMs: 75);
            manager.Render();
        });
        var motion = new CheckBox
        {
            Content = "Enable animations (preview only, overrides RDP preference)",
            IsChecked = true, Margin = new Thickness(0, 10, 0, 0)
        };
        motion.Checked += (_, __) => animations = true;
        motion.Unchecked += (_, __) => animations = false;
        controls.Children.Add(motion);
        controls.Children.Add(new TextBlock
        {
            Text = "Windows animations: " + (SystemParameters.ClientAreaAnimation ? "on" : "off")
                + " · Remote session: " + (SystemParameters.IsRemoteSession ? "yes" : "no"),
            FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
        });
        var incoming = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        incoming.Tick += (_, __) =>
        {
            activity.RecordResult("Find Items", "Live sample " + ++count + ": 12 matching items", true, false, true, durationMs: 180);
            manager.Render();
        };
        var simulate = new CheckBox { Content = "New sample call every 2 seconds (test live-tail / older scroll)", Margin = new Thickness(0, 10, 0, 0) };
        simulate.Checked += (_, __) => incoming.Start();
        simulate.Unchecked += (_, __) => incoming.Stop();
        controls.Children.Add(simulate);
        var brand = new CheckBox { Content = "Show branding", Margin = new Thickness(0, 10, 0, 10) };
        brand.Checked += (_, __) => { branding = true; manager.ApplyShowBranding(); };
        brand.Unchecked += (_, __) => { branding = false; manager.ApplyShowBranding(); };
        controls.Children.Add(brand);
        var horizontal = new ComboBox { ItemsSource = new[] { "Left", "Right" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 4) };
        var vertical = new ComboBox { ItemsSource = new[] { "Top", "Bottom" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 4) };
        horizontal.SelectionChanged += (_, __) => { position = position.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1); manager.ApplyPosition(); positionReadout.Text = "Corner changed; saved drag offset cleared."; };
        vertical.SelectionChanged += (_, __) => { position = position.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1); manager.ApplyPosition(); positionReadout.Text = "Corner changed; saved drag offset cleared."; };
        controls.Children.Add(horizontal);
        controls.Children.Add(vertical);
        var drag = new CheckBox { Content = "Allow dragging from title row", Margin = new Thickness(0, 4, 0, 4) };
        drag.Checked += (_, __) => { position = position.WithDrag(true); manager.ApplyPosition(); };
        drag.Unchecked += (_, __) => { position = position.WithDrag(false); manager.ApplyPosition(); };
        controls.Children.Add(drag);
        AddButton(controls, "Reset position (sample RAM only)", () => { position = position.WithOffset(null, null); manager.ApplyPosition(); positionReadout.Text = "Position reset."; });
        controls.Children.Add(positionReadout);
        AddButton(controls, "Clear toast", manager.DismissAllImmediate);
        var controller = new Window
        {
            Title = "NWD toast preview — sample data", Width = 390, SizeToContent = SizeToContent.Height,
            Left = 430, Top = 100, Content = controls, FontFamily = McpToastTheme.UiFont
        };
        controller.Loaded += (_, __) => { Seed(activity); manager.Render(); };
        controller.Closed += (_, __) => { incoming.Stop(); manager.DismissAllImmediate(); manager.Dispose(); };
        return app.Run(controller);
    }

    internal static int Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        var activity = new ActivityAggregator();
        Seed(activity);
        var cursor = new Point(10, 10);
        var window = new McpToastWindow(activity.TakeRender().Card, null, null, null, null, null,
            () => cursor, () => false, "nwd-mcp 2025 · sample");
        try
        {
            window.SetShowBranding(false); window.CapturePointerBaseline();
            window.SetPosition(50, 50); window.Show(); window.PlayEnterAnimation();
            Save(window, Path.Combine(directory, "compact.png"));
            cursor = new Point(20, 10);
            window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                { RoutedEvent = Mouse.MouseEnterEvent });
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            Save(window, Path.Combine(directory, "hover-expanded.png"));
            Find<ScrollViewer>(Find<ToastActivityPanel>(window)).ScrollToTop();
            frame = new DispatcherFrame();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            Save(window, Path.Combine(directory, "hover-older.png"));
            Console.WriteLine("Captured sample compact/expanded cards in " + Path.GetFullPath(directory));
            return 0;
        }
        finally { window.CloseImmediate(); }
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

    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6) };
        button.Click += (_, __) => action(); panel.Children.Add(button);
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
    }
}
