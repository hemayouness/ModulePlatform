using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ModulePlatform.Api.Auth;

/// <summary>
/// Development-only authentication that stamps a fixed administrator identity.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the proof of concept demonstrates the full authorisation flow
/// (policies, claims, <c>InstalledBy</c> auditing) without requiring an identity
/// provider. It is NOT a security control.
/// </para>
/// <para>
/// In production replace it with a real scheme — for example
/// <c>AddJwtBearer</c> against Entra ID / Keycloak / Auth0 — and keep the
/// <c>modules.manage</c> policy exactly as it is. The endpoints do not care
/// which scheme produced the claims.
/// </para>
/// <para>
/// The <c>modules.manage</c> permission should be granted to a small number of
/// platform operators, and every install should be recorded, because installing
/// a module is equivalent to deploying code into every user's browser session.
/// </para>
/// </remarks>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IWebHostEnvironment env)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!env.IsDevelopment())
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Development authentication is disabled outside the Development environment."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "u-1042"),
                new Claim(ClaimTypes.Name, "ifathy@example.local"),
                new Claim("permission", "modules.read"),
                new Claim("permission", "modules.manage"),
            ],
            SchemeName);

        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
