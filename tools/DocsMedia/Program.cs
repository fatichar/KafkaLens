using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Styling;
using DocsMedia;

var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "docs-media-out");
var mode = args.Length > 1 ? args[1] : "screens";
var theme = args.Length > 2 ? args[2] : "Light";
Directory.CreateDirectory(outDir);
DemoApp.Theme = theme;

var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
AppBuilder.Configure<DemoApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithLifetime(lifetime);

// Render popups (menus, flyouts) inside the window so they appear in captured frames.
Application.Current!.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.Primitives.Popup>())
{
    Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.Popup.ShouldUseOverlayLayerProperty, true) }
});

var window = lifetime.MainWindow ?? throw new InvalidOperationException("MainWindow not created");
var studio = new Studio(window, outDir);
window.Width = Studio.Width;
window.Height = Studio.Height;
window.Show();
studio.Pump(300);

if (mode == "video") new VideoScript(studio).Run();
else new ScreenshotScript(studio).Run();

Console.WriteLine($"Done → {outDir}");
