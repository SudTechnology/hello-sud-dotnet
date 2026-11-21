# SudGIP Auth Server Demo

A lightweight .NET 8 HTTP server demo for SudGIP authentication services. This project implements authentication APIs with ASP.NET Core Minimal APIs and the **Sud.Gip.Auth 1.0.0** NuGet package.

Use this demo to learn how to initialize the SDK and build authentication endpoints in your own server.

## Prerequisites

- .NET 8 SDK for local development.
- Sud.Gip.Auth 1.0.0, automatically restored from NuGet.
- Alternatively, Docker with its engine running; a local .NET installation is not required for the Docker workflow.

## Installation

1. Clone the repository:

   ```bash
   git clone https://github.com/SudTechnology/hello-sud-dotnet.git
   cd hello-sud-dotnet
   ```

2. Restore dependencies for local development (skip when using Docker):

   ```bash
   dotnet restore HelloSud.sln
   ```

3. Configure your application using environment variables:

   | Variable | Description |
   |---|---|
   | `APP_ID` | Your application ID. |
   | `APP_SECRET` | Your application secret, at least 32 UTF-8 bytes. |
   | `ASPNETCORE_URLS` | Listening address, such as `http://127.0.0.1:8000`. |

The secret must match the one used by the verifying service. If an issued secret is too short, coordinate rotation with the issuer; do not pad or hash it locally. Do not commit real credentials or generated tokens.

For isolated local testing, use `openssl rand -hex 32` to generate a random test secret. This does not provision a Sud credential and cannot replace integration testing with issued credentials.

Open `HelloSud.sln` in Rider. For IDE launches, set the environment variables in the API project's run configuration. The application does **not** automatically load `.env` files.

## Running the Server

### Development Environment

From the project root, run the following in a macOS/Linux terminal:

```bash
export APP_ID=local-test-app
export APP_SECRET="$(openssl rand -hex 32)"
export ASPNETCORE_URLS=http://127.0.0.1:8000

dotnet run --project src/HelloSud.Api
```

These credentials are for isolated local testing. Keep the terminal running and press Ctrl+C to stop. If your SDK is installed at `~/.dotnet` and `dotnet` is not on PATH, use `~/.dotnet/dotnet` instead.

Check the server at `http://127.0.0.1:8000/health`, which returns `{"status":"ok"}`. There is no homepage at `/`; use the API paths below.

### Docker

Create a local configuration file:

```bash
cp .env.example .env
```

Edit `.env` and replace its example values with your application ID and secret, then run:

```bash
docker build -t hello-sud-dotnet:test .
docker run --rm --name hello-sud-dotnet-test \
  -p 127.0.0.1:8000:8000 \
  --env-file .env hello-sud-dotnet:test
```

The `.env` file is excluded from Git and the Docker build context. Docker supplies its values as environment variables at startup. After changing `.env`, stop and recreate the container. Code or dependency changes require rebuilding the image. Press Ctrl+C to stop.

## Implementing the Demo Server

The SDK handles signing, verification, and UID extraction. Your server owns
routing, request validation, user profiles, HTTP status codes, and the
`ret_code` response envelope. The complete implementation is in
`src/HelloSud.Api/Program.cs`.

### 1. Initialize and register the SDK

The demo reads application configuration, validates it, and registers a shared
SDK instance with dependency injection:

```csharp
using Sud.Gip.Api;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var appId = builder.Configuration["APP_ID"];
var secret = builder.Configuration["APP_SECRET"];
if (string.IsNullOrWhiteSpace(appId) || secret is null ||
    Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Configure APP_ID and APP_SECRET first.");

builder.Services.AddSingleton(new SudGIPAuth(appId, secret));
var app = builder.Build();
```

### 2. Generate a Code

A Minimal API handler receives the registered SDK instance:

```csharp
app.MapGet("/api/getCode", (string? uid, SudGIPAuth auth) =>
    string.IsNullOrEmpty(uid)
        ? Error("Missing uid parameter")
        : Success(new { code = auth.GetCode(uid).Code }));
```

`Success` and `Error` are the server's response helpers, shown below. The demo
accepts UID as a query parameter. In your own server, derive it from an
authenticated session or validated access token. The SDK does not authenticate
the caller or authorize access to a UID.

### 3. Exchange a Code for an SSToken

Inside the POST handler, after reading the `code` JSON field:

```csharp
var result = auth.GetUidByCode(code);
if (!result.IsSuccess) return Error("", result.ErrorCode);
if (string.IsNullOrEmpty(result.Uid)) return Error("Missing token uid", 1004);

var token = auth.GetSSToken(result.Uid);
return Success(new {
    ss_token = token.Token,
    expire_date = token.ExpireDate,
    user_info = new { uid = result.Uid }
});
```

The full demo adds fixed profile values. Replace those values with data from
your own user service or database. The SDK provides the verified UID, not a
nickname or avatar.

### 4. Refresh an SSToken

Inside the refresh handler, after reading the `ss_token` field:

```csharp
var result = auth.GetUidBySSToken(value);
if (!result.IsSuccess) return Error("", result.ErrorCode);
if (string.IsNullOrEmpty(result.Uid)) return Error("Missing token uid", 1004);

var token = auth.GetSSToken(result.Uid);
return Success(new {
    ss_token = token.Token,
    expire_date = token.ExpireDate
});
```

This validates the current token and signs a new one for the same UID. It does
not revoke the old token; an expired token cannot be refreshed through this path.

### 5. Wrap SDK results in HTTP responses

The response helpers are application code, not SDK methods:

```csharp
static IResult Success(object data) => Results.Json(new {
    ret_code = 0, ret_msg = "", sdk_error_code = 0, data
});

static IResult Error(string message, int code = 0) => Results.Json(new {
    ret_code = 1, ret_msg = message, sdk_error_code = code, data = new { }
}, statusCode: 400);
```

The complete `Program.cs` also reads JSON request bodies, handles malformed
requests, and calls `app.Run()`. The excerpts above explain the integration
points rather than replacing the full server implementation.

## API Reference

All POST requests require `Content-Type: application/json`. Successful authentication responses use HTTP 200; validation failures use HTTP 400. Expiration timestamps are Unix milliseconds.

### 1. getCode (GET)

**Endpoint:** `/api/getCode?uid={uid}`

**Parameters:** `uid` (required): User identifier.

**Response:**

```json
{
  "ret_code": 0,
  "ret_msg": "",
  "sdk_error_code": 0,
  "data": {
    "code": "generated_code_here"
  }
}
```

### 2. getSSToken (POST)

**Endpoint:** `/api/getSSToken`

**Request Body:**

```json
{
  "code": "code_from_getCode"
}
```

**Response:**

```json
{
  "ret_code": 0,
  "ret_msg": "",
  "sdk_error_code": 0,
  "data": {
    "ss_token": "generated_sstoken_here",
    "expire_date": 1795257193000,
    "user_info": {
      "uid": "test-user-001",
      "nick_name": "Demo User",
      "avatar_url": "https://example.com/avatar.png",
      "gender": "female",
      "is_ai": 0,
      "ai_level": 0
    }
  }
}
```

The UID comes from the verified token. Other user profile fields are demonstration values.

### 3. updateSSToken (POST)

**Endpoint:** `/api/updateSSToken`

**Request Body:**

```json
{
  "ss_token": "current_sstoken_here"
}
```

**Response:**

```json
{
  "ret_code": 0,
  "ret_msg": "",
  "sdk_error_code": 0,
  "data": {
    "ss_token": "updated_sstoken_here",
    "expire_date": 1795257312000
  }
}
```

## Error Handling

Authentication APIs use a consistent response format:

```json
{
  "ret_code": 1,
  "ret_msg": "",
  "sdk_error_code": 1002,
  "data": {}
}
```

| Field | Meaning |
|---|---|
| `ret_code` | 0 for success, 1 for failure. |
| `ret_msg` | Error description; may be empty for SDK verification failures. |
| `sdk_error_code` | SDK error code, or 0 when no SDK error applies. |
| `data` | Response payload, or an empty object on failure. |

Common SDK error codes:

- `1002`: Token verification failed.
- `1004`: Verified token is missing a usable UID in this demo.
- `1005`: Token has expired.

Missing parameters, invalid JSON, and unsupported request content types return HTTP 400. Unknown paths return HTTP 404. Unexpected exceptions return HTTP 500 without exposing exception details. Missing application configuration or a short secret prevents startup.

## Testing

With the server running, execute this from the project root in a second terminal:

```bash
dotnet run --project tests/SmokeTests -- http://127.0.0.1:8000
```

The test automatically passes tokens between requests and does not print them. It checks:

- Code generation.
- Code exchange and UID retrieval.
- SSToken refresh with a later expiration time.
- Invalid Code rejection.
- Invalid SSToken rejection.
- Missing UID rejection.
- Missing Code rejection.
- Malformed JSON rejection.

Expected output: eight `PASS` lines, followed by `All HTTP smoke tests passed. Tokens were not printed.` Failures exit with a nonzero status. These checks validate the local API flow, not remote-service integration.

If you run the server in Docker without a local SDK, run the tests in an SDK container:

```bash
docker run --rm --network container:hello-sud-dotnet-test \
  --mount "type=bind,source=$PWD,target=/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet run --project tests/SmokeTests -- http://127.0.0.1:8000
```

## Project Structure

```text
hello-sud-dotnet/
├── src/
│   └── HelloSud.Api/
│       ├── Program.cs                 # HTTP endpoints and configuration
│       └── HelloSud.Api.csproj        # NuGet SDK reference
├── tests/
│   └── SmokeTests/
│       ├── Program.cs                 # Automated HTTP tests
│       └── SmokeTests.csproj
├── .github/workflows/tests.yml       # Build and HTTP test workflow
├── .env.example                      # Configuration template
├── .dockerignore                     # Excludes secrets and build output
├── .gitignore
├── Dockerfile
├── HelloSud.sln
└── README.md
```

## Integration Notes

This is a local demonstration project, without user login, persistence, or production access controls. Add authentication, authorization, application-context checks, HTTPS, and operational configuration before adapting it for a deployed service.

Refreshing issues a new token for the same UID; it does not revoke the old token. The SDK uses a shared format for Code and SSToken and does not automatically compare the token's `app_id` with the configured application ID.

## Continuous Integration

GitHub Actions restores the NuGet package, builds the solution, starts the API with temporary test credentials, and runs the HTTP smoke tests.

## References

- SDK source: https://github.com/SudTechnology/sud-gip-auth-dotnet
- NuGet package: https://www.nuget.org/packages/Sud.Gip.Auth/1.0.0
