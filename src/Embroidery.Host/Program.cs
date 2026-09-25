using System.Net;
using System.Text.Json;
using Embroidery.Application.Projects;
using Embroidery.Application.Serialization;
using Embroidery.Formats.Dst;
using Embroidery.Geometry.Svg;
using Embroidery.Host;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var port = builder.Configuration.GetValue("Embroidery:Port", 5170);

// Loopback only: the local-first app must never be reachable from the network.
builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(IPAddress.Loopback, port);
    k.Limits.MaxRequestBodySize = 64L * 1024 * 1024;
});

builder.Services.Configure<JsonOptions>(o =>
{
    var shared = EmbroideryJson.Options;
    o.SerializerOptions.PropertyNamingPolicy = shared.PropertyNamingPolicy;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    foreach (var converter in shared.Converters) o.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<LocalSecurity>();
builder.Services.AddProblemDetails();

var app = builder.Build();
var security = app.Services.GetRequiredService<LocalSecurity>();

app.UseExceptionHandler(errors => errors.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        RevisionConflictException e => (StatusCodes.Status409Conflict, e.Message),
        ProjectNotFoundException e => (StatusCodes.Status404NotFound, e.Message),
        DesignValidationException e => (StatusCodes.Status400BadRequest, e.Message),
        EmbxFormatException e => (StatusCodes.Status400BadRequest, e.Message),
        DstFormatException e => (StatusCodes.Status400BadRequest, e.Message),
        SvgPathFormatException e => (StatusCodes.Status400BadRequest, e.Message),
        JsonException => (StatusCodes.Status400BadRequest, "Request body is not valid JSON for this endpoint."),
        BadHttpRequestException e => (e.StatusCode, e.Message),
        InvalidOperationException e => (StatusCodes.Status400BadRequest, e.Message),
        System.IO.InvalidDataException => (StatusCodes.Status400BadRequest, "File is not a valid project package."),
        OperationCanceledException => (499, "Request cancelled."),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error."),
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { title, status });
}));

app.Use(security.Middleware);
app.UseDefaultFiles();
app.UseStaticFiles();
ApiEndpoints.Map(app);

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation("Embroidery is running at http://localhost:{Port}", port));

app.Run();

public partial class Program;
