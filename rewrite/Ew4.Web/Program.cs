using Ew4.Mods;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024L * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1024L * 1024 * 1024);
string? root = builder.Configuration["root"];
string data = Path.GetFullPath(builder.Configuration["data-root"] ?? (root == null
    ? Path.Combine(AppContext.BaseDirectory, "data") : Path.Combine(root, "rewrite/data")));
string saves = builder.Configuration["save-root"] ?? (root == null
    ? Path.Combine(AppContext.BaseDirectory, "saves") : Path.Combine(root, "rewrite/local/web"));
string frontend = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
string ResourcePath(string path) => path.StartsWith("frontend/", StringComparison.Ordinal)
    ? Path.Combine(frontend, path[9..]) : Path.Combine(data, path);
byte[] Read(string path) => File.ReadAllBytes(ResourcePath(path));
var files = new[] { "original", "legacy", "client", "rewrite" }.Where(part => Directory.Exists(Path.Combine(data, part))).SelectMany(part => Directory.EnumerateFiles(Path.Combine(data, part), "*", SearchOption.AllDirectories)
    .Where(p => part != "legacy" || Path.GetDirectoryName(p) == Path.Combine(data, part))
    .Select(p => Path.GetRelativePath(data, p).Replace('\\', '/'))).Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]);
ModStore store = null!;
Task StartGame() => Task.Run(() =>
{
    var prepared = StartupAssets.Prepare(new ModBase(Read, files, openRead: p => File.OpenRead(ResourcePath(p))), Path.Combine(saves, "startup-assets"),
        p => System.Console.WriteLine(p.Message));
    store = new ModStore(prepared.Content, builder.Configuration["mod-root"] ?? Path.Combine(saves, "content-mods"), saves);
});
Task startup = StartGame();
object startupGate = new();
var app = builder.Build();
app.Use(async (context, next) =>
{
    string path = context.Request.Path.Value ?? "/";
    Task ready;
    lock (startupGate)
    {
        if (startup.IsFaulted && path is "/" or "/index.html") startup = StartGame();
        ready = startup;
    }
    try { await ready; }
    catch (Exception error)
    {
        context.Response.StatusCode = 503;
        context.Response.Headers.CacheControl = "no-store";
        if (path.StartsWith("/api/", StringComparison.Ordinal))
            await context.Response.WriteAsJsonAsync(new { ok = false, code = "startup.assets", message = error.Message });
        else
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><title>游戏资源检查失败</title>"
                + "<h1>游戏资源检查失败</h1><pre style=\"white-space:pre-wrap\">" + System.Net.WebUtility.HtmlEncode(error.Message)
                + "</pre><button onclick=\"location.reload()\">重试</button></html>");
        }
        return;
    }
    if (path.StartsWith("/api/", StringComparison.Ordinal))
    {
        if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site" || context.Request.Headers.Origin.Count > 0
            && context.Request.Headers.Origin != context.Request.Scheme + "://" + context.Request.Host) { context.Response.StatusCode = 403; return; }
        context.Response.Headers.CacheControl = "no-store";
    }
    if (path.StartsWith("/content/", StringComparison.Ordinal) || path is "/conquest-pages.json" or "/city-names.json")
    {
        try
        {
            context.Response.ContentType = Mime(path); context.Response.Headers.CacheControl = "no-store";
            await context.Response.Body.WriteAsync(path.StartsWith("/content/", StringComparison.Ordinal)
                ? store.ReadContent(path[9..]) : store.Read("frontend" + path)); return;
        }
        catch (Exception e) when (e is IOException or ArgumentException) { context.Response.StatusCode = 404; return; }
    }
    string sourcePath = path == "/" ? "index.html" : path.TrimStart('/');
    bool compatibleAsset = sourcePath.StartsWith("compat/", StringComparison.Ordinal)
        && !sourcePath.Contains("..", StringComparison.Ordinal) && !sourcePath.Contains('\\');
    if ((!sourcePath.Contains('/') || compatibleAsset) && Path.GetExtension(sourcePath) is ".html" or ".css" or ".js")
    {
        string sourceFile = Path.Combine(frontend, sourcePath);
        if (File.Exists(sourceFile))
        {
            context.Response.ContentType = Mime(sourcePath); context.Response.Headers.CacheControl = "no-store";
            await context.Response.Body.WriteAsync(store.Frontend(File.ReadAllBytes(sourceFile), Path.GetExtension(sourcePath))); return;
        }
    }
    await next();
});
app.MapPost("/api/mod-import", async (HttpRequest request) =>
{
    string? temp = null;
    try
    {
        var form = await request.ReadFormAsync(); var file = form.Files.GetFile("file") ?? throw new InvalidDataException("请选择本地资源包");
        if (file.Length > 1024L * 1024 * 1024) throw new InvalidDataException("文件超过1GiB");
        temp = Path.Combine(Path.GetTempPath(), "ew4-import-" + Guid.NewGuid().ToString("N"));
        await using (var output = File.Create(temp)) await file.CopyToAsync(output);
        store.BeginImport(temp, form["id"].ToString(), form["name"].ToString(), deleteSource: true); temp = null;
        return Results.Text(store.Status(), "application/json; charset=utf-8");
    }
    catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { return Results.Json(new { error = e.Message }, statusCode: 400); }
    finally { if (temp != null) File.Delete(temp); }
}).DisableAntiforgery();
app.MapPost("/api/mod-prepare", async (HttpRequest request) =>
{
    string? temp = null;
    try
    {
        var form = await request.ReadFormAsync(); var file = form.Files.GetFile("file") ?? throw new InvalidDataException("请选择本地资源包");
        if (file.Length > 1024L * 1024 * 1024) throw new InvalidDataException("文件超过1GiB");
        temp = Path.Combine(Path.GetTempPath(), "ew4-inspect-" + Guid.NewGuid().ToString("N"));
        await using (var output = File.Create(temp)) await file.CopyToAsync(output);
        var prepared = store.PrepareImport(temp, file.FileName); temp = null;
        return Results.Json(prepared);
    }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
    { return Results.Json(new { error = e.Message }, statusCode: 400); }
    finally { if (temp != null) File.Delete(temp); }
}).DisableAntiforgery();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMethods("/api/{route}", ["GET", "POST"], async (HttpRequest request) =>
{
    if (request.Headers["Sec-Fetch-Site"] == "cross-site") return Results.StatusCode(403);
    if (request.Headers.Origin.Count > 0 && request.Headers.Origin != request.Scheme + "://" + request.Host) return Results.StatusCode(403);
    using var reader = new StreamReader(request.Body);
    string body = request.Query["q"].FirstOrDefault() ?? await reader.ReadToEndAsync();
    string result = store.Dispatch(request.Path, body);
    return Results.Text(result, "application/json; charset=utf-8");
});
app.Run();
static string Mime(string path) => Path.GetExtension(path) switch { ".html" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".png" => "image/png", ".json" => "application/json", ".ttf" => "font/ttf", ".woff2" => "font/woff2", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".ogg" => "audio/ogg", _ => "application/octet-stream" };
