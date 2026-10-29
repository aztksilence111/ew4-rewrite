using Android.App;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using System.Text.Json;

namespace Ew4.Android;

public sealed partial class MainActivity
{
    private sealed record StartupSnapshot(string Stage, string WebView, string Error, bool Complete);
    private readonly object startupGate = new();
    private LinearLayout? startupPanel;
    private ImageView? startupSplash;
    private global::Android.Graphics.Bitmap? startupArtwork;
    private TextView? startupText;
    private ProgressBar? startupSpinner;
    private Button? startupContinue;
    private Func<string, byte[]>? assetRead;
    private string startupFile = "", startupStage = "准备启动", webViewVersion = "尚未初始化", previousExit = "", startupVersion = "";
    private bool previousStartupIncomplete, startupComplete, startupHasFailed;
    private volatile bool destroyed;

    private void CreateStartupSurface()
    {
        startupFile = Path.Combine(FilesDir!.AbsolutePath, "startup-state.json");
        startupVersion = PackageManager!.GetPackageInfo(PackageName!, 0)!.VersionName ?? "未知";
        StartupSnapshot? previous = null;
        try
        {
            if (new FileInfo(startupFile) is { Exists: true, Length: < 16384 })
                previous = JsonSerializer.Deserialize<StartupSnapshot>(File.ReadAllText(startupFile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        previousStartupIncomplete = previous is { Complete: false, Stage.Length: > 0 and < 256, WebView: not null, Error: not null };
        surface = new FrameLayout(this);
        surface.SetBackgroundColor(global::Android.Graphics.Color.Rgb(24, 26, 29));
        startupSplash = new ImageView(this);
        startupSplash.SetBackgroundColor(global::Android.Graphics.Color.White);
        startupSplash.SetScaleType(ImageView.ScaleType.FitCenter);
        try
        {
            using var image = Assets!.Open("www/startup-loading.png");
            startupArtwork = global::Android.Graphics.BitmapFactory.DecodeStream(image);
            startupSplash.SetImageBitmap(startupArtwork);
        }
        catch (Exception error) { global::Android.Util.Log.Warn("EW4", "Startup artwork: " + error.Message); }
        surface.AddView(startupSplash, new FrameLayout.LayoutParams(-1, -1));
        startupPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        startupPanel.SetBackgroundColor(global::Android.Graphics.Color.Rgb(24, 26, 29));
        startupPanel.SetGravity(GravityFlags.Center);
        int pad = (int)(20 * Resources!.DisplayMetrics!.Density);
        startupPanel.SetPadding(pad, pad, pad, pad);
        var title = new TextView(this) { Text = "欧陆战争4", TextSize = 22, Gravity = GravityFlags.Center };
        title.SetTextColor(global::Android.Graphics.Color.White);
        startupPanel.AddView(title, new LinearLayout.LayoutParams(-1, -2));
        startupText = new TextView(this) { TextSize = 15, Gravity = GravityFlags.Center };
        startupText.SetTextColor(global::Android.Graphics.Color.Rgb(220, 225, 230));
        startupText.SetPadding(0, pad / 2, 0, pad / 2);
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(startupText);
        startupPanel.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        startupSpinner = new ProgressBar(this) { Indeterminate = true };
        startupPanel.AddView(startupSpinner, new LinearLayout.LayoutParams(pad * 2, pad * 2));
        startupContinue = new Button(this) { Text = "继续启动", Visibility = ViewStates.Gone };
        startupContinue.Click += (_, _) => StartGame();
        startupPanel.AddView(startupContinue, new LinearLayout.LayoutParams(-2, -2));
        surface.AddView(startupPanel, new FrameLayout.LayoutParams(-1, -1));
        SetContentView(surface);
        if (previousStartupIncomplete)
        {
            ReadPreviousExit();
            startupSplash.Visibility = ViewStates.Gone;
            startupPanel.Visibility = ViewStates.Visible;
            startupStage = previous!.Stage; webViewVersion = previous.WebView!;
            startupText.Text = StartupDescription("上次启动未完成，停在：" + startupStage, previous.Error!);
            startupSpinner.Visibility = ViewStates.Gone;
            startupContinue.Visibility = ViewStates.Visible;
        }
        else startupText.Text = StartupDescription(startupStage, "");
    }

    private string StartupDescription(string stage, string error) => stage + "\n" + Build.Manufacturer + " " + Build.Model
        + " · Android " + Build.VERSION.Release + "（API " + (int)Build.VERSION.SdkInt + "）"
        + "\nCPU 架构：" + string.Join(", ", Build.SupportedAbis ?? [])
        + " · 进程：" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
        + "\n硬件：" + Build.Hardware + " · " + Build.Board
        + "\n显示：" + Resources!.DisplayMetrics!.WidthPixels + " × " + Resources.DisplayMetrics.HeightPixels
        + " · " + Resources.DisplayMetrics.DensityDpi + " dpi"
        + "\nWebView：" + webViewVersion
        + "\n版本：" + startupVersion
        + (previousExit.Length == 0 ? "" : "\n" + previousExit) + (error.Length == 0 ? "" : "\n" + error);

    private void SetStartupStage(string stage, string error = "", bool complete = false)
    {
        if (destroyed) return;
        string text;
        lock (startupGate)
        {
            startupStage = stage;
            text = StartupDescription(stage, error);
            try
            {
                // Keep one small checkpoint so a system kill can be reported on the next launch.
                File.WriteAllText(startupFile + ".tmp", JsonSerializer.Serialize(new StartupSnapshot(stage, webViewVersion, error, complete)));
                File.Move(startupFile + ".tmp", startupFile, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { global::Android.Util.Log.Warn("EW4", "Startup checkpoint: " + e.Message); }
        }
        RunOnUiThread(() => { if (!destroyed) startupText!.Text = text; });
    }

    private void ReadPreviousExit()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return;
        try
        {
            using var manager = GetSystemService(ActivityService) as ActivityManager;
            using var last = manager?.GetHistoricalProcessExitReasons(PackageName!, 0, 1)?.FirstOrDefault();
            if (last == null) return;
            string reason = (int)last.Reason switch { 3 => "内存不足", 4 or 5 => "崩溃", 6 => "无响应（ANR）", _ => "原因码 " + (int)last.Reason };
            previousExit = "系统记录上次退出：" + reason;
        }
        catch (Exception error) { global::Android.Util.Log.Warn("EW4", "Previous exit: " + error.Message); }
    }

    private void CreateGameWebView()
    {
        if (destroyed || IsFinishing) return;
        try
        {
            SetStartupStage("初始化系统网页组件");
            using var package = WebView.CurrentWebViewPackage;
            webViewVersion = package?.PackageName + " " + package?.VersionName;
            SetStartupStage("创建游戏页面");
#if EW4_DIAGNOSTICS
            WebView.SetWebContentsDebuggingEnabled(true);
#endif
            ReleaseWebView();
            web = new WebView(this);
            web.SetBackgroundColor(global::Android.Graphics.Color.Black);
            web.Settings.JavaScriptEnabled = true;
            web.Settings.DomStorageEnabled = true;
            // Music transitions can occur after the initial user gesture.
            web.Settings.MediaPlaybackRequiresUserGesture = false;
            web.Settings.AllowFileAccess = false;
            web.Settings.AllowContentAccess = false;
            web.Settings.MixedContentMode = MixedContentHandling.NeverAllow;
            web.Settings.SetSupportZoom(false);
            web.Settings.BuiltInZoomControls = false;
            web.Settings.DisplayZoomControls = false;
            bridge = new ImportBridge(this); web.AddJavascriptInterface(bridge, "Ew4ModImport");
            web.SetWebViewClient(new LocalClient(this, assetRead!, store!));
            web.SetWebChromeClient(new LocalChrome());
            surface!.AddView(web, 0, new FrameLayout.LayoutParams(-1, -1));
            SetStartupStage("载入游戏页面");
            web.LoadUrl("https://ew4.local/index.html");
        }
        catch (Exception error) { StartupFailed("网页组件初始化失败", error); }
    }

    private void ReleaseWebView()
    {
        var old = web; web = null;
        if (old != null)
        {
            surface?.RemoveView(old);
            old.RemoveJavascriptInterface("Ew4ModImport");
            old.Destroy(); old.Dispose();
        }
        bridge?.Dispose(); bridge = null;
    }

    private void StartupFailed(string stage, Exception error)
    {
        if (destroyed) return;
        RunOnUiThread(() =>
        {
            if (destroyed || IsFinishing) return;
            starting = false; startupComplete = false; startupHasFailed = true;
            string message = "错误类型：" + error.GetType().Name + "\n" + error.Message;
            SetStartupStage(stage, message.Length > 1200 ? message[..1200] : message);
            startupPanel!.Visibility = ViewStates.Visible;
            startupSplash!.Visibility = ViewStates.Gone;
            startupSpinner!.Visibility = ViewStates.Gone;
            startupContinue!.Text = "重试";
            startupContinue.Visibility = ViewStates.Visible;
        });
    }

    private void FrontendStartup(string state, string message)
    {
        if (destroyed) return;
        RunOnUiThread(() =>
        {
            if (destroyed || startupComplete) return;
            if (state == "ready")
            {
                if (startupHasFailed) return;
                startupComplete = true; starting = false; startupHasFailed = false;
                SetStartupStage("启动完成", complete: true);
                startupPanel!.Visibility = ViewStates.Gone;
                startupSplash!.Visibility = ViewStates.Gone;
            }
            else if (state == "pending" && !startupHasFailed) SetStartupStage(message.Length > 120 ? message[..120] : message);
            else if (state == "error") StartupFailed("游戏页面启动失败", new InvalidOperationException(message));
        });
    }

}
