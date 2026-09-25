namespace Embroidery.Core.Model;

/// <summary>A thread in the design palette. <see cref="ColorHex"/> is "#RRGGBB".</summary>
public sealed record EmbroideryThread(string Name, string ColorHex, string? Brand = null, string? Code = null);

/// <summary>Physical hoop the design must fit into, in millimetres.</summary>
public sealed record Hoop(string Name, double WidthMm, double HeightMm)
{
    public static readonly Hoop Default = new("130x180", 130, 180);
}

/// <summary>Common hoop and frame sizes. Large frames are for industrial single/multi-head machines.</summary>
public static class HoopPresets
{
    public static IReadOnlyList<Hoop> All { get; } =
    [
        new("100x100", 100, 100),
        new("130x180", 130, 180),
        new("200x200", 200, 200),
        new("360x200", 360, 200),
        new("300x500 büyük çerçeve", 300, 500),
        new("400x600 büyük çerçeve", 400, 600),
    ];
}
