using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DocsMedia;

/// <summary>Helpers to drive the headless UI: pump the dispatcher, find controls, simulate input, capture frames.</summary>
public sealed class Studio(Window window, string outDir)
{
    public const int Width = 1600;
    public const int Height = 1000;
    public const double Scale = 1.5;

    public Window Window { get; } = window;

    /// <summary>Every window that has been loaded (dialogs included).</summary>
    private static readonly List<Window> Opened = new();
    static Studio() => Control.LoadedEvent.AddClassHandler<Window>((w, _) => Opened.Add(w));

    /// <summary>Moves the pointer to a neutral spot so no hover highlight leaks into captures.</summary>
    public void Park() { Cursor = new Point(Width * 0.6, Height * 0.8); Window.MouseMove(Cursor); Pump(60); }
    public string OutDir { get; } = outDir;
    public MainViewModel Main => ((KafkaLens.UI.App)Application.Current!).Services!.GetRequiredService<MainViewModel>();
    public OpenedClusterViewModel? Current => Main.OpenedClusters.ElementAtOrDefault(Main.SelectedIndex);

    /// <summary>Last known pointer position in window DIPs (used by the video cursor overlay).</summary>
    public Point Cursor { get; private set; } = new(Width / 2.0, Height / 2.0);

    // ── frame recording (video mode) ──────────────────────────────────────
    private int frameNo;
    private StreamWriter? frameLog;
    public string? Caption { get; set; }
    public bool Clicking { get; private set; }
    public const int Fps = 25;

    public void StartRecording(string dir)
    {
        Directory.CreateDirectory(dir);
        frameLog = new StreamWriter(Path.Combine(dir, "frames.tsv"), false, Encoding.UTF8);
        recordDir = dir;
    }

    private string? recordDir;

    public void StopRecording() => frameLog?.Dispose();

    /// <summary>Advances time: pumps the UI and, when recording, emits one frame per 1/Fps second.</summary>
    public void Hold(double seconds)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds * Fps));
        for (var i = 0; i < frames; i++) Frame();
    }

    public void Frame()
    {
        Pump(1000 / Fps / 2);
        if (recordDir == null) { Pump(1000 / Fps / 2); return; }
        var bmp = Window.CaptureRenderedFrame();
        var file = $"f{frameNo:D5}.png";
        bmp!.Save(Path.Combine(recordDir, file));
        frameLog!.WriteLine($"{file}\t{Cursor.X:F1}\t{Cursor.Y:F1}\t{(Clicking ? 1 : 0)}\t{Caption}");
        frameNo++;
    }

    // ── pumping ──────────────────────────────────────────────────────────
    public void Pump(int ms = 60)
    {
        var sw = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(5);
        } while (sw.ElapsedMilliseconds < ms);
        Dispatcher.UIThread.RunJobs();
    }

    public void WaitUntil(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not met");
            Pump(30);
        }
        Pump(150);
    }

    // ── capture ──────────────────────────────────────────────────────────
    public string Capture(string name, TopLevel? top = null)
    {
        top ??= Window;
        ClearMenuHighlight();
        top.SetRenderScaling(Scale);
        Pump(250);
        var path = Path.Combine(OutDir, name + ".png");
        top.CaptureRenderedFrame()!.Save(path);
        top.SetRenderScaling(1);
        Pump(100);
        Console.WriteLine($"  captured {name}");
        return path;
    }

    /// <summary>Waits for a dialog window of type T to open and render.</summary>
    public T Dialog<T>() where T : Window
    {
        WaitUntil(() => Opened.OfType<T>().Any(w => w.IsVisible));
        var w = Opened.OfType<T>().Last(x => x.IsVisible);
        Pump(300);
        return w;
    }

    /// <summary>Captures a dialog and the main window behind it; tools/DocsMedia/postprocess.py composites them.</summary>
    public void CaptureDialog(string name, Window dialog)
    {
        Capture(name + "__bg");
        Capture(name + "__dlg", dialog);
    }

    /// <summary>Headless focus changes can leave a top-level menu item highlighted; clear it.</summary>
    public void ClearMenuHighlight()
    {
        foreach (var mi in All<Menu>().SelectMany(m => m.GetLogicalChildren().OfType<MenuItem>()))
            if (!mi.IsSubMenuOpen) mi.IsSelected = false;
        Pump(30);
    }

    // ── finding controls ─────────────────────────────────────────────────
    public T Find<T>(string? name = null, Visual? root = null, Func<T, bool>? where = null) where T : Control =>
        All<T>(root).FirstOrDefault(c => (name == null || c.Name == name) && (where == null || where(c)) && c.IsEffectivelyVisible)
        ?? throw new InvalidOperationException($"Control {typeof(T).Name} '{name}' not found");

    public IEnumerable<T> All<T>(Visual? root = null) where T : Visual =>
        (root ?? Window).GetVisualDescendants().OfType<T>();

    public Point CenterOf(Visual v)
    {
        var p = v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), Window)
                ?? throw new InvalidOperationException("Cannot translate point");
        return p;
    }

    // ── input ────────────────────────────────────────────────────────────
    /// <summary>Moves the (virtual) pointer smoothly to a point, emitting frames when recording.</summary>
    public void MoveTo(Point target, double seconds = 0.5)
    {
        var start = Cursor;
        var frames = recordDir == null ? 1 : Math.Max(1, (int)(seconds * Fps));
        for (var i = 1; i <= frames; i++)
        {
            var t = (double)i / frames;
            t = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // ease-in-out
            Cursor = new Point(start.X + (target.X - start.X) * t, start.Y + (target.Y - start.Y) * t);
            Window.MouseMove(Cursor);
            if (recordDir != null) Frame();
        }
        Pump(20);
    }

    public void Click(Visual v, double moveSeconds = 0.5, MouseButton button = MouseButton.Left) => Click(CenterOf(v), moveSeconds, button);

    public void Click(Point p, double moveSeconds = 0.5, MouseButton button = MouseButton.Left)
    {
        MoveTo(p, moveSeconds);
        Clicking = true;
        Window.MouseDown(p, button);
        Pump(20);
        Window.MouseUp(p, button);
        if (recordDir != null) { Frame(); Frame(); Frame(); }
        Clicking = false;
        Pump(80);
    }

    public void DoubleClick(Visual v)
    {
        var p = CenterOf(v);
        MoveTo(p);
        Window.MouseDown(p, MouseButton.Left); Window.MouseUp(p, MouseButton.Left);
        Window.MouseDown(p, MouseButton.Left); Window.MouseUp(p, MouseButton.Left);
        Pump(80);
    }

    /// <summary>Types text into the focused control, one character per frame-ish when recording.</summary>
    public void Type(string text, double charSeconds = 0.06)
    {
        foreach (var ch in text)
        {
            Window.KeyTextInput(ch.ToString());
            if (recordDir != null) Hold(charSeconds); else Pump(5);
        }
        Pump(100);
    }

    public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        Window.KeyPress(key, mods, PhysicalKey.None, null);
        Window.KeyRelease(key, mods, PhysicalKey.None, null);
        Pump(60);
    }

    public void SelectAllAndType(TextBox box, string text, double charSeconds = 0.06)
    {
        box.Focus();
        box.SelectAll();
        Pump(30);
        if (box.SelectedText.Length > 0) Key(Avalonia.Input.Key.Back);
        Type(text, charSeconds);
    }
}
