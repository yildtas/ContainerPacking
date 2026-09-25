using System.Globalization;
using Embroidery.Application.Projects;
using Embroidery.Core.Diagnostics;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;
using Embroidery.Geometry.Svg;
using Microsoft.AspNetCore.Mvc;

namespace Embroidery.Host;

public sealed record ImportSvgRequest(string FileName, string Svg, double? TargetWidthMm);
public sealed record ConvertRequest(StitchType Type);
public sealed record ReorderRequest(IReadOnlyList<Guid> Order);
public sealed record SettingsRequest(string? Name, Hoop? Hoop, ConnectionPolicy? Connections);

public sealed record ProjectState(Design Design, bool CanUndo, bool CanRedo, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Thin HTTP layer over <see cref="ProjectService"/>. Mutations take the client's revision in
/// If-Match and answer 409 when the design changed meanwhile.
/// </summary>
public static class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/session", (LocalSecurity security) => Results.Ok(new { token = security.Token }));

        var api = app.MapGroup("/api/projects");

        api.MapPost("/import/svg", (ImportSvgRequest request, ProjectService projects) =>
        {
            var result = projects.ImportSvg(request.FileName, request.Svg, new SvgImportOptions { TargetWidthMm = request.TargetWidthMm });
            return Results.Ok(State(projects, result.Design, result.Diagnostics));
        });

        api.MapPost("/open", async (HttpRequest request, ProjectService projects) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            buffer.Position = 0;
            var design = projects.Open(buffer);
            return Results.Ok(State(projects, design));
        });

        api.MapGet("/{id:guid}", (Guid id, ProjectService projects) => Results.Ok(State(projects, projects.Get(id))));

        api.MapDelete("/{id:guid}", (Guid id, ProjectService projects) =>
        {
            projects.Close(id);
            return Results.NoContent();
        });

        api.MapPut("/{id:guid}/objects/{objectId:guid}", (Guid id, Guid objectId, [FromBody] EmbroideryObject item, HttpRequest http, ProjectService projects) =>
        {
            if (item.Id != objectId) throw new DesignValidationException("Object id in the body does not match the URL.");
            return Results.Ok(State(projects, projects.UpdateObject(id, Revision(http), item)));
        });

        api.MapPost("/{id:guid}/objects/{objectId:guid}/convert", (Guid id, Guid objectId, ConvertRequest body, HttpRequest http, ProjectService projects) =>
            Results.Ok(State(projects, projects.ConvertObject(id, Revision(http), objectId, body.Type))));

        api.MapDelete("/{id:guid}/objects/{objectId:guid}", (Guid id, Guid objectId, HttpRequest http, ProjectService projects) =>
            Results.Ok(State(projects, projects.DeleteObject(id, Revision(http), objectId))));

        api.MapPut("/{id:guid}/order", (Guid id, ReorderRequest body, HttpRequest http, ProjectService projects) =>
            Results.Ok(State(projects, projects.Reorder(id, Revision(http), body.Order))));

        api.MapPut("/{id:guid}/threads", (Guid id, IReadOnlyList<EmbroideryThread> threads, HttpRequest http, ProjectService projects) =>
            Results.Ok(State(projects, projects.UpdateThreads(id, Revision(http), threads))));

        api.MapPut("/{id:guid}/settings", (Guid id, SettingsRequest body, HttpRequest http, ProjectService projects) =>
            Results.Ok(State(projects, projects.UpdateSettings(id, Revision(http), body.Hoop, body.Connections, body.Name))));

        api.MapPost("/{id:guid}/undo", (Guid id, ProjectService projects) => Results.Ok(State(projects, projects.Undo(id))));
        api.MapPost("/{id:guid}/redo", (Guid id, ProjectService projects) => Results.Ok(State(projects, projects.Redo(id))));

        api.MapGet("/{id:guid}/preview", (Guid id, ProjectService projects, CancellationToken ct) =>
            Results.Ok(projects.Preview(id, ct)));

        api.MapGet("/{id:guid}/export/dst", (Guid id, ProjectService projects, CancellationToken ct) =>
        {
            var result = projects.ExportDst(id, ct);
            return Results.File(result.Data, "application/octet-stream", result.FileName);
        });

        api.MapGet("/{id:guid}/export/embx", (Guid id, ProjectService projects) =>
        {
            var result = projects.ExportEmbx(id);
            return Results.File(result.Data, "application/zip", result.FileName);
        });
    }

    private static ProjectState State(ProjectService projects, Design design, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        new(design, projects.CanUndo(design.Id), projects.CanRedo(design.Id), diagnostics ?? []);

    /// <summary>Reads the expected revision from If-Match (quotes and W/ prefix tolerated).</summary>
    private static long? Revision(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw == "*") return null;
        raw = raw.Replace("W/", "").Trim('"', ' ');
        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var r)
            ? r
            : throw new DesignValidationException("If-Match must contain the design revision number.");
    }
}
