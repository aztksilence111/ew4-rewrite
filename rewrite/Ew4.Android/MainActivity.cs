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
public sealed partial class MainActivity : Activity
{
    private WebView? web;
    private Lazy<ModStore>? store;
    private ImportBridge? bridge;
    private bool picking;
    private bool exportingSaves;
    private FrameLayout? surface;
    private SafeInsets? safeInsets;
    private SystemBack? systemBack;
    private AlertDialog? exitDialog;
    private bool starting;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        CreateStartupSurface();
        var assets = Assets!;
        Stream Open(string path)
        {
            try { return assets.Open(path); }
            catch (Java.IO.FileNotFoundException error) { throw new FileNotFoundException(path, error); }
            catch (Java.IO.IOException error) { throw new IOException(path, error); }
        }
        byte[] Read(string path)
        {
            using var stream = Open(path); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
        }
        assetRead = Read;
        IEnumerable<string> Walk(string path)
        {
            foreach (string name in assets.List(path) ?? [])
            {
                string full = path + "/" + name;
                if (assets.List(full)?.Length > 0) { foreach (string child in Walk(full)) yield return child; }
                else yield return full;
            }
        }
        string PackageRevision()
        {
            var info = ApplicationInfo!;
            var paths = new[] { info.SourceDir! }.Concat(info.SplitSourceDirs ?? []).Order(StringComparer.Ordinal);
            return System.Text.Json.JsonSerializer.Serialize(new {
                updated = PackageManager!.GetPackageInfo(PackageName!, PackageInfoFlags.MetaData)!.LastUpdateTime,
                files = paths.Select(p => { var file = new FileInfo(p); return new { file.FullName, file.Length, file.LastWriteTimeUtc }; }).ToArray()
            });
        }
        string ContentPath(string path) => path.StartsWith("client/", StringComparison.Ordinal) ? "www/content/" + path[7..]
            : path.StartsWith("legacy/", StringComparison.Ordinal) ? "data/" + path[7..]
            : path.StartsWith("frontend/", StringComparison.Ordinal) ? "www/" + path[9..] : path;
        string filesRoot = FilesDir!.AbsolutePath;
        store = new Lazy<ModStore>(() => new ModStore(StartupAssets.PreparePackage(p => Read(ContentPath(p)), () => Walk("original").Concat(Walk("www/content").Select(p => "client/" + p[12..]))
            .Concat(Walk("data").Select(p => "legacy/" + p[5..]))
            .Concat(Walk("rewrite"))
            .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]), PackageRevision(),
            Path.Combine(filesRoot, "startup-assets"), p => SetStartupStage(p.Message + (p.Total > 0 ? $"（{p.Completed}/{p.Total}）" : "")), p => Open(ContentPath(p))).Content,
            Path.Combine(filesRoot, "content-mods"), Path.Combine(filesRoot, "saves")), LazyThreadSafetyMode.PublicationOnly);
        safeInsets = new SafeInsets(this);
        surface!.SetOnApplyWindowInsetsListener(safeInsets);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            systemBack = new SystemBack(this);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, systemBack);
        }
        EnterFullscreen();
        if (!previousStartupIncomplete) StartGame();
    }
    private void StartGame()
    {
        if (starting || store == null || destroyed) return;
        starting = true;
        startupComplete = false;
        startupHasFailed = false;
        startupPanel!.Visibility = ViewStates.Gone;
        startupSplash!.Visibility = ViewStates.Visible;
        startupContinue!.Visibility = ViewStates.Gone;
        startupSpinner!.Visibility = ViewStates.Visible;
        SetStartupStage("检查游戏资源");
        _ = Task.Run(() =>
        {
            try
            {
                ReadPreviousExit();
                _ = store.Value;
                if (!destroyed) RunOnUiThread(CreateGameWebView);
            }
            catch (Exception error)
            {
                StartupFailed("资源检查失败", error);
            }
        });
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
    private void NavigateBack()
    {
        if (web == null || startupPanel?.Visibility == ViewStates.Visible) ConfirmExit();
        else web.EvaluateJavascript("window.ew4Back?.()", new BackResult(this));
    }
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
        destroyed = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && systemBack != null)
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(systemBack);
        systemBack?.Dispose(); systemBack = null;
        exitDialog?.Dismiss(); exitDialog?.Dispose(); exitDialog = null;
        surface?.SetOnApplyWindowInsetsListener(null); safeInsets?.Dispose(); safeInsets = null;
        ReleaseWebView();
        startupSplash?.SetImageDrawable(null);
        startupArtwork?.Dispose(); startupArtwork = null;
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
    private void PickSaveExport() => RunOnUiThread(() =>
    {
        if (exportingSaves || IsFinishing || store == null) return;
        exportingSaves = true;
        try
        {
            var intent = new Intent(Intent.ActionCreateDocument); intent.AddCategory(Intent.CategoryOpenable); intent.SetType("application/zip");
            intent.PutExtra(Intent.ExtraTitle, "ew4-saves-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            StartActivityForResult(intent, 442);
        }
        catch (Exception error) { SaveExportFinished(false, "无法打开保存窗口：" + error.Message); }
    });
    private void SaveExportFinished(bool ok, string message) => RunOnUiThread(() =>
    {
        exportingSaves = false;
        if (IsFinishing || IsDestroyed) return;
        string result = System.Text.Json.JsonSerializer.Serialize(new { ok, message });
        web?.EvaluateJavascript("window.ew4SaveExportFinished?.(" + result + ")", null);
        if (message.Length > 0) Toast.MakeText(this, message, ToastLength.Long)?.Show();
    });
    private void ExportSaves(global::Android.Net.Uri uri)
    {
        exportingSaves = true;
        string version = PackageManager!.GetPackageInfo(PackageName!, PackageInfoFlags.MetaData)!.VersionName ?? "";
        _ = Task.Run(() =>
        {
            try
            {
                SaveExportResult exported;
                using (var output = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("无法写入文件"))
                    exported = store!.Value.ExportSaves(output, version, "android");
                SaveExportFinished(true, "已导出 " + exported.Files + " 个存档及备份文件");
            }
            catch (Exception error)
            {
                try { global::Android.Provider.DocumentsContract.DeleteDocument(ContentResolver!, uri); } catch (Exception) { }
                SaveExportFinished(false, "存档导出失败：" + error.Message);
            }
        });
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == 442)
        {
            if (resultCode == Result.Ok && data?.Data is { } target && store != null) ExportSaves(target);
            else SaveExportFinished(false, "");
            return;
        }
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
        [JavascriptInterface, Java.Interop.Export("exportSaves")]
        public void ExportSaves() => activity.PickSaveExport();
        [JavascriptInterface, Java.Interop.Export("startup")]
        public void Startup(string state, string message)
        {
            if (activity.web != null && ReferenceEquals(activity.bridge, this)) activity.FrontendStartup(state, message);
        }
    }
    private sealed class LocalClient(MainActivity activity, Func<string, byte[]> read, Lazy<ModStore> store) : WebViewClient
    {
        public override void OnPageStarted(WebView? view, string? url, global::Android.Graphics.Bitmap? favicon)
        {
            if (view != activity.web || url != "https://ew4.local/index.html" || activity.destroyed) return;
            activity.startupComplete = false; activity.startupHasFailed = false; activity.starting = true;
            activity.startupPanel!.Visibility = ViewStates.Gone;
            activity.startupSplash!.Visibility = ViewStates.Visible;
            activity.startupSpinner!.Visibility = ViewStates.Visible;
            activity.startupContinue!.Visibility = ViewStates.Gone;
            activity.SetStartupStage("载入游戏页面");
        }
        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            if (view == activity.web && request?.IsForMainFrame == true) activity.StartupFailed("游戏页面载入失败", new IOException(error?.Description?.ToString()));
        }
        public override bool OnRenderProcessGone(WebView? view, RenderProcessGoneDetail? detail)
        {
            if (view == activity.web)
            {
                activity.ReleaseWebView();
                activity.StartupFailed("网页进程已退出", new IOException(detail?.DidCrash() == true ? "WebView 渲染进程崩溃" : "系统终止了 WebView 渲染进程"));
            }
            return true;
        }
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
                string mime = Path.GetExtension(path) switch { ".html" => "text/html", ".js" => "text/javascript", ".css" => "text/css", ".json" => "application/json", ".png" => "image/png", ".svg" => "image/svg+xml", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf", _ => "application/octet-stream" };
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
