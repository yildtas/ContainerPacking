namespace Embroidery.Core.Diagnostics;

public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A finding reported by any stage (import, generation, quality, encoding).
/// <see cref="Code"/> is stable and documented so the UI and tests can rely on it.
/// </summary>
public sealed record Diagnostic(Severity Severity, string Code, string Message, Guid? ObjectId = null)
{
    public static Diagnostic Info(string code, string message, Guid? objectId = null) => new(Severity.Info, code, message, objectId);
    public static Diagnostic Warning(string code, string message, Guid? objectId = null) => new(Severity.Warning, code, message, objectId);
    public static Diagnostic Error(string code, string message, Guid? objectId = null) => new(Severity.Error, code, message, objectId);
}

public sealed record GenerationResult<T>(T Value, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
}
