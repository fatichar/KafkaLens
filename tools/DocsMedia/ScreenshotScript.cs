using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using KafkaLens.UI.Views;
using KafkaLens.ViewModels;

namespace DocsMedia;

/// <summary>Produces the still screenshots used on the website (one PNG per feature).</summary>
public sealed class ScreenshotScript(Studio s)
{
    private OpenedClusterViewModel C => s.Current!;

    private ITreeNode Topic(string name) => C.Topics.First(t => t.Name == name);

    private void Fetch(ITreeNode node, string position = "End", int count = 100)
    {
        C.SelectedNode = node;
        s.Pump(200);
        C.FetchPosition = position;
        C.FetchCount = count;
        C.CurrentMessages.PositiveFilter = "";
        C.CurrentMessages.LineFilter = "";
        s.Pump(100);
        C.ToggleFetchCommand.Execute(null);
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Messages.Count > 0);
    }

    private void Select(int indexFromEnd)
    {
        var f = C.CurrentMessages.Filtered;
        C.CurrentMessages.CurrentMessage = f[Math.Max(0, f.Count - indexFromEnd)];
        s.Pump(300);
    }

    private TabItem Tab(string header) => s.Find<TabItem>(where: t => (t.Header as string) == header);

    public void Run()
    {
        var mode = DemoApp.Theme;
        s.WaitUntil(() => s.Main.Clusters.Count >= 3);
        s.Main.OpenClusterCommand.Execute("prod-eu");
        s.WaitUntil(() => s.Current is { Topics.Count: > 0 });
        s.Park();

        if (mode == "Dark")
        {
            Fetch(Topic("payments.transactions"));
            Select(6);
            s.Capture("dark-theme");
            return;
        }

        // 1. Main window: topic browsing + pretty JSON
        Fetch(Topic("orders.created"));
        Select(4);
        s.Capture("main");

        // 2. Headers tab
        Tab("Headers").IsSelected = true;
        s.Pump(300);
        s.Capture("message-headers");
        Tab("Body").IsSelected = true;
        s.Pump(200);

        // 3. Message list filter with boolean expression
        Fetch(Topic("orders.status-changed"), count: 500);
        C.CurrentMessages.PositiveFilter = "CANCELLED || PAYMENT_FAILED";
        s.Pump(300);
        Select(2);
        s.Capture("filter-messages");

        // 4. Filter syntax help flyout
        var help = s.Find<Button>(where: b => (b.Content as string) == "?");
        s.Click(help, 0);
        s.Pump(400);
        s.Capture("filter-help");
        help.Flyout!.Hide();
        s.Park();

        // 5. Object filter inside a single message
        Fetch(Topic("payments.transactions"));
        Select(3);
        C.CurrentMessages.LineFilter = "method";
        s.Pump(300);
        s.Capture("object-filter");
        C.CurrentMessages.LineFilter = "";

        // 6. Partition + fetch from timestamp
        var shipments = (ITreeNode)Topic("shipments.tracking");
        shipments.IsExpanded = true;
        s.Pump(200);
        var p2 = shipments.Children.Cast<ITreeNode>().ElementAt(2);
        C.SelectedNode = p2;
        s.Pump(200);
        C.FetchPosition = "Timestamp";
        var from = DemoKafkaClient.Now.LocalDateTime.AddMinutes(-27);
        C.StartDate = from.Date;
        C.StartTimeText = from.ToString("HH:mm:00");
        C.FetchCount = 50;
        s.Pump(200);
        C.ToggleFetchCommand.Execute(null);
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Messages.Count > 0);
        Select(48);
        s.Capture("fetch-timestamp");
        shipments.IsExpanded = false;

        // 7. Binary data with numeric formatter
        ((IMessageSource)Topic("metrics.api-latency-ms")).FormatterName = "Int64";
        Fetch(Topic("metrics.api-latency-ms"), count: 250);
        Select(5);
        s.Capture("numeric-formatter");

        // 8. Topic configuration (per-topic formatters)
        Tab("Configuration").IsSelected = true;
        s.Pump(300);
        s.Capture("topic-config");
        Tab("Messages").IsSelected = true;
        s.Pump(200);

        // 9. Plain-text logs + topic filter
        C.FilterText = "o";
        s.Pump(100);
        C.FilterText = "";
        Fetch(Topic("audit.log"), count: 250);
        C.CurrentMessages.PositiveFilter = "ERROR || WARN";
        s.Pump(300);
        Select(1);
        s.Capture("text-logs");
        C.CurrentMessages.PositiveFilter = "";

        // 10. Multiple clusters in tabs + topic search
        s.Main.OpenClusterCommand.Execute("staging");
        s.WaitUntil(() => s.Main.OpenedClusters.Count == 2 && s.Current is { Topics.Count: > 0 });
        s.Main.OpenClusterCommand.Execute("local");
        s.WaitUntil(() => s.Main.OpenedClusters.Count == 3 && s.Current is { Topics.Count: > 0 });
        s.Main.SelectedIndex = 0;
        s.Pump(300);
        C.FilterText = "order";
        s.Pump(300);
        Fetch(Topic("orders.status-changed"));
        Select(5);
        s.Capture("multi-cluster-tabs");
        C.FilterText = "";

        // 11. Context menu: copy / save
        var grid = s.Find<DataGrid>("MessagesGrid");
        var row = s.All<DataGridRow>(grid).OrderBy(r => r.Bounds.Y).Skip(4).First();
        var pt = row.TranslatePoint(new Point(420, row.Bounds.Height / 2), s.Window)!.Value;
        s.Click(pt, 0);
        s.Click(pt, 0, MouseButton.Right);
        s.Pump(300);
        var copy = s.All<MenuItem>().FirstOrDefault(m => (m.Header as string) == "Save Selected");
        if (copy != null) { s.MoveTo(s.CenterOf(copy), 0); s.Window.MouseMove(s.CenterOf(copy)); s.Pump(600); }
        s.Capture("context-menu");
        grid.ContextMenu?.Close();
        s.Park();

        // 12. Application log panel
        s.Main.IsAppLogPanelVisible = true;
        s.Pump(300);
        s.Capture("app-log");
        s.Main.IsAppLogPanelVisible = false;
        s.Pump(200);

        // 13. Dialogs
        s.Main.EditClustersCommand.Execute(null);
        var edit = s.Dialog<EditClustersDialog>();
        s.CaptureDialog("edit-clusters", edit);
        edit.Close();
        s.Pump(200);

        var add = new AddEditClusterDialog();
        add.ShowDialog(s.Window);
        s.Pump(300);
        add.FindControl<TextBox>("NameBox")!.Text = "Analytics (on-prem)";
        add.FindControl<TextBox>("AddressBox")!.Text = "kafka-01.dc1.example.net:9092,kafka-02.dc1.example.net:9092";
        s.Pump(200);
        s.CaptureDialog("add-cluster", add);
        add.Close();
        s.Pump(200);

        s.Main.ShowPreferencesCommand.Execute(null);
        var prefs = s.Dialog<PreferencesWindow>();
        s.CaptureDialog("preferences", prefs);
        var tabs = s.Find<TabControl>(root: prefs);
        tabs.SelectedIndex = 2;
        s.Pump(300);
        s.CaptureDialog("preferences-browser", prefs);
        tabs.SelectedIndex = 3;
        s.Pump(300);
        s.CaptureDialog("preferences-formatters", prefs);
        prefs.Close();
        s.Pump(200);
    }
}
