using System.Security.Cryptography;
using System.Text;

/// <summary>Optional request authentication before JSON binding or SDK calls.</summary>
public sealed class CallbackSignatureMiddleware
{
    private readonly RequestDelegate next;
    private readonly string appId;
    private readonly byte[] secret;
    private readonly bool enabled;

    public CallbackSignatureMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        this.next = next;
        appId = configuration["APP_ID"]!;
        secret = Encoding.UTF8.GetBytes(configuration["APP_SECRET"]!);
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
        var id = request.Headers["Sud-AppId"].ToString();
        var timestamp = request.Headers["Sud-Timestamp"].ToString();
        var nonce = request.Headers["Sud-Nonce"].ToString();
        var signature = request.Headers["Sud-Signature"].ToString();
        if (id != appId || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(nonce)
            || signature.Length != 40 || signature.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
        {
            return false;
        }

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, secret);
        hmac.AppendData(Encoding.UTF8.GetBytes($"{id}\n{timestamp}\n{nonce}\n"));
        hmac.AppendData(body);
        // Append the protocol newline without removing any newline already present in the body.
        hmac.AppendData(new byte[] { (byte)'\n' });
        return CryptographicOperations.FixedTimeEquals(hmac.GetHashAndReset(), Convert.FromHexString(signature));
    }
}
