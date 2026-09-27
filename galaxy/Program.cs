using Galaxy.Auth;
using Galaxy.Core;
using Galaxy.Data;
using Galaxy.Mods;
using Galaxy.OAuth;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 配置
var config = new GalaxyConfig();
builder.Configuration.Bind("Galaxy", config);
builder.Services.AddSingleton(config);

// 服务注册
builder.Services.AddGalaxyData(config.DatabasePath);
builder.Services.AddGalaxyAuth();
builder.Services.AddGalaxyMods();
builder.Services.AddGalaxyOAuth();

// CORS
builder.Services.AddCors(o => o.AddDefaultPolicy(b => b
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// 初始化数据库
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<GalaxyDbInitializer>();
    await initializer.InitializeAsync(config);
}

app.UseCors();
app.UseMiddleware<AuthMiddleware>();

// API 路由
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

RegisterAuthRoutes(app, jsonOpts);
RegisterModRoutes(app, jsonOpts);
RegisterDeveloperRoutes(app, jsonOpts);
RegisterOAuth2Routes(app, jsonOpts);
RegisterAdminRoutes(app, jsonOpts);

// 静态文件服务（Vue 前端）
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = new FileExtensionContentTypeProvider()
});
app.MapFallbackToFile("index.html");

var port = Environment.GetEnvironmentVariable("GALAXY_PORT") ?? "9800";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"Galaxy 服务已启动: http://0.0.0.0:{port}");
app.Run();

static void RegisterAuthRoutes(WebApplication app, JsonSerializerOptions jsonOpts)
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<GalaxyDbContext>>();

    // 公开设置查询（注册状态 + SMTP状态 + 开发者审核状态）
    app.MapGet("/api/auth/settings", async (HttpContext ctx) =>
    {
        using var db = await dbFactory.CreateDbContextAsync();
        var regOpen = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "registration_open");
        var smtpEnabled = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "smtp_enabled");
        var devApproval = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == "developer_require_approval");
        return Results.Json(new
        {
            status = 200,
            data = new
            {
                registration_open = regOpen?.Value ?? "true",
                smtp_enabled = smtpEnabled?.Value ?? "false",
                developer_require_approval = devApproval?.Value ?? "true"
            }
        }, jsonOpts);
    });

    app.MapPost("/api/auth/register", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var body = await JsonSerializer.DeserializeAsync<RegisterBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);
        var result = await auth.RegisterAsync(body.Username, body.Email, body.Password, body.VerifyCode);
        return Results.Json(result, jsonOpts);
    });

    app.MapPost("/api/auth/send-verify-code", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var emailService = ctx.RequestServices.GetRequiredService<EmailService>();
        var body = await JsonSerializer.DeserializeAsync<SendVerifyCodeBody>(req.Body, jsonOpts, ct);
        if (body is null || string.IsNullOrWhiteSpace(body.Email))
            return Results.Json(new { status = 400, message = "邮箱不能为空" }, jsonOpts);
        var result = await emailService.SendVerifyCodeAsync(body.Email);
        return Results.Json(result, jsonOpts);
    });

    app.MapPost("/api/auth/login", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var body = await JsonSerializer.DeserializeAsync<LoginBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);
        var result = await auth.LoginAsync(body.Username, body.Password);
        return Results.Json(result, jsonOpts);
    });

    app.MapGet("/api/auth/me", (HttpContext ctx) =>
    {
        var user = ctx.Items["User"];
        if (user is ClaimsPrincipal principal)
        {
            return Results.Json(new
            {
                status = 200,
                data = new
                {
                    id = int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"),
                    username = principal.FindFirst(ClaimTypes.Name)?.Value,
                    permissions = principal.FindFirst("permissions")?.Value,
                    isDeveloper = principal.FindFirst("isDeveloper")?.Value == "true"
                }
            }, jsonOpts);
        }
        if (user is User u)
        {
            return Results.Json(new
            {
                status = 200,
                data = new { id = u.Id, username = u.Username, permissions = u.Permissions, isDeveloper = u.IsDeveloper }
            }, jsonOpts);
        }
        return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);
    });

    app.MapPost("/api/auth/api-key", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.ApiKeyManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var body = await JsonSerializer.DeserializeAsync<CreateApiKeyBody>(req.Body, jsonOpts, ct);
        if (body is null || string.IsNullOrWhiteSpace(body.Name))
            return Results.Json(new { status = 400, message = "名称不能为空" }, jsonOpts);

        var result = await auth.CreateApiKeyAsync(userId.Value, body.Name, body.Permissions);
        return Results.Json(result, jsonOpts);
    });

    app.MapGet("/api/auth/api-key", async (HttpContext ctx) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);
        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var result = await auth.ListApiKeysAsync(userId.Value);
        return Results.Json(result, jsonOpts);
    });

    app.MapPut("/api/auth/api-key/{id}/permissions", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.ApiKeyManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var body = await JsonSerializer.DeserializeAsync<UpdateApiKeyPermissionsBody>(req.Body, jsonOpts, ct);
        if (body is null || body.Permissions is null)
            return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var result = await auth.UpdateApiKeyPermissionsAsync(userId.Value, id, body.Permissions);
        return Results.Json(result, jsonOpts);
    });

    app.MapDelete("/api/auth/api-key/{id}", async (HttpContext ctx, int id) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);
        var auth = ctx.RequestServices.GetRequiredService<AuthService>();
        var result = await auth.RevokeApiKeyAsync(userId.Value, id);
        return Results.Json(result, jsonOpts);
    });
}

static void RegisterModRoutes(WebApplication app, JsonSerializerOptions jsonOpts)
{
    app.MapPost("/api/mod/submit/{type}", async (string type, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.ModSubmit, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        if (type != "client" && type != "server")
            return Results.Json(new { status = 400, message = "无效的类型参数" }, jsonOpts);

        var body = await JsonSerializer.DeserializeAsync<SubmitModBody>(req.Body, jsonOpts, ct);
        if (body is null || string.IsNullOrWhiteSpace(body.Modid))
            return Results.Json(new { status = 400, message = "未提供 modid" }, jsonOpts);

        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var userId = GetUserId(ctx) ?? 0;
        var modIds = body.Modid.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<object>();
        foreach (var mid in modIds)
        {
            var r = await modService.SubmitAsync(mid, type, userId);
            if (r.Data is not null) results.Add(r.Data);
        }
        return Results.Json(new { status = 200, data = results }, jsonOpts);
    });

    app.MapGet("/api/mod/{modId}", async (HttpContext ctx, string modId) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var result = await modService.GetByModIdAsync(modId, onlyApproved: true);
        return Results.Json(result, jsonOpts);
    });

    app.MapGet("/api/mod/search", async (HttpContext ctx, string? q, int? page, int? pageSize, int? status) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        ModStatus? modStatus = status.HasValue ? (ModStatus)status.Value : null;
        var result = await modService.SearchAsync(q, page ?? 1, pageSize ?? 20, modStatus, onlyApproved: true);
        return Results.Json(result, jsonOpts);
    });

    app.MapGet("/api/mod/stats", async (HttpContext ctx) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var result = await modService.GetStatsAsync();
        return Results.Json(result, jsonOpts);
    });
}

static void RegisterDeveloperRoutes(WebApplication app, JsonSerializerOptions jsonOpts)
{
    app.MapPost("/api/developer/apply", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var devService = ctx.RequestServices.GetRequiredService<DeveloperService>();
        var body = await JsonSerializer.DeserializeAsync<DeveloperApplyBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var result = await devService.ApplyAsync(userId.Value, body.DeveloperName, body.Purpose, body.WebsiteUrl, body.ContactInfo);
        return Results.Json(result, jsonOpts);
    });

    app.MapGet("/api/developer/status", async (HttpContext ctx) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var devService = ctx.RequestServices.GetRequiredService<DeveloperService>();
        var result = await devService.GetMyStatusAsync(userId.Value);
        return Results.Json(result, jsonOpts);
    });
}

static void RegisterOAuth2Routes(WebApplication app, JsonSerializerOptions jsonOpts)
{
    // 授权页面（GET - 返回前端页面，由前端处理）
    app.MapGet("/api/oauth2/authorize", async (HttpContext ctx, string? client_id, string? redirect_uri, string? scope, string? state, string? response_type) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null)
        {
            // 未登录，重定向到登录页
            var loginUrl = $"/login?redirect={Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString)}";
            ctx.Response.Redirect(loginUrl);
            return;
        }

        if (string.IsNullOrWhiteSpace(client_id) || string.IsNullOrWhiteSpace(redirect_uri) || response_type != "code")
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { status = 400, message = "参数错误" }));
            return;
        }

        // 前端会处理展示授权确认页
        ctx.Response.Redirect($"/oauth2/authorize?client_id={Uri.EscapeDataString(client_id)}&redirect_uri={Uri.EscapeDataString(redirect_uri)}&scope={Uri.EscapeDataString(scope ?? "")}&state={Uri.EscapeDataString(state ?? "")}");
    });

    // 用户确认授权（POST）
    app.MapPost("/api/oauth2/authorize", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var body = await JsonSerializer.DeserializeAsync<OAuthAuthorizeBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        if (!body.Approved)
        {
            var denyUrl = $"{body.RedirectUri}?error=access_denied&state={Uri.EscapeDataString(body.State ?? "")}";
            return Results.Json(new { status = 200, data = new { redirect_url = denyUrl } }, jsonOpts);
        }

        var oauth2Service = ctx.RequestServices.GetRequiredService<OAuth2Service>();
        var result = await oauth2Service.CreateAuthorizationCodeAsync(userId.Value, body.ClientId, body.RedirectUri, body.Scope ?? "[]", body.State ?? "");
        var authCode = result.Data;
        if (result.Status != 200 || string.IsNullOrEmpty(authCode)) return Results.Json(result, jsonOpts);

        var redirectUrl = $"{body.RedirectUri}?code={Uri.EscapeDataString(authCode)}&state={Uri.EscapeDataString(body.State ?? "")}";
        return Results.Json(new { status = 200, data = new { redirect_url = redirectUrl } }, jsonOpts);
    });

    // 授权码换取 token
    app.MapPost("/api/oauth2/token", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var body = await JsonSerializer.DeserializeAsync<OAuthTokenBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var oauth2Service = ctx.RequestServices.GetRequiredService<OAuth2Service>();
        var result = await oauth2Service.ExchangeCodeForTokenAsync(body.Code, body.ClientId, body.ClientSecret, body.RedirectUri);
        return Results.Json(result, jsonOpts);
    });

    // 获取应用信息（用于授权确认页）
    app.MapGet("/api/oauth2/app-info", async (HttpContext ctx, string? client_id) =>
    {
        if (string.IsNullOrWhiteSpace(client_id))
            return Results.Json(new { status = 400, message = "client_id 不能为空" }, jsonOpts);

        var oauth2Service = ctx.RequestServices.GetRequiredService<OAuth2Service>();
        var result = await oauth2Service.GetAppInfoForAuthorizationAsync(client_id);
        return Results.Json(result, jsonOpts);
    });

    // 注册 OAuth 应用
    app.MapPost("/api/oauth2/apps", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var body = await JsonSerializer.DeserializeAsync<CreateOAuthAppBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = await oauthAppService.CreateAppAsync(userId.Value, body.AppName, body.RedirectUris, body.Scopes);
        return Results.Json(result, jsonOpts);
    });

    // 列出我的 OAuth 应用
    app.MapGet("/api/oauth2/apps", async (HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = await oauthAppService.ListMyAppsAsync(userId.Value);
        return Results.Json(result, jsonOpts);
    });

    // 更新 OAuth 应用
    app.MapPut("/api/oauth2/apps/{id}", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var body = await JsonSerializer.DeserializeAsync<UpdateOAuthAppBody>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = await oauthAppService.UpdateAppAsync(userId.Value, id, body.AppName, body.RedirectUris, body.Scopes);
        return Results.Json(result, jsonOpts);
    });

    // 删除 OAuth 应用
    app.MapDelete("/api/oauth2/apps/{id}", async (int id, HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = await oauthAppService.DeleteAppAsync(userId.Value, id);
        return Results.Json(result, jsonOpts);
    });
}

static void RegisterAdminRoutes(WebApplication app, JsonSerializerOptions jsonOpts)
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<GalaxyDbContext>>();

    // 用户管理
    app.MapGet("/api/admin/users", async (HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var users = await db.Users.Select(u => new
        {
            u.Id, u.Username, u.Email, u.Permissions, u.IsDisabled, u.IsDeveloper, u.CreatedAt
        }).ToListAsync();
        return Results.Json(new { status = 200, data = users }, jsonOpts);
    });

    app.MapPut("/api/admin/users/{id}/permissions", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return Results.Json(new { status = 404, message = "用户不存在" }, jsonOpts);
        }

        var body = await JsonSerializer.DeserializeAsync<UpdatePermissionsBody>(req.Body, jsonOpts, ct);
        if (body is null || body.Permissions is null)
        {
            return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);
        }

        user.Permissions = JsonSerializer.Serialize(body.Permissions);
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = "权限已更新" }, jsonOpts);
    });

    app.MapPut("/api/admin/users/{id}/toggle", async (int id, HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return Results.Json(new { status = 404, message = "用户不存在" }, jsonOpts);
        }
        user.IsDisabled = !user.IsDisabled;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = user.IsDisabled ? "用户已禁用" : "用户已启用" }, jsonOpts);
    });

    app.MapPost("/api/admin/users", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var body = await JsonSerializer.DeserializeAsync<CreateUserBody>(req.Body, jsonOpts, ct);
        if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            return Results.Json(new { status = 400, message = "用户名和密码不能为空" }, jsonOpts);

        using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(u => u.Username == body.Username))
            return Results.Json(new { status = 409, message = "用户名已存在" }, jsonOpts);
        if (!string.IsNullOrWhiteSpace(body.Email) && await db.Users.AnyAsync(u => u.Email == body.Email))
            return Results.Json(new { status = 409, message = "邮箱已注册" }, jsonOpts);

        var user = new User
        {
            Username = body.Username,
            Email = body.Email ?? "",
            PasswordHash = GalaxyDbInitializer.HashPassword(body.Password),
            Permissions = JsonSerializer.Serialize(body.Permissions ?? GalaxyPermissions.Default.ToList()),
            IsDisabled = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = "用户已创建" }, jsonOpts);
    });

    app.MapPut("/api/admin/users/{id}", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var body = await JsonSerializer.DeserializeAsync<UpdateUserBody>(req.Body, jsonOpts, ct);
        if (body is null)
            return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.Json(new { status = 404, message = "用户不存在" }, jsonOpts);

        if (!string.IsNullOrWhiteSpace(body.Email)) user.Email = body.Email;
        if (!string.IsNullOrWhiteSpace(body.Password))
            user.PasswordHash = GalaxyDbInitializer.HashPassword(body.Password);
        if (body.Permissions is not null)
            user.Permissions = JsonSerializer.Serialize(body.Permissions);
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = "用户已更新" }, jsonOpts);
    });

    app.MapDelete("/api/admin/users/{id}", async (int id, HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.Json(new { status = 404, message = "用户不存在" }, jsonOpts);

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = "用户已删除" }, jsonOpts);
    });

    // 模组管理
    app.MapGet("/api/admin/mods", async (HttpContext ctx, int? page, int? pageSize, int? status) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        ModStatus? modStatus = status.HasValue ? (ModStatus)status.Value : null;
        var result = await modService.SearchAsync(null, page ?? 1, pageSize ?? 50, modStatus);
        return Results.Json(result, jsonOpts);
    });

    app.MapPost("/api/admin/mods/{id}/review", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.ModManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var body = await JsonSerializer.DeserializeAsync<ReviewModBody>(req.Body, jsonOpts, ct);
        if (body is null)
            return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var statusValue = body.Status;
        if (statusValue < 0 || statusValue > 2)
            return Results.Json(new { status = 400, message = "无效的状态值" }, jsonOpts);

        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var result = await modService.ReviewAsync(id, (ModStatus)statusValue, body.ReviewNote);
        return Results.Json(result, jsonOpts);
    });

    app.MapPut("/api/admin/mods/{id}", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var body = await JsonSerializer.DeserializeAsync<UpdateModBody>(req.Body, jsonOpts, ct);
        var result = await modService.UpdateModAsync(id, body?.ClientOk, body?.ServerOk, body?.Note, body?.ReviewNote);
        return Results.Json(result, jsonOpts);
    });

    app.MapDelete("/api/admin/mods/{id}", async (int id, HttpContext ctx) =>
    {
        var modService = ctx.RequestServices.GetRequiredService<ModService>();
        var result = await modService.DeleteModAsync(id);
        return Results.Json(result, jsonOpts);
    });

    // 开发者管理
    app.MapGet("/api/admin/developers", async (HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var devService = ctx.RequestServices.GetRequiredService<DeveloperService>();
        var result = await devService.ListApplicationsAsync();
        return Results.Json(result, jsonOpts);
    });

    app.MapPut("/api/admin/developers/{id}/review", async (int id, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.UserManage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var userId = GetUserId(ctx);
        if (userId is null) return Results.Json(new { status = 401, message = "未认证" }, jsonOpts);

        var body = await JsonSerializer.DeserializeAsync<ReviewDeveloperBody>(req.Body, jsonOpts, ct);
        if (body is null)
            return Results.Json(new { status = 400, message = "无效的请求" }, jsonOpts);

        var devService = ctx.RequestServices.GetRequiredService<DeveloperService>();
        var result = await devService.ReviewAsync(id, body.Approved, body.ReviewNote, userId.Value);
        return Results.Json(result, jsonOpts);
    });

    // OAuth 应用管理（管理员）
    app.MapGet("/api/admin/oauth-apps", async (HttpContext ctx, int? developerUserId) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = developerUserId.HasValue
            ? await oauthAppService.ListAppsByDeveloperAsync(developerUserId.Value)
            : await oauthAppService.ListAllAppsAsync();
        return Results.Json(result, jsonOpts);
    });

    app.MapPut("/api/admin/oauth-apps/{id}/toggle", async (int id, HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.OAuth2Manage, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        var oauthAppService = ctx.RequestServices.GetRequiredService<OAuthAppService>();
        var result = await oauthAppService.ToggleAppAsync(id);
        return Results.Json(result, jsonOpts);
    });

    // 系统设置
    app.MapGet("/api/admin/settings", async (HttpContext ctx) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.SystemSettings, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return Results.Json(new { status = 200, data = settings }, jsonOpts);
    });

    app.MapPut("/api/admin/settings", async (HttpContext ctx, HttpRequest req, CancellationToken ct) =>
    {
        await PermissionMiddleware.RequirePermission(ctx, GalaxyPermissions.SystemSettings, async () => { });
        if (ctx.Response.HasStarted) return Results.Empty;

        using var db = await dbFactory.CreateDbContextAsync();
        var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(req.Body, jsonOpts, ct);
        if (body is null) return Results.Empty;

        // 处理 SMTP 密码加密
        if (body.TryGetValue("smtp_password", out var smtpPwd) && !string.IsNullOrEmpty(smtpPwd))
        {
            var config = ctx.RequestServices.GetRequiredService<GalaxyConfig>();
            body["smtp_password"] = GalaxyDbInitializer.EncryptSmtpPassword(smtpPwd, config.JwtSecret);
        }

        foreach (var (key, value) in body)
        {
            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting is not null)
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
            }
        }
        await db.SaveChangesAsync();
        return Results.Json(new { status = 200, message = "设置已更新" }, jsonOpts);
    });
}

static int? GetUserId(HttpContext ctx)
{
    var user = ctx.Items["User"];
    if (user is ClaimsPrincipal principal)
        return int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    if (user is User u) return u.Id;
    return null;
}

// 请求体模型
record RegisterBody(string Username, string Email, string Password, string? VerifyCode);
record LoginBody(string Username, string Password);
record SendVerifyCodeBody(string Email);
record CreateApiKeyBody(string Name, List<string>? Permissions);
record UpdateApiKeyPermissionsBody(List<string> Permissions);
record SubmitModBody(string Modid);
record UpdatePermissionsBody(List<string> Permissions);
record UpdateModBody(bool? ClientOk, bool? ServerOk, string? Note, string? ReviewNote);
record ReviewModBody(int Status, string? ReviewNote);
record CreateUserBody(string Username, string? Email, string Password, List<string>? Permissions);
record UpdateUserBody(string? Email, string? Password, List<string>? Permissions);
record DeveloperApplyBody(string DeveloperName, string Purpose, string? WebsiteUrl, string? ContactInfo);
record OAuthAuthorizeBody(string ClientId, string RedirectUri, string? Scope, string? State, bool Approved);
record OAuthTokenBody(string Code, string ClientId, string ClientSecret, string RedirectUri);
record CreateOAuthAppBody(string AppName, List<string> RedirectUris, List<string> Scopes);
record UpdateOAuthAppBody(string? AppName, List<string>? RedirectUris, List<string>? Scopes);
record ReviewDeveloperBody(bool Approved, string? ReviewNote);
