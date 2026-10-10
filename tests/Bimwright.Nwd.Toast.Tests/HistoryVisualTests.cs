using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Bimwright.Nwd.Shared.Views.Toast;

internal static class HistoryVisualTests
{
    internal static void Run()
    {
        var host = new McpToastHost();
        host.SetHostDispatcher(Dispatcher.CurrentDispatcher);
        var enabled = false;
        var notifier = new McpToastNotifier(host, () => enabled);
        try
        {
            notifier.OnCompleted("get_document_info", "{\"ok\":true,\"data\":{\"title\":\"fixture.nwd\"},\"meta\":{\"duration_ms\":42}}", true, null, null);
            if (notifier.History.Snapshot().Length != 1 || notifier.History.Snapshot()[0].DurationMs != 42)
                throw new Exception("Real notifier did not retain measured History while toast was off.");
            notifier.ShowHistory();
            Pump(350);
            var window = (ToastHistoryWindow)typeof(McpToastHost).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            if (window == null || !window.IsVisible) throw new Exception("History did not open through the host.");
            var table = (DataGrid)typeof(ToastHistoryWindow).GetField("_table", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            if (table.Items.Count != 1) throw new Exception("History row was missing.");
            notifier.OnCompleted("get_document_info", "{\"ok\":false,\"error\":{\"message\":\"Fixture error\"}}", false, "Fixture error", null);
            Pump(350);
            if (table.Items.Count != 2) throw new Exception("History live refresh did not add failure.");
            var filter = (ComboBox)typeof(ToastHistoryWindow).GetField("_status", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            filter.SelectedIndex = 2;
            if (table.Items.Count != 1) throw new Exception("Failure filter did not apply.");
            window.Close();
            enabled = true;
            notifier.OnCompleted("get_document_info", "{\"ok\":true,\"data\":{\"title\":\"fixture.nwd\"}}", true, null, null);
            Pump(350);
            var manager = (McpToastManager)typeof(McpToastHost).GetField("_manager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            var card = (McpToastWindow)typeof(McpToastManager).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            if (card == null || !card.IsVisible) throw new Exception("Real notifier did not display a card when enabled.");
            var click = (MouseButtonEventHandler)typeof(McpToastWindow).GetField("_mouseUpHandler", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(card);
            click(card, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Pump(550);
            window = (ToastHistoryWindow)typeof(McpToastHost).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            if (window == null || !window.IsVisible || card.IsVisible) throw new Exception("Card click must open History and dismiss the card.");
            table = (DataGrid)typeof(ToastHistoryWindow).GetField("_table", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            if (table.Items.Count != 3) throw new Exception("Card click History lost earlier outcomes.");
            host.Shutdown();
            if (window.IsVisible || table.ItemsSource != null) throw new Exception("History did not release UI state on shutdown.");
        }
        finally { host.Shutdown(); }
        Console.WriteLine("PASS: real notifier retains History while off; measured duration, live refresh, filter, card click and shutdown cleanup");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, __) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
