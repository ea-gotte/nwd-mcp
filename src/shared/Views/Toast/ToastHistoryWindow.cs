#nullable disable
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Bimwright.Nwd.Shared.Localization;

namespace Bimwright.Nwd.Shared.Views.Toast
{
    internal sealed class ToastHistoryWindow : Window
    {
        private readonly ActivityHistory _history;
        private readonly TextBox _search = new TextBox { MinWidth = 220, Margin = new Thickness(4) };
        private readonly ComboBox _status = new ComboBox { Width = 100, Margin = new Thickness(4) };
        private readonly DataGrid _table = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true };
        private readonly DispatcherTimer _timer;
        private long _version = -1;

        internal ToastHistoryWindow(ActivityHistory history, string identity)
        {
            _history = history;
            Title = L.T("history.title", ("identity", identity ?? "nwd-mcp"));
            Width = 800; Height = 460;
            var root = new DockPanel { Margin = new Thickness(12) };
            var filters = new StackPanel { Orientation = Orientation.Horizontal };
            filters.Children.Add(new TextBlock { Text = L.T("history.search"), VerticalAlignment = VerticalAlignment.Center });
            filters.Children.Add(_search);
            _status.Items.Add(L.T("history.all"));
            _status.Items.Add(L.T("toast.activity.success"));
            _status.Items.Add(L.T("toast.activity.failed"));
            _status.SelectedIndex = 0;
            filters.Children.Add(_status);
            DockPanel.SetDock(filters, Dock.Top);
            root.Children.Add(filters);
            AddColumn("history.time", "LocalTimeText", 75);
            AddColumn("history.tool", "Title", 170);
            AddColumn("history.summary", "Body", 350);
            AddColumn("history.duration", "DurationMs", 80);
            var row = new Style(typeof(DataGridRow));
            row.Setters.Add(new Setter(ToolTipProperty, new Binding("TooltipText")));
            _table.RowStyle = row;
            root.Children.Add(_table);
            Content = root;
            _search.TextChanged += (_, __) => Refresh();
            _status.SelectionChanged += (_, __) => Refresh();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, __) => { if (_history.Version != _version) Refresh(); };
            Closed += (_, __) => { _timer.Stop(); _table.ItemsSource = null; };
            Refresh();
            _timer.Start();
        }
        private void AddColumn(string key, string path, double width) =>
            _table.Columns.Add(new DataGridTextColumn { Header = L.T(key), Binding = new Binding(path), Width = width });
        private void Refresh()
        {
            _version = _history.Version;
            var text = _search.Text ?? string.Empty;
            _table.ItemsSource = _history.Snapshot().Where(entry =>
                (_status.SelectedIndex == 0 || entry.Success == (_status.SelectedIndex == 1))
                && (entry.Title + " " + entry.Body).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        }
    }
}
