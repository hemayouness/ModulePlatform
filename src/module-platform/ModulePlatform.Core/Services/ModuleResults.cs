namespace ModulePlatform.Core.Services;

/// <summary>
/// Outcome of a module-management operation, expressed so the API layer can map
/// it to an HTTP status without catching exceptions for control flow.
/// </summary>
public enum ModuleOperationStatus
{
    Success,
    /// <summary>Package or request failed validation -> 400.</summary>
    Invalid,
    /// <summary>Module or version does not exist -> 404.</summary>
    NotFound,
    /// <summary>Would violate an invariant, e.g. reinstalling a version -> 409.</summary>
    Conflict,
}

public sealed record ModuleOperationError(string Code, string Message);

public sealed class ModuleOperationResult<T>
{
    public ModuleOperationStatus Status { get; private init; }
    public T? Value { get; private init; }
    public string Title { get; private init; } = "";
    public IReadOnlyList<ModuleOperationError> Errors { get; private init; } = [];

    public bool IsSuccess => Status == ModuleOperationStatus.Success;

    public static ModuleOperationResult<T> Ok(T value) =>
        new() { Status = ModuleOperationStatus.Success, Value = value };

    public static ModuleOperationResult<T> Invalid(string title, IEnumerable<ModuleOperationError> errors) =>
        new() { Status = ModuleOperationStatus.Invalid, Title = title, Errors = errors.ToList() };

    public static ModuleOperationResult<T> Invalid(string title, string code, string message) =>
        Invalid(title, [new ModuleOperationError(code, message)]);

    public static ModuleOperationResult<T> NotFound(string message) =>
        new() { Status = ModuleOperationStatus.NotFound, Title = "Not found",
                Errors = [new ModuleOperationError("not_found", message)] };

    public static ModuleOperationResult<T> Conflict(string code, string message) =>
        new() { Status = ModuleOperationStatus.Conflict, Title = "Conflict",
                Errors = [new ModuleOperationError(code, message)] };
}
