using System.Net;
using Embroidery.Application.Backends;
using Embroidery.Application.Projects;

namespace Embroidery.UnitTests;

public class BackendTests
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="40mm" height="20mm" viewBox="0 0 40 20">
          <path d="M5,10 L35,10" stroke="#d4a53c" stroke-width="3" fill="none"/>
        </svg>
        """;

    private static byte[] NativeDst()
    {
        var service = new ProjectService();
        return service.ExportDst(service.ImportSvg("a.svg", Svg).Design.Id).Data;
    }

    [Fact]
    public async Task Native_backend_digitizes()
    {
        var result = await new NativeBackend().DigitizeAsync("a.svg", Svg);
        Assert.True(result.Succeeded, result.Message);
        Assert.NotEmpty(result.Dst!);
    }

    [Fact]
    public async Task Unconfigured_backends_say_what_is_missing()
    {
        var ink = new CommandBackend("inkstitch", "Ink/Stitch", null);
        var ewa = new WilcomEwaBackend(new HttpClient(), new WilcomEwaSettings());
        Assert.False(ink.IsConfigured);
        Assert.False(ewa.IsConfigured);
        var r = await ewa.DigitizeAsync("a.svg", Svg);
        Assert.False(r.Succeeded);
        Assert.Contains("appId", r.Message);
    }

    [Fact]
    public async Task Command_backend_runs_a_process_and_reads_its_output()
    {
        var dst = Path.GetTempFileName();
        await File.WriteAllBytesAsync(dst, NativeDst());
        try
        {
            var backend = new CommandBackend("fake", "Fake", $"cp \"{dst}\" {{output}}");
            var rows = await DigitizerBackends.CompareAsync([new NativeBackend(), backend], "a.svg", Svg);
            Assert.All(rows, r => Assert.True(r.Result.Succeeded, r.Result.Message));
            Assert.Equal(rows[0].Metrics!.Stitches, rows[1].Metrics!.Stitches);

            var failing = await new CommandBackend("bad", "Bad", "false").DigitizeAsync("a.svg", Svg);
            Assert.False(failing.Succeeded);
        }
        finally
        {
            File.Delete(dst);
        }
    }

    [Fact]
    public async Task Ewa_backend_posts_credentials_and_reads_a_base64_file()
    {
        var dst = NativeDst();
        var handler = new StubHandler($"<response><files><file name=\"out.dst\">{Convert.ToBase64String(dst)}</file></files></response>");
        var pdf = Path.GetTempFileName();
        try
        {
            var settings = new WilcomEwaSettings
            {
                AppId = "id", AppKey = "key",
                RequestTemplate = "<request><vector name=\"{file_name}\">{file_base64}</vector><output format=\"{format}\"/></request>",
                SvgToPdfCommand = "cp {input} {output}", // stands in for Inkscape
            };
            var result = await new WilcomEwaBackend(new HttpClient(handler), settings).DigitizeAsync("logo.svg", Svg);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(dst, result.Dst);
            Assert.EndsWith("/vectorArtDesign", handler.LastUri!.AbsolutePath);
            Assert.Contains("appId=id", handler.LastBody);
            Assert.Contains("logo.pdf", Uri.UnescapeDataString(handler.LastBody!.Replace('+', ' ')));

            handler.Status = HttpStatusCode.BadRequest;
            handler.Body = "<error><error_info>quota exceeded</error_info></error>";
            var failed = await new WilcomEwaBackend(new HttpClient(handler), settings).DigitizeAsync("logo.svg", Svg);
            Assert.False(failed.Succeeded);
            Assert.Contains("quota exceeded", failed.Message);
        }
        finally
        {
            File.Delete(pdf);
        }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
