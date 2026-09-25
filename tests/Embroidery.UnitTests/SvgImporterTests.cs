using Embroidery.Core.Diagnostics;
using Embroidery.Core.Primitives;
using Embroidery.Geometry.Svg;

namespace Embroidery.UnitTests;

public class SvgImporterTests
{
    [Fact]
    public void Physical_width_and_viewbox_define_millimetre_scale()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="50mm" viewBox="0 0 1000 500">
              <rect x="100" y="100" width="200" height="100" fill="#ff0000"/>
            </svg>
            """);

        Assert.Equal(0.1, art.ScaleMmPerUnit, 9);
        Assert.Equal(100, art.WidthMm, 9);
        var shape = Assert.Single(art.Shapes);
        Assert.Equal("#FF0000", shape.FillColor);
        var b = Bounds.Of(shape.Subpaths.SelectMany(s => s.Points));
        Assert.Equal(new Bounds(10, 10, 30, 20), b);
    }

    [Fact]
    public void Unitless_svg_assumes_96_dpi_and_reports_it()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96">
              <rect width="96" height="96"/>
            </svg>
            """);

        Assert.Equal(25.4, art.WidthMm, 6);
        Assert.Contains(art.Diagnostics, d => d.Code == "SVG005");
    }

    [Fact]
    public void Transforms_and_inline_styles_are_applied()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" width="100mm" height="100mm" viewBox="0 0 100 100">
              <g transform="translate(10,20)">
                <rect width="10" height="10" transform="scale(2)" style="fill:none;stroke:rgb(0,128,255);stroke-width:2"/>
              </g>
            </svg>
            """);

        var shape = Assert.Single(art.Shapes);
        Assert.Null(shape.FillColor);
        Assert.Equal("#0080FF", shape.StrokeColor);
        Assert.Equal(4, shape.StrokeWidthMm, 6); // 2 user units × scale(2)
        Assert.Equal(new Bounds(10, 20, 30, 40), Bounds.Of(shape.Subpaths[0].Points));
    }

    [Fact]
    public void Unsupported_elements_produce_warnings_not_silence()
    {
        var art = SvgImporter.Import("""
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="10mm" viewBox="0 0 10 10">
              <text x="0" y="5">Hello</text>
              <circle cx="5" cy="5" r="4"/>
            </svg>
            """);

        Assert.Single(art.Shapes);
        Assert.Contains(art.Diagnostics, d => d.Code == "SVG002" && d.Severity == Severity.Warning);
    }

    [Fact]
    public void Hidden_elements_are_skipped()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="10mm" viewBox="0 0 10 10">
              <rect width="5" height="5" display="none"/>
              <g style="visibility:hidden"><rect width="5" height="5"/></g>
            </svg>
            """);

        Assert.Empty(art.Shapes);
    }

    [Fact]
    public void Malformed_xml_is_an_error_diagnostic()
    {
        var art = SvgImporter.Import("<svg><rect></svg>");
        Assert.Contains(art.Diagnostics, d => d.Code == "SVG001" && d.Severity == Severity.Error);
    }

    [Fact]
    public void Target_width_rescales_everything()
    {
        var art = SvgImporter.Import("""
            <svg xmlns="http://www.w3.org/2000/svg" width="10mm" height="10mm" viewBox="0 0 10 10">
              <rect width="10" height="5" fill="black" stroke="red" stroke-width="1"/>
            </svg>
            """, new SvgImportOptions { TargetWidthMm = 50 });

        Assert.Equal(50, art.WidthMm, 6);
        var shape = Assert.Single(art.Shapes);
        Assert.Equal(5, shape.StrokeWidthMm, 6);
        Assert.Equal(50, Bounds.Of(shape.Subpaths[0].Points).Width, 6);
    }

    [Theory]
    [InlineData("25.4mm", 25.4)]
    [InlineData("1in", 25.4)]
    [InlineData("2cm", 20)]
    [InlineData("72pt", 25.4)]
    [InlineData("96px", 25.4)]
    [InlineData("96", 25.4)]
    public void Lengths_convert_to_millimetres(string value, double expected)
    {
        Assert.Equal(expected, SvgImporter.ParseLengthMm(value)!.Value, 6);
    }

    [Fact]
    public void Percentage_lengths_have_no_absolute_size()
    {
        Assert.Null(SvgImporter.ParseLengthMm("100%"));
    }
}
