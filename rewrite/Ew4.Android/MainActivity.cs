using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Webkit;
using Android.Views;
using Android.Widget;
using Ew4.Mods;
using System.Text;

namespace Ew4.Android;

[Activity(Label = "欧陆战争4 重构", MainLauncher = true, Exported = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : Activity
{
    private WebView? web;
    private Lazy<ModStore>? store;
    private ImportBridge? bridge;
    private bool picking;
    private FrameLayout? surface;
    private SafeInsets? safeInsets;
    private SystemBack? systemBack;
    private AlertDialog? exitDialog;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
#if EW4_DIAGNOSTICS
        WebView.SetWebContentsDebuggingEnabled(true);
#endif
        var assets = Assets!;
        byte[] Read(string path)
        {
            try { using var stream = assets.Open(path); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
            catch (Java.IO.FileNotFoundException error) { throw new FileNotFoundException(path, error); }
            catch (Java.IO.IOException error) { throw new IOException(path, error); }
        }
        IEnumerable<string> Walk(string path)
        {
            foreach (string name in assets.List(path) ?? [])
            {
                string full = path + "/" + name;
                if (assets.List(full)?.Length > 0) { foreach (string child in Walk(full)) yield return child; }
                else yield return full;
            }
        }
        byte[] ContentRead(string path) => Read(path.StartsWith("client/", StringComparison.Ordinal) ? "www/content/" + path[7..]
            : path.StartsWith("legacy/", StringComparison.Ordinal) ? "data/" + path[7..]
            : path.StartsWith("frontend/", StringComparison.Ordinal) ? "www/" + path[9..] : path);
        store = new Lazy<ModStore>(() => new ModStore(new ModBase(ContentRead, Walk("original").Concat(Walk("www/content").Select(p => "client/" + p[12..]))
            .Concat(["legacy/catalog.json", "legacy/runtime_rule_tables.json", "legacy/metagame_rule_tables.json", "frontend/conquest-pages.json", "frontend/city-names.json"])),
            Path.Combine(FilesDir!.AbsolutePath, "content-mods"), Path.Combine(FilesDir.AbsolutePath, "saves")));
        web = new WebView(this);
        web.Settings.JavaScriptEnabled = true;
        web.Settings.DomStorageEnabled = true;
        // The shared music player gates initial playback on interaction and pauses
        // in the background. WebView's additional gesture gate loses asynchronous
        // scene/resource transitions on some Android versions.
        web.Settings.MediaPlaybackRequiresUserGesture = false;
        web.Settings.AllowFileAccess = false;
        web.Settings.AllowContentAccess = false;
        web.Settings.MixedContentMode = MixedContentHandling.NeverAllow;
        web.Settings.SetSupportZoom(false);
        bridge = new ImportBridge(this); web.AddJavascriptInterface(bridge, "Ew4ModImport");
        web.SetWebViewClient(new LocalClient(Read, store));
        web.SetWebChromeClient(new LocalChrome());
        surface = new FrameLayout(this);
        surface.SetBackgroundColor(global::Android.Graphics.Color.Black);
        surface.AddView(web, new FrameLayout.LayoutParams(-1, -1));
        safeInsets = new SafeInsets(this);
        surface.SetOnApplyWindowInsetsListener(safeInsets);
        SetContentView(surface);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            systemBack = new SystemBack(this);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, systemBack);
        }
        EnterFullscreen();
        web.LoadUrl("https://ew4.local/index.html");
    }
    private void EnterFullscreen()
    {
        if (Window == null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            var attributes = Window.Attributes!;
            attributes.LayoutInDisplayCutoutMode = OperatingSystem.IsAndroidVersionAtLeast(30)
                ? LayoutInDisplayCutoutMode.Always : LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attributes;
        }
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(35)) Window.SetDecorFitsSystemWindows(false);
            var controller = Window.InsetsController;
            if (controller != null)
            {
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                controller.Hide(WindowInsets.Type.SystemBars());
            }
        }
        else
        {
#pragma warning disable CS0618
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation
                | SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CS0618
        }
        surface?.RequestApplyInsets();
    }
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) EnterFullscreen();
    }
    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        EnterFullscreen();
    }
    protected override void OnPause()
    {
        web?.EvaluateJavascript("window.ew4SetAppVisible?.(false)", null);
        web?.OnPause();
        web?.PauseTimers();
        base.OnPause();
    }
    protected override void OnResume()
    {
        base.OnResume();
        web?.ResumeTimers();
        web?.OnResume();
        web?.EvaluateJavascript("window.ew4SetAppVisible?.(true)", null);
    }
#pragma warning disable CS0672
    public override void OnBackPressed() => NavigateBack();
#pragma warning restore CS0672
    private void NavigateBack() => web?.EvaluateJavascript("window.ew4Back?.()", new BackResult(this));
    private void ConfirmExit()
    {
        if (IsFinishing || exitDialog?.IsShowing == true) return;
        exitDialog?.Dispose();
        exitDialog = new AlertDialog.Builder(this)!.SetMessage("退出游戏？")!
            .SetNegativeButton("取消", (_, _) => EnterFullscreen())!
            .SetPositiveButton("退出", (_, _) => Finish())!.Create();
        exitDialog!.Show();
    }
    protected override void OnDestroy()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && systemBack != null)
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(systemBack);
        systemBack?.Dispose(); systemBack = null;
        exitDialog?.Dismiss(); exitDialog?.Dispose(); exitDialog = null;
        surface?.SetOnApplyWindowInsetsListener(null); safeInsets?.Dispose(); safeInsets = null;
        web?.RemoveJavascriptInterface("Ew4ModImport"); bridge?.Dispose(); bridge = null;
        web?.Destroy(); web?.Dispose(); web = null;
        surface?.Dispose(); surface = null;
        base.OnDestroy();
    }
    private sealed class SystemBack(MainActivity activity) : Java.Lang.Object, global::Android.Window.IOnBackInvokedCallback
    {
        public void OnBackInvoked() => activity.NavigateBack();
    }
    private sealed class BackResult(MainActivity activity) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            if (value?.ToString() == "\"exit\"") activity.ConfirmExit();
        }
    }
    private sealed class SafeInsets(MainActivity activity) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            // Only visible system bars reduce the viewport; a cutout is not a full-height gutter.
            if (view != null && insets != null)
            {
                int left = 0, top = 0, right = 0, bottom = 0;
                void Include(int l, int t, int r, int b)
                {
                    left = Math.Max(left, l); top = Math.Max(top, t);
                    right = Math.Max(right, r); bottom = Math.Max(bottom, b);
                }
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    // Hidden bars and gesture hit regions do not obscure the fullscreen surface.
                    var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                    Include(bars.Left, bars.Top, bars.Right, bars.Bottom);
                }
                else
                {
#pragma warning disable CS0618
                    var flags = (SystemUiFlags)(int)(activity.Window?.DecorView.SystemUiVisibility ?? StatusBarVisibility.Visible);
                    bool navigationHidden = (flags & SystemUiFlags.HideNavigation) != 0;
                    bool statusHidden = (flags & SystemUiFlags.Fullscreen) != 0;
                    Include(navigationHidden ? 0 : insets.SystemWindowInsetLeft, statusHidden ? 0 : insets.SystemWindowInsetTop,
                        navigationHidden ? 0 : insets.SystemWindowInsetRight, navigationHidden ? 0 : insets.SystemWindowInsetBottom);
#pragma warning restore CS0618
                }
                if (view.PaddingLeft != left || view.PaddingTop != top || view.PaddingRight != right || view.PaddingBottom != bottom)
                    view.SetPadding(left, top, right, bottom);
                // Preserve cutout information for floating content, consuming only handled system bars.
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    using var remaining = new WindowInsets.Builder(insets);
                    var zero = global::Android.Graphics.Insets.Of(0, 0, 0, 0);
                    remaining.SetInsets(WindowInsets.Type.SystemBars(), zero);
                    remaining.SetInsetsIgnoringVisibility(WindowInsets.Type.SystemBars(), zero);
                    return remaining.Build()!;
                }
#pragma warning disable CS0618
                return insets.ConsumeSystemWindowInsets()!;
#pragma warning restore CS0618
            }
            return insets!;
        }
    }
    private void Pick(string id, string name) => RunOnUiThread(() =>
    {
        if (picking || IsFinishing) return;
        picking = true;
        var intent = new Intent(Intent.ActionOpenDocument); intent.AddCategory(Intent.CategoryOpenable); intent.SetType("*/*");
        StartActivityForResult(intent, 441);
    });
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != 441) return; picking = false;
        if (resultCode != Result.Ok || data?.Data is not { } uri || store == null) return;
        web?.EvaluateJavascript("window.ew4ModFileSelected?.()", null);
        _ = Task.Run(() =>
        {
            string? temp = Path.Combine(CacheDir!.AbsolutePath, "mod-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("无法读取文件"))
                using (var output = File.Create(temp))
                {
                    byte[] buffer = new byte[65536]; long total = 0; int count;
                    while ((count = input.Read(buffer)) > 0) { total += count; if (total > 1024L * 1024 * 1024) throw new IOException("文件超过1GiB"); output.Write(buffer, 0, count); }
                }
                string fileName = uri.LastPathSegment ?? "Mod.apk";
                using (var cursor = ContentResolver!.Query(uri, [global::Android.Provider.IOpenableColumns.DisplayName!], null, null, null))
                    if (cursor?.MoveToFirst() == true) fileName = cursor.GetString(0) ?? fileName;
                var prepared = store.Value.PrepareImport(temp, fileName); temp = null;
                string preparedJson = System.Text.Json.JsonSerializer.Serialize(prepared, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                RunOnUiThread(() => web?.EvaluateJavascript("window.ew4ModFilePrepared?.(" + preparedJson + ")", null));
            }
            catch (Exception e) { string message = System.Text.Json.JsonSerializer.Serialize(e.Message); RunOnUiThread(() => web?.EvaluateJavascript("window.ew4ModImportError?.(" + message + ")", null)); }
            finally { if (temp != null) File.Delete(temp); }
        });
    }
    private sealed class ImportBridge(MainActivity activity) : Java.Lang.Object
    {
        [JavascriptInterface, Java.Interop.Export("choose")]
        public void Choose(string id, string name) => activity.Pick(id, name);
    }
    private sealed class LocalClient(Func<string, byte[]> read, Lazy<ModStore> store) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request) => request?.Url?.Host != "ew4.local";
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
        {
            var uri = request?.Url;
            if (uri?.Host != "ew4.local") return Response("text/plain", "External request blocked", 403);
            string path = Uri.UnescapeDataString(uri.EncodedPath ?? "/");
            if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')) return Response("text/plain", "Invalid path", 400);
            try
            {
                if (path.StartsWith("/api/", StringComparison.Ordinal))
                    return Response("application/json", store.Value.Dispatch(path, uri.GetQueryParameter("q") ?? "{}"));
                byte[] bytes = path.StartsWith("/content/", StringComparison.Ordinal) ? store.Value.ReadContent(path[9..])
                    : path is "/conquest-pages.json" or "/city-names.json" ? store.Value.Read("frontend" + path) : read("www/" + path.TrimStart('/'));
                if (!path.StartsWith("/content/", StringComparison.Ordinal)) bytes = store.Value.Frontend(bytes, Path.GetExtension(path));
                string mime = Path.GetExtension(path) switch { ".html" => "text/html", ".js" => "text/javascript", ".css" => "text/css", ".json" => "application/json", ".png" => "image/png", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf", _ => "application/octet-stream" };
                return new WebResourceResponse(mime, "UTF-8", 200, "OK", new Dictionary<string, string> { ["Cache-Control"] = "no-store" }, new MemoryStream(bytes));
            }
            catch (Exception error)
            {
                global::Android.Util.Log.Error("EW4", "Local request failed " + path + ": " + error);
                return Response("text/plain", "Local resource unavailable", error is IOException or Java.IO.IOException ? 404 : 500);
            }
        }
        private static WebResourceResponse Response(string mime, string text, int status = 200) => new(mime, "UTF-8", status,
            status == 200 ? "OK" : "Error", new Dictionary<string, string> { ["Cache-Control"] = "no-store" }, new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }
    private sealed class LocalChrome : WebChromeClient
    {
        public override bool OnConsoleMessage(ConsoleMessage? message)
        {
            if (message != null && message.InvokeMessageLevel() == ConsoleMessage.MessageLevel.Error)
                global::Android.Util.Log.Error("EW4", message.Message() + " at " + message.SourceId() + ":" + message.LineNumber());
            return true;
        }
    }
}
