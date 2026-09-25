using System.Security.Cryptography;
using System.Text;

namespace Embroidery.Host;

/// <summary>
/// Protections for a web UI served from localhost:
/// <list type="bullet">
/// <item>Host header must be a loopback name, which defeats DNS rebinding.</item>
/// <item>State-changing API calls need a per-process token (CSRF). The token is only
/// readable same-origin: there is no CORS, so other sites cannot read it.</item>
/// </list>
/// </summary>
public sealed class LocalSecurity
{
    public const string TokenHeader = "X-Embroidery-Token";

    public string Token { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool IsLoopbackHost(HostString host) =>
        host.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1";

    public bool IsValidToken(string? value) =>
        value is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(Token));

    public RequestDelegate Middleware(RequestDelegate next) => async context =>
    {
        if (!IsLoopbackHost(context.Request.Host))
        {
            context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            await context.Response.WriteAsync("This application only answers on localhost.");
            return;
        }

        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/session"))
        {
            if (!IsValidToken(context.Request.Headers[TokenHeader]))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { title = "Missing or invalid session token." });
                return;
            }
        }
        else if (path.StartsWithSegments("/api/session") && context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    };
}
