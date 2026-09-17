using Microsoft.AspNetCore.Mvc;
using ModulePlatform.Core.Options;
using ModulePlatform.Core.Services;

namespace ModulePlatform.Api.Endpoints;

public static class ModuleEndpoints
{
    /// <summary>
    /// Authorisation policy names. Discovery is open to any signed-in user;
    /// everything that changes what code runs in browsers requires the
    /// administrative policy.
    /// </summary>
    public const string ReadPolicy = "modules.read";
    public const string ManagePolicy = "modules.manage";

    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/modules").WithTags("Modules");

        /* ---------------- Discovery (read) ---------------- */

        group.MapGet("/", async (IModuleManager manager, CancellationToken ct) =>
                Results.Ok(await manager.GetDiscoverableAsync(ct)))
            .RequireAuthorization(ReadPolicy)
            .WithName("ListModules")
            .WithSummary("Modules the Shell should offer: enabled, with an active version.")
            .Produces<IReadOnlyList<ModuleDescriptorDto>>();

        // Registered before "/{name}" so the literal segment wins the route match.
        group.MapGet("/admin", async (IModuleManager manager, CancellationToken ct) =>
                Results.Ok(await manager.GetAllForAdminAsync(ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("ListModulesForAdmin")
            .WithSummary("Every module and every installed version, including disabled ones.")
            .Produces<IReadOnlyList<AdminModuleDto>>();

        group.MapGet("/{name}", async (string name, IModuleManager manager, CancellationToken ct) =>
                ToHttp(await manager.GetDiscoverableByNameAsync(name, ct)))
            .RequireAuthorization(ReadPolicy)
            .WithName("GetModule")
            .Produces<ModuleDescriptorDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{name}/versions", async (string name, IModuleManager manager, CancellationToken ct) =>
                ToHttp(await manager.GetVersionsAsync(name, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("GetModuleVersions")
            .Produces<IReadOnlyList<AdminModuleVersionDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        /* ---------------- Lifecycle (manage) ---------------- */

        group.MapPost("/", InstallAsync)
            .RequireAuthorization(ManagePolicy)
            .DisableAntiforgery() // API clients authenticate with a bearer token, not a cookie.
            .WithName("InstallModule")
            .WithSummary("Upload and install a module package (multipart/form-data, field 'package').")
            .Produces<AdminModuleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{name}/{version}/activate",
                async (string name, string version, IModuleManager manager, CancellationToken ct) =>
                    ToHttp(await manager.ActivateAsync(name, version, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("ActivateModuleVersion")
            .WithSummary("Make this version the one served to browsers. Also the rollback operation.");

        group.MapPost("/{name}/{version}/deactivate",
                async (string name, string version, IModuleManager manager, CancellationToken ct) =>
                    ToHttp(await manager.DeactivateAsync(name, version, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("DeactivateModuleVersion");

        group.MapPost("/{name}/enable",
                async (string name, IModuleManager manager, CancellationToken ct) =>
                    ToHttp(await manager.SetEnabledAsync(name, true, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("EnableModule");

        group.MapPost("/{name}/disable",
                async (string name, IModuleManager manager, CancellationToken ct) =>
                    ToHttp(await manager.SetEnabledAsync(name, false, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("DisableModule");

        group.MapDelete("/{name}/{version}",
                async (string name, string version, IModuleManager manager, CancellationToken ct) =>
                    ToHttp(await manager.UninstallVersionAsync(name, version, ct)))
            .RequireAuthorization(ManagePolicy)
            .WithName("UninstallModuleVersion")
            .WithSummary("Permanently remove one version. Refuses to remove the active version.");

        return app;
    }

    private static async Task<IResult> InstallAsync(
        [FromForm(Name = "package")] IFormFile? package,
        IModuleManager manager,
        IHttpContextAccessor http,
        Microsoft.Extensions.Options.IOptions<ModulePlatformOptions> options,
        CancellationToken ct)
    {
        if (package is null || package.Length == 0)
        {
            return Results.Problem(
                title: "No package uploaded.",
                detail: "Send the ZIP as multipart/form-data in a field named 'package'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Cheap rejection before reading a single byte into memory.
        if (package.Length > options.Value.MaxPackageBytes)
        {
            return Results.Problem(
                title: "Package too large.",
                detail: $"The package is {package.Length:N0} bytes; the limit is {options.Value.MaxPackageBytes:N0}.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        await using var stream = package.OpenReadStream();
        var installedBy = http.HttpContext?.User.Identity?.Name;
        var result = await manager.InstallAsync(stream, installedBy, ct);

        return result.IsSuccess
            ? Results.Created($"/api/modules/{result.Value!.Name}", result.Value)
            : ToHttp(result);
    }

    /// <summary>
    /// Maps a domain result onto HTTP, with RFC 9457 ProblemDetails carrying the
    /// individual validation errors so the admin UI can show something useful.
    /// </summary>
    private static IResult ToHttp<T>(ModuleOperationResult<T> result) => result.Status switch
    {
        ModuleOperationStatus.Success => Results.Ok(result.Value),

        ModuleOperationStatus.NotFound => Results.Problem(
            title: "Not found",
            detail: result.Errors.FirstOrDefault()?.Message,
            statusCode: StatusCodes.Status404NotFound),

        ModuleOperationStatus.Conflict => Results.Problem(
            title: result.Errors.FirstOrDefault()?.Code ?? "Conflict",
            detail: result.Errors.FirstOrDefault()?.Message,
            statusCode: StatusCodes.Status409Conflict),

        ModuleOperationStatus.Invalid => Results.Problem(
            title: result.Title,
            detail: string.Join("\n", result.Errors.Select(e => $"[{e.Code}] {e.Message}")),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = result.Errors.Select(e => new { code = e.Code, message = e.Message }),
            }),

        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
