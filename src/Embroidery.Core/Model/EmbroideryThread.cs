namespace Embroidery.Core.Model;

/// <summary>A thread in the design palette. <see cref="ColorHex"/> is "#RRGGBB".</summary>
public sealed record EmbroideryThread(string Name, string ColorHex, string? Brand = null, string? Code = null);

/// <summary>Physical hoop the design must fit into, in millimetres.</summary>
public sealed record Hoop(string Name, double WidthMm, double HeightMm)
{
    public static readonly Hoop Default = new("130x180", 130, 180);
}
