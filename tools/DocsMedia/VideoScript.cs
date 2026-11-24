using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;

namespace DocsMedia;

/// <summary>
/// Scripted product walkthrough. Emits numbered frames + frames.tsv (cursor, click, caption);
/// tools/DocsMedia/make_video.py adds the cursor, captions, intro/outro and encodes the MP4.
/// </summary>
public sealed class VideoScript(Studio s)
{
    public const double Scale = 1.2; // 1600x1000 DIPs → 1920x1200 video

    private OpenedClusterViewModel C => s.Current!;

    private void Say(string caption) => s.Caption = caption;

    private MenuItem MenuItem(string header) =>
        s.All<MenuItem>().First(m => m.IsEffectivelyVisible && Clean(m.Header) == header);

    private static string Clean(object? h) => (h?.ToString() ?? "").Replace("_", "");

    private void ClickMenu(params string[] path)
    {
        foreach (var h in path)
        {
            s.Pump(100);
            s.Click(MenuItem(h), 0.45);
            s.Hold(0.25);
        }
    }

    private TreeViewItem TreeItem(string name) =>
        s.All<TreeViewItem>().First(t => t.IsEffectivelyVisible && t.DataContext is ITreeNode n && n.Name == name);

    private void ClickTreeItem(string name)
    {
        var item = TreeItem(name);
        var header = item.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);
        s.Click(header, 0.6);
    }

    private Control MessagesGrid => s.Find<DataGrid>("MessagesGrid");

    private void ClickRow(int index)
    {
        var rows = s.All<DataGridRow>(MessagesGrid).Where(r => r.IsEffectivelyVisible).OrderBy(r => r.TranslatePoint(default, s.Window)!.Value.Y).ToList();
        var row = rows[Math.Min(index, rows.Count - 1)];
        s.Click(row.TranslatePoint(new Point(330, row.Bounds.Height / 2), s.Window)!.Value, 0.6);
    }

    private void ClickTab(string header) =>
        s.Click(s.Find<TabItem>(where: t => (t.Header as string) == header), 0.5);

    private void ClickCombo(ComboBox combo, string item)
    {
        s.Click(combo, 0.5);
        s.Hold(0.35);
        var target = s.All<ComboBoxItem>().First(i => i.IsEffectivelyVisible && (i.Content?.ToString() ?? i.DataContext?.ToString()) == item);
        s.Click(target, 0.4);
        s.Hold(0.2);
    }

    private ComboBox FetchCombo(int index) =>
        s.All<ComboBox>(s.Find<KafkaLens.UI.Views.FetchPanel>()).Where(c => c.IsEffectivelyVisible).ElementAt(index);

    private void TypeInto(TextBox box, string text)
    {
        s.Click(box, 0.6);
        box.Focus();
        s.Hold(0.15);
        s.Type(text, 0.07);
    }

    public void Run()
    {
        s.WaitUntil(() => s.Main.Clusters.Count >= 3);
        s.Window.SetRenderScaling(Scale);
        s.Pump(200);
        s.StartRecording(Path.Combine(s.OutDir, "frames"));

        // ── 1. open a cluster ───────────────────────────────────────────
        Say("Open any configured Kafka cluster from the Cluster menu");
        s.Hold(1.2);
        ClickMenu("Cluster", "Open Cluster", "Production (eu-west-1)");
        s.WaitUntil(() => s.Current is { Topics.Count: > 0 });
        s.Hold(1.0);

        // ── 2. browse a topic ───────────────────────────────────────────
        Say("Pick a topic — the latest messages are fetched instantly");
        ClickTreeItem("orders.created");
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Filtered.Count > 0);
        s.Hold(1.5);

        Say("Select a message to read it as pretty-printed, syntax-highlighted JSON");
        ClickRow(3);
        s.Hold(2.5);
        Say("Headers are one click away");
        ClickTab("Headers");
        s.Hold(1.8);
        ClickTab("Body");
        s.Hold(0.6);

        // ── 3. filter the list ──────────────────────────────────────────
        Say("Filter loaded messages with boolean expressions: &&  ||  !  ( )  \"phrases\"");
        ClickTreeItem("orders.status-changed");
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Filtered.Count > 0);
        ClickCombo(FetchCombo(1), "500");
        s.Click(s.Find<Button>(where: b => b.Command == C.ToggleFetchCommand), 0.5);
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Messages.Count >= 500);
        s.Hold(0.6);
        TypeInto(s.Find<TextBox>("FilterBox", s.Find<KafkaLens.UI.Views.MessagesToolbar>()), "CANCELLED || PAYMENT_FAILED");
        s.Hold(1.2);
        ClickRow(1);
        s.Hold(2.2);

        // ── 4. object filter inside a payload ───────────────────────────
        Say("Zoom into big payloads: the object filter keeps only matching fields");
        var listFilter = s.Find<TextBox>("FilterBox", s.Find<KafkaLens.UI.Views.MessagesToolbar>());
        s.Click(listFilter, 0.5);
        listFilter.Focus();
        listFilter.SelectAll();
        s.Key(Key.Back);
        s.Hold(0.3);
        ClickTreeItem("payments.transactions");
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Filtered.Count > 0);
        s.Hold(0.4);
        ClickRow(2);
        s.Hold(1.2);
        TypeInto(s.Find<TextBox>("FilterBox", s.Find<KafkaLens.UI.Views.MessageDisplayToolbar>()), "method");
        s.Hold(2.4);
        var lineFilter = s.Find<TextBox>("FilterBox", s.Find<KafkaLens.UI.Views.MessageDisplayToolbar>());
        lineFilter.Focus();
        lineFilter.SelectAll();
        s.Key(Key.Back);

        // ── 5. partition + timestamp ────────────────────────────────────
        Say("Drill into a single partition and start from any timestamp or offset");
        var shipments = TreeItem("shipments.tracking");
        var expander = shipments.GetVisualDescendants().OfType<ToggleButton>().First(t => t.IsEffectivelyVisible);
        s.Click(expander, 0.6);
        s.Hold(0.5);
        ClickTreeItem("Partition 2");
        s.WaitUntil(() => !C.IsLoading);
        s.Hold(0.6);
        ClickCombo(FetchCombo(0), "Timestamp");
        var time = s.Find<TextBox>("TimeTextBox");
        s.Click(time, 0.5);
        time.Focus();
        time.SelectAll();
        s.Key(Key.Back);
        s.Type(DemoKafkaClient.Now.LocalDateTime.AddMinutes(-20).ToString("HH:mm:00"), 0.08);
        ClickCombo(FetchCombo(1), "50");
        s.Click(s.Find<Button>(where: b => b.Command == C.ToggleFetchCommand), 0.5);
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Messages.Count > 0);
        ClickRow(0);
        s.Hold(2.2);
        s.Click(expander, 0.5);

        // ── 6. binary data / formatters ─────────────────────────────────
        Say("Binary payload? Pick a formatter per topic: JSON, Text, numeric types, or plugins");
        ClickTreeItem("metrics.api-latency-ms");
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Filtered.Count > 0);
        s.Hold(1.0);
        ClickTab("Configuration");
        s.Hold(0.8);
        var valueCombo = s.All<ComboBox>(s.Find<KafkaLens.UI.Views.TopicConfigView>()).ElementAt(1);
        ClickCombo(valueCombo, "Int64");
        s.Click(s.Find<Button>(where: b => (b.Content as string) == "Save"), 0.5);
        s.Hold(0.8);
        ClickTab("Messages");
        Say("…values are decoded immediately, and the choice is remembered for the topic");
        s.Hold(0.6);
        ClickRow(4);
        s.Hold(2.2);

        // ── 7. several clusters + topic search ──────────────────────────
        Say("Keep several clusters open side by side, and find topics fast");
        ClickMenu("Cluster", "Open Cluster", "Staging");
        s.WaitUntil(() => s.Main.OpenedClusters.Count == 2 && s.Current is { Topics.Count: > 0 });
        s.Hold(0.6);
        TypeInto(s.Find<TextBox>(where: t => t.PlaceholderText == "Filter topics..." && t.IsEffectivelyVisible), "ship");
        s.Hold(0.6);
        ClickTreeItem("shipments.tracking");
        s.WaitUntil(() => !C.IsLoading && C.CurrentMessages.Filtered.Count > 0);
        ClickRow(5);
        s.Hold(1.6);
        s.Click(s.All<TabItem>().First(t => t.DataContext is OpenedClusterViewModel o && o.ClusterId == "prod-eu"), 0.7);
        s.Hold(1.2);

        // ── 8. copy / save ──────────────────────────────────────────────
        Say("Copy keys and values, or save messages to disk and reopen them offline");
        var rows = s.All<DataGridRow>(MessagesGrid).Where(r => r.IsEffectivelyVisible).OrderBy(r => r.TranslatePoint(default, s.Window)!.Value.Y).ToList();
        var pt = rows[2].TranslatePoint(new Point(330, rows[2].Bounds.Height / 2), s.Window)!.Value;
        s.Click(pt, 0.5, MouseButton.Right);
        s.Hold(0.5);
        s.MoveTo(s.CenterOf(MenuItem("Copy")), 0.5);
        s.Hold(0.6);
        s.MoveTo(s.CenterOf(MenuItem("Save Selected")), 0.5);
        s.Hold(1.6);
        (MessagesGrid.ContextMenu)?.Close();
        s.Hold(0.4);

        // ── 9. dark theme ───────────────────────────────────────────────
        Say("Light or dark — your choice");
        ClickMenu("View", "Theme", "Dark");
        s.Hold(2.4);

        s.Caption = null;
        s.Park();
        s.Hold(1.0);
        s.StopRecording();
        s.Window.SetRenderScaling(Studio.Scale);
        s.Capture("video-poster");
    }
}
