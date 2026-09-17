using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using ModulePlatform.Api.Auth;
using ModulePlatform.Api.Endpoints;
using ModulePlatform.Core.Abstractions;
using ModulePlatform.Core.Options;
using ModulePlatform.Core.Packaging;
using ModulePlatform.Core.Services;
using ModulePlatform.Infrastructure.Data;
using ModulePlatform.Infrastructure.Services;
using ModulePlatform.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

/* ------------------------------------------------------------------ */
/* Options                                                             */
/* ------------------------------------------------------------------ */

builder.Services
    .AddOptions<ModulePlatformOptions>()
    .Bind(builder.Configuration.GetSection(ModulePlatformOptions.SectionName))
    .ValidateOnStart();

var platformOptions = builder.Configuration
    .GetSection(ModulePlatformOptions.SectionName)
    .Get<ModulePlatformOptions>() ?? new ModulePlatformOptions();

// Cap multipart uploads at the same limit the package reader enforces, so an
// oversized upload is rejected by Kestrel instead of being buffered first.
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = platformOptions.MaxPackageBytes;
});

/* ------------------------------------------------------------------ */
/* Persistence                                                         */
/* ------------------------------------------------------------------ */

builder.Services.AddDbContext<ModulePlatformDbContext>(o =>
    o.UseSqlServer(
        builder.Configuration.GetConnectionString("ModulePlatform")
        ?? throw new InvalidOperationException(
            "Connection string 'ModulePlatform' is not configured."),
        sql => sql.EnableRetryOnFailure()));

/* ------------------------------------------------------------------ */
/* Platform services                                                   */
/* ------------------------------------------------------------------ */

builder.Services.AddSingleton<IModulePackageReader, ModulePackageReader>();

// Swap this single registration for a blob/S3/MinIO implementation and nothing
// in ModuleManager changes.
builder.Services.AddSingleton<IModuleStorage>(sp => new FileSystemModuleStorage(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ModulePlatformOptions>>(),
    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<ILogger<FileSystemModuleStorage>>()));

builder.Services.AddScoped<IModuleManager, ModuleManager>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

/* ------------------------------------------------------------------ */
/* AuthN / AuthZ                                                       */
/* ------------------------------------------------------------------ */
/*
 * Module installation executes attacker-controlled JavaScript in every user's
 * browser, inside this origin. It must therefore be an authenticated,
 * authorised and audited operation -- never anonymous.
 *
 * For the proof of concept a development authentication handler stamps a fixed
 * identity so the flow can be demonstrated without an identity provider. It
 * refuses to run outside Development, so the platform cannot be deployed
 * accidentally with authentication disabled.
 */

builder.Services
    .AddAuthentication(DevAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(
        DevAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ModuleEndpoints.ReadPolicy, p => p.RequireAuthenticatedUser())
    .AddPolicy(ModuleEndpoints.ManagePolicy, p => p
        .RequireAuthenticatedUser()
        .RequireClaim("permission", "modules.manage"))
    // Any authenticated user may read/write module business data. Per-module
    // fine-grained authorization (e.g. only users with "requests.write" may
    // post to the "requests" collection) is not enforced server-side yet —
    // today it is only checked client-side via the module's own UI. Add a
    // real check here before treating this as safe against a hostile caller.
    .AddPolicy(ModuleDataEndpoints.DataPolicy, p => p.RequireAuthenticatedUser());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Fail fast rather than silently serving a privileged API with a stub identity.
    // Remove this guard at the same time you register a real authentication scheme.
    app.Logger.LogCritical(
        "Development authentication is the only configured scheme, but the environment is {Env}. " +
        "Register a real OIDC/JWT scheme before deploying.", app.Environment.EnvironmentName);

    throw new InvalidOperationException(
        "Refusing to start outside Development while development authentication is the only scheme. " +
        "See Auth/DevAuthenticationHandler.cs.");
}

/* ------------------------------------------------------------------ */
/* Pipeline                                                            */
/* ------------------------------------------------------------------ */

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

/*
 * Security headers.
 *
 * Note what CSP can and cannot do here: because remotes are served from this
 * same origin, 'self' is enough for script-src and no per-module allowance is
 * needed. But that also means CSP gives NO protection between the Shell and a
 * module -- they share one origin and one JavaScript realm. CSP here limits
 * what any of that code can reach OUTWARD, not what a module can do to the Shell.
 */
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "same-origin";
    h["X-Frame-Options"] = "DENY";
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        /*
         * Native Federation installs import maps through es-module-shims, which
         * needs BOTH of these and it is not optional:
         *   'unsafe-inline' — the injected <script type="importmap-shim"> and the
         *                     shim's own bootstrap are inline.
         *   blob:           — the shim rewrites each module's import specifiers
         *                     and executes the result from a Blob URL. Without
         *                     blob: every federated module fails to load with
         *                     "Loading the script 'blob:...' violates ... CSP".
         * Tightening this further means dropping the shim (native import maps
         * only, no dynamic re-registration), which would cost runtime discovery.
         */
        "script-src 'self' 'unsafe-inline' blob:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        // Modules may only call back to this origin.
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'";

    /*
     * The SPA document must never be cached, whatever serves it. Static files
     * get this from OnPrepareResponse, but the SPA fallback (deep links such as
     * /m/requests/REQ-1001) and the default-files rewrite for "/" bypass that
     * hook, so the rule is applied here by content type instead. A cached
     * index.html pins a browser to a previous Shell deployment and produces
     * 404s for bundle hashes that no longer exist.
     */
    ctx.Response.OnStarting(() =>
    {
        if (ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }

        return Task.CompletedTask;
    });

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

/* ---------- Module artifacts: /modules/{name}/{version}/... ---------- */
/*
 * This is what makes the whole design work: ONE ASP.NET Core application serves
 * every remote's federation artifacts as ordinary static files. There is no IIS
 * site, virtual directory or app pool per module.
 *
 * Native Federation derives a remote's base URL from the directory of its
 * remoteEntry.json, so an artifact served at
 *   /modules/requests/1.0.0/remoteEntry.json
 * resolves all of its chunks under /modules/requests/1.0.0/ automatically --
 * with no publicPath, deployUrl or base-href configuration in the remote build.
 *
 * Shell and modules share this origin, so there are NO CORS requirements at all.
 */
var storage = (FileSystemModuleStorage)app.Services.GetRequiredService<IModuleStorage>();
Directory.CreateDirectory(storage.RootPath);

var contentTypes = new FileExtensionContentTypeProvider();
// Be explicit about the types federation depends on rather than trusting defaults.
contentTypes.Mappings[".js"] = "text/javascript";
contentTypes.Mappings[".mjs"] = "text/javascript";
contentTypes.Mappings[".json"] = "application/json";
contentTypes.Mappings[".map"] = "application/json";
contentTypes.Mappings[".wasm"] = "application/wasm";
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

app.UseStaticFiles(new StaticFileOptions
{
    // PhysicalFileProvider defaults to ExclusionFilters.Sensitive, so hidden
    // directories (such as the storage staging area) are never served.
    FileProvider = new PhysicalFileProvider(storage.RootPath),
    RequestPath = "/modules",
    ContentTypeProvider = contentTypes,
    // Never guess: an artifact whose type we do not recognise is not served.
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        /*
         * Versioned URLs are immutable by construction: a (module, version) pair
         * can only be installed once, and the storage layer refuses to overwrite
         * a committed prefix. So these bytes can be cached essentially forever.
         *
         * This is precisely why the platform never exposes a mutable
         * /modules/{name}/current/ alias -- that would make a cached URL point
         * at different bytes over time, which is a cache-poisoning hazard and
         * would break subresource integrity for anyone holding a stale copy.
         * Version switching happens in the registry (GET /api/modules returns a
         * different entryUrl), not by rewriting files behind a fixed URL.
         */
        ctx.Context.Response.Headers.CacheControl =
            $"public, max-age={platformOptions.ArtifactCacheSeconds}, immutable";

        // Defence in depth: artifacts are attacker-supplied content served from
        // the Shell's own origin, so make absolutely sure nothing is sniffed
        // into a different type and nothing renders as a top-level document.
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers["X-Frame-Options"] = "DENY";
    },
});

/* ---------- API ---------- */

app.MapModuleEndpoints();
app.MapModuleDataEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

/* ---------- Angular Shell ---------- */
/*
 * The Shell is served by this same application as plain static files, with a
 * SPA fallback so deep links such as /m/requests/REQ-1001 reach index.html.
 * The fallback deliberately does not apply to /api or /modules.
 */
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        /*
         * The Shell's own bundles are content-hashed, so they cache like the
         * module artifacts. index.html is NOT hashed: it is the document that
         * names the current bundle hashes, so caching it would pin browsers to
         * a previous Shell deployment and produce 404s for chunks that no
         * longer exist. It must always be revalidated.
         *
         * The same applies to the Shell's own remoteEntry.json: unlike a
         * module's, it lives at a path that is reused by every deployment and
         * it names the current shared-dependency bundles.
         */
        var name = ctx.File.Name;
        var mustRevalidate =
            name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("remoteEntry.json", StringComparison.OrdinalIgnoreCase);

        ctx.Context.Response.Headers.CacheControl = mustRevalidate
            ? "no-cache, no-store, must-revalidate"
            : $"public, max-age={platformOptions.ArtifactCacheSeconds}, immutable";
    },
});

app.MapFallbackToFile("index.html").AllowAnonymous();

/* ---------- Startup ---------- */

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ModulePlatformDbContext>();
    // Migrate on start for the PoC. In production run `dotnet ef database update`
    // as a deployment step so app instances never race each other.
    await db.Database.MigrateAsync();
}

app.Run();

/// <summary>Exposed so the integration tests can spin up this exact pipeline.</summary>
public partial class Program;
