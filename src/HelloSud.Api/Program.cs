using Sud.Gip.Api;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var appId = builder.Configuration["APP_ID"];
var secret = builder.Configuration["APP_SECRET"];
if (string.IsNullOrWhiteSpace(appId) || secret is null || Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Set APP_ID and APP_SECRET (at least 32 UTF-8 bytes) before starting.");
builder.Services.AddSingleton(new SudGIPAuth(appId, secret));
builder.Services.AddSingleton(new RequestSigner(appId, secret));
// Reject an invalid switch instead of silently disabling callback verification.
_ = builder.Configuration.GetValue<bool>("VERIFY_CALLBACK_SIGNATURE");
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ret_code = 1, ret_msg = "Invalid JSON request", sdk_error_code = 0, data = new { } });
    }
    catch (Exception)
    {
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { ret_code = 1, ret_msg = "Internal Server Error", sdk_error_code = 0, data = new { } });
    }
});
// Explicit routing keeps callback verification ahead of endpoint selection and request binding.
app.UseMiddleware<CallbackSignatureMiddleware>();
app.UseRouting();
app.Use(async (context, next) =>
{
    await next();
    if (context.Response.StatusCode == 405 && !context.Response.HasStarted)
    {
        await context.Response.WriteAsJsonAsync(new { ret_code = 1, ret_msg = "Method Not Allowed", sdk_error_code = 0, data = new { } });
    }
});
app.MapGet("/health", () => Success(new { status = "ok" }));
app.MapGet("/api/getCode", (string? uid, SudGIPAuth auth) =>
    string.IsNullOrWhiteSpace(uid) ? Error("Missing uid parameter") : Success(new { code = auth.GetCode(uid).Code }));
app.MapPost("/api/getSSToken", async (HttpRequest request, SudGIPAuth auth) =>
{
    var code = await ReadField(request, "code");
    if (string.IsNullOrWhiteSpace(code))
    {
        return Error("Missing code parameter");
    }
    var result = auth.GetUidByCode(code);
    if (!result.IsSuccess)
    {
        return Error("", result.ErrorCode);
    }
    if (string.IsNullOrWhiteSpace(result.Uid))
    {
        return Error("Missing token uid", 1004);
    }
    var token = auth.GetSSToken(result.Uid);
    // Replace the fixed profile values with your user service; keep the verified UID.
    return Success(new
    {
        ss_token = token.Token,
        expire_date = token.ExpireDate,
        user_info = new
        {
            uid = result.Uid,
            nick_name = "Demo User",
            avatar_url = "https://example.com/avatar.png",
            gender = "female",
            is_ai = 0,
            ai_level = 0
        }
    });
});
app.MapPost("/api/updateSSToken", async (HttpRequest request, SudGIPAuth auth) =>
{
    var value = await ReadField(request, "ss_token");
    if (string.IsNullOrWhiteSpace(value))
    {
        return Error("Missing ss_token parameter");
    }
    var result = auth.GetUidBySSToken(value);
    if (!result.IsSuccess)
    {
        return Error("", result.ErrorCode);
    }
    if (string.IsNullOrWhiteSpace(result.Uid))
    {
        return Error("Missing token uid", 1004);
    }
    var token = auth.GetSSToken(result.Uid);
    return Success(new { ss_token = token.Token, expire_date = token.ExpireDate });
});
app.MapFallback((HttpContext context) =>
{
    // A fallback otherwise masks method mismatches as 404 in Minimal APIs.
    var path = context.Request.Path.Value ?? "";
    var allowed = path.Equals("/api/getCode", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/health", StringComparison.OrdinalIgnoreCase) ? "GET"
        : path.Equals("/api/getSSToken", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/updateSSToken", StringComparison.OrdinalIgnoreCase) ? "POST" : null;
    if (allowed is not null)
    {
        context.Response.Headers.Allow = allowed;
    }
    return Results.Json(new
    {
        ret_code = 1,
        ret_msg = allowed is null ? "Not Found" : "Method Not Allowed",
        sdk_error_code = 0,
        data = new { }
    }, statusCode: allowed is null ? 404 : 405);
});
app.Run();

static IResult Success(object data) => Results.Json(new { ret_code = 0, ret_msg = "", sdk_error_code = 0, data });
// SDK business errors use HTTP 200; clients must inspect ret_code and sdk_error_code.
static IResult Error(string message, int code = 0) => Results.Json(new { ret_code = 1, ret_msg = message, sdk_error_code = code, data = new { } }, statusCode: code == 0 ? 400 : 200);
// Called only after callback middleware has verified and rewound the original body.
static async Task<string?> ReadField(HttpRequest request, string name)
{
    if (!request.HasJsonContentType())
    {
        throw new BadHttpRequestException("Expected JSON");
    }
    using var json = await JsonDocument.ParseAsync(request.Body);
    if (json.RootElement.ValueKind != JsonValueKind.Object)
    {
        throw new JsonException();
    }
    if (!json.RootElement.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null)
    {
        return null;
    }
    if (field.ValueKind != JsonValueKind.String)
    {
        throw new JsonException();
    }
    return field.GetString();
}
