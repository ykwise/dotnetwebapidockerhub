using System.Security.Cryptography;
using System.Text;

// Load key=value pairs from a local .env file (when one exists) into the process
// environment BEFORE the host is built.
// In Docker / GitHub Actions the variables are injected directly with -e or
// with workflow env, so no .env file is needed there.
DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ---------------------------------------------------------------------------
// GET /  -> welcome message
// ---------------------------------------------------------------------------
app.MapGet("/", () => Results.Ok(new
{
    message = "Welcome to the Secret Verification API",
    endpoints = new[] { "GET /", "GET /verify?key=<secret>", "GET /health" }
}));

// ---------------------------------------------------------------------------
// GET /health -> liveness probe (bonus)
// ---------------------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    timestampUtc = DateTime.UtcNow.ToString("o")
}));

// ---------------------------------------------------------------------------
// GET /verify?key=... -> validates the key against API_SECRET_KEY
//   200 OK           -> key matches
//   400 Bad Request  -> key query parameter missing
//   401 Unauthorized -> key does not match
//   500 Internal     -> server has no secret configured
// ---------------------------------------------------------------------------
app.MapGet("/verify", (string? key) =>
{
    // The secret is NEVER hardcoded. It comes from the environment only.
    var expected = Environment.GetEnvironmentVariable("API_SECRET_KEY");

    if (string.IsNullOrWhiteSpace(expected))
    {
        return Results.Json(new
        {
            error = "Server misconfiguration",
            message = "API_SECRET_KEY is not configured on the server."
        }, statusCode: StatusCodes.Status500InternalServerError);
    }

    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.Json(new
        {
            error = "Bad request",
            message = "Query parameter 'key' is required. Example: /verify?key=my-secret-key"
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (!SecretComparer.Matches(key, expected))
    {
        return Results.Json(new
        {
            error = "Unauthorized",
            message = "Invalid secret key"
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new { message = "Authentication successful" });
});

app.Run();

/// <summary>Minimal .env reader, so no extra NuGet package is required.</summary>
internal static class DotEnv
{
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');

            // Real environment variables (Docker, CI) always win over the file.
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}

/// <summary>Length-safe, constant-time secret comparison.</summary>
internal static class SecretComparer
{
    public static bool Matches(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
}
