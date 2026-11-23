using Sud.Gip.Api;

/// <summary>Optional request authentication before JSON binding or SDK calls.</summary>
public sealed class CallbackSignatureMiddleware
{
    private readonly RequestDelegate next;
    private readonly RequestSigner signer;
    private readonly bool enabled;

    public CallbackSignatureMiddleware(RequestDelegate next, IConfiguration configuration, RequestSigner signer)
    {
        this.next = next;
        this.signer = signer;
        enabled = configuration.GetValue<bool>("VERIFY_CALLBACK_SIGNATURE");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        // Cover future callback endpoints too. getCode is client-facing, not a callback.
        if (!enabled || !request.Path.StartsWithSegments("/api")
            || string.Equals(request.Path.Value, "/api/getCode", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // SECURITY: Preserve the original entity-body bytes. Do not deserialize JSON, re-serialize
        // a DTO, decode/re-encode text, trim, reorder fields, or normalize line endings before
        // verification. This check detects tampering even when the JSON values remain equivalent.
        // EnableBuffering allows downstream handlers to read exactly the same verified bytes.
        request.EnableBuffering();
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, context.RequestAborted);
        request.Body.Position = 0;
        if (!Verify(request, body.ToArray()))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                ret_code = 1,
                ret_msg = "Invalid callback signature",
                sdk_error_code = 0,
                data = new { }
            });
            return;
        }
        // JSON parsing is permitted only after successful verification of the original bytes.
        await next(context);
    }

    private bool Verify(HttpRequest request, byte[] body)
    {
        // Reject ambiguous duplicate headers in the HTTP adapter.
        foreach (var name in new[] { "Sud-AppId", "Sud-Timestamp", "Sud-Nonce", "Sud-Signature" })
        {
            if (request.Headers[name].Count != 1)
            {
                return false;
            }
        }
        // Pass the ORIGINAL received bytes unchanged; the SDK owns all HMAC operations.
        return signer.VerifySignature(
            request.Headers["Sud-AppId"].ToString(),
            request.Headers["Sud-Timestamp"].ToString(),
            request.Headers["Sud-Nonce"].ToString(),
            body,
            request.Headers["Sud-Signature"].ToString());
    }
}
