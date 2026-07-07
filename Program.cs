using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Diagnostics;
using System.Text.Json;
using XaaDemo.Services;

var builder = WebApplication.CreateBuilder(args);
var config  = builder.Configuration;

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme          = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o => { o.Cookie.Name = "xaa.auth"; o.ExpireTimeSpan = TimeSpan.FromHours(1); })
    .AddOpenIdConnect(o =>
    {
        o.Authority             = config["Xaa:IdpBaseUrl"];
        o.ClientId              = config["Xaa:ClientId"];
        o.ClientSecret          = config["Xaa:ClientSecret"];
        o.ResponseType          = "code";
        o.UsePkce               = true;
        o.SaveTokens            = true;
        o.CallbackPath          = "/callback";
        o.SignedOutCallbackPath = "/logged-out";
        o.SignedOutRedirectUri  = "http://localhost:5000";
        o.MapInboundClaims = false;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("email");
        o.Scope.Add("profile");
        o.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = ctx => { ctx.Properties!.IsPersistent = false; return Task.CompletedTask; }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient("xaa", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<XaaService>();
builder.Services.AddScoped<McpTodoService>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// Sign out any existing session before challenging — lets a different user authenticate.
app.MapGet("/login", async (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
            new AuthenticationProperties { RedirectUri = "/login" });
        return Results.Empty;
    }
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/?autorun=true" },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapGet("/logout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

app.MapGet("/api/user", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Json(new { loggedIn = false });
    var email = ctx.User.FindFirst("email")?.Value
             ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
             ?? ctx.User.FindFirst("preferred_username")?.Value
             ?? ctx.User.FindFirst("sub")?.Value
             ?? "unknown";
    return Results.Json(new { loggedIn = true, email });
});

app.MapGet("/api/flow", async (HttpContext ctx, XaaService xaa, McpTodoService mcpTodos) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true) { ctx.Response.StatusCode = 401; return; }

    var idToken = await ctx.GetTokenAsync("id_token");
    if (idToken is null) { ctx.Response.StatusCode = 401; return; }

    ctx.Response.ContentType                  = "text/event-stream";
    ctx.Response.Headers["Cache-Control"]     = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = ctx.RequestAborted;
    async Task Send(string evt, string data)
    {
        await ctx.Response.WriteAsync($"event: {evt}\ndata: {data}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    var sw = Stopwatch.StartNew();
    try
    {
        await Send("step",    "Step 1 — User Login (OIDC)");
        await Send("detail",  "ASP.NET Core OIDC middleware completed login at https://idp.xaa.dev");
        await Send("detail",  "PKCE code exchange → ID Token stored in encrypted auth cookie");
        await Send("success", "ID Token obtained from session|0");

        var s2 = Stopwatch.StartNew();
        await Send("step",    "Step 2 — Token Exchange (RFC 8693)");
        await Send("detail",  $"POST {config["Xaa:IdpBaseUrl"]}/token");
        await Send("detail",  "grant_type=token-exchange · subject_token=<id_token>");
        await Send("detail",  "audience=https://auth.resource.xaa.dev · resource=https://mcp.xaa.dev/mcp");
        var accessToken = await xaa.GetAccessTokenAsync(idToken, ct);
        s2.Stop();
        await Send("success", $"ID-JAG assertion issued by IdP|{s2.ElapsedMilliseconds}");

        var s3 = Stopwatch.StartNew();
        await Send("step",    "Step 3 — Access Token Request (RFC 7523)");
        await Send("detail",  $"POST {config["Xaa:AuthServerUrl"]}/token");
        await Send("detail",  "grant_type=jwt-bearer · assertion=<ID-JAG> · scope=todos.read mcp.access");
        s3.Stop();
        await Send("success", $"Bearer token issued (aud: mcp.xaa.dev/mcp)|{s3.ElapsedMilliseconds}");
        await Send("token",   accessToken[..Math.Min(50, accessToken.Length)]);

        try
        {
            var parts   = accessToken.Split('.');
            var padded  = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var payload = System.Text.Json.JsonDocument.Parse(
                           Convert.FromBase64String(padded));
            var iss = payload.RootElement.TryGetProperty("iss", out var i) ? i.GetString() : null;
            var aud = payload.RootElement.TryGetProperty("aud", out var a) ? a.GetString() : null;
            var sub = payload.RootElement.TryGetProperty("sub", out var s) ? s.GetString() : null;
            await Send("claims", System.Text.Json.JsonSerializer.Serialize(new { iss, aud, sub }, jsonOpts));
        }
        catch { /* non-fatal */ }

        var s4 = Stopwatch.StartNew();
        await Send("step",    "Step 4 — MCP Server (Streamable HTTP)");
        await Send("detail",  $"HttpClientTransport (StreamableHttp) → {config["Xaa:McpServerUrl"]}");
        await Send("detail",  "Authorization: Bearer <token> · ReadResource: todo0://todos");

        var (resourceUris, todos, rawContent) = await mcpTodos.FetchAsync(accessToken, ct);
        s4.Stop();
        sw.Stop();

        await Send("detail",  $"Resources: {string.Join(", ", resourceUris)}");
        await Send("success", $"{todos.Count} todo(s) from MCP Server|{s4.ElapsedMilliseconds}");

        await Send("data", JsonSerializer.Serialize(new
        {
            resources  = resourceUris,
            todos,
            rawContent = rawContent.Length > 300 ? rawContent[..300] + "…" : rawContent,
            insights   = BuildInsights(todos),
            totalMs    = sw.ElapsedMilliseconds
        }, jsonOpts));

        await Send("done", "ok");
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        await Send("error", ex.Message);
        await Send("done", "failed");
    }
});

app.Run();

static int PriNum(string? p) => (p?.ToLower()) switch { "high" => 1, "medium" => 2, "low" => 3, _ => 2 };

static object BuildInsights(List<TodoItem> todos)
{
    if (todos.Count == 0)
        return new { summary = "No tasks returned by the MCP server.", items = Array.Empty<object>() };

    var done    = todos.Count(t => t.Completed);
    var total   = todos.Count;
    var rate    = total > 0 ? (double)done / total : 0;
    var pct     = (int)(rate * 100);
    var overdue = todos.Count(t => !t.Completed && t.DueDate.HasValue && t.DueDate < DateTime.UtcNow);
    var p1Open  = todos.Count(t => !t.Completed && PriNum(t.Priority) == 1);
    var topTag  = todos.SelectMany(t => t.Tags ?? []).GroupBy(x => x)
                       .OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

    var mood    = pct >= 80 ? "Excellent progress" : pct >= 50 ? "Good progress"
                : pct >= 30 ? "Getting started"    : "Just beginning";
    var summary = $"{mood} — {done} of {total} tasks complete ({pct}%)."
                + (overdue > 0 ? $" {overdue} task(s) overdue." : "")
                + (p1Open  > 0 ? $" {p1Open} high-priority item(s) still open." : "");

    var focus = todos
        .Where(t => !t.Completed)
        .OrderBy(t => PriNum(t.Priority))
        .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
        .Take(3)
        .Select(t =>
        {
            var label = t.Priority?.ToLower() switch { "high" => "[High] ", "medium" => "[Med] ", _ => "" };
            var due   = t.DueDate.HasValue
                ? (t.DueDate < DateTime.UtcNow ? " · overdue" : $" · due {t.DueDate:MMM d}") : "";
            return $"{label}{t.Title}{due}";
        }).ToList();

    var bullets = new List<object>();
    if (overdue > 0) bullets.Add(new { icon = "⚠️", text = $"{overdue} overdue — reschedule or delegate." });
    if (p1Open  > 0) bullets.Add(new { icon = "🔥", text = $"{p1Open} high-priority item(s) still open." });
    if (topTag  != null) bullets.Add(new { icon = "🏷️", text = $"Most active tag: #{topTag}." });
    if (bullets.Count == 0) bullets.Add(new { icon = "👍", text = "Everything looks healthy!" });

    return new { summary, totalCount = total, completedCount = done, completionRate = rate,
                 overdueCount = overdue, p1Open, todaysFocus = focus, items = bullets };
}
