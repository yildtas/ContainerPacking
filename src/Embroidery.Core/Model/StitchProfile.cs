using Embroidery.Core.Objects;

namespace Embroidery.Core.Model;

/// <summary>
/// Starting parameter values for a thread/fabric combination. Applying a profile overwrites the
/// density-related parameters of objects; geometry and structural choices (underlay layers,
/// tapers, rungs) are kept. Values other than the measured FER-7 spacing are starting points
/// that the calibration sew-out must confirm.
/// </summary>
public sealed record StitchProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public double SatinSpacingMm { get; init; } = 0.40;
    public double SatinPullMm { get; init; } = 0.20;
    public double TatamiRowSpacingMm { get; init; } = 0.40;
    public double TatamiStitchLengthMm { get; init; } = 4.0;
    public double TatamiPullMm { get; init; } = 0.20;
    public double RunStitchLengthMm { get; init; } = 2.5;
    public double RopeSpacingMm { get; init; } = 0.35;

    public static readonly StitchProfile Standard = new()
    {
        Id = "standard",
        Name = "Standart",
        Description = "Genel amaçlı polyester/viskon iplik, orta ağırlıkta dokuma kumaş.",
    };

    public static readonly StitchProfile GlossySatin = new()
    {
        Id = "glossy-satin",
        Name = "Parlak saten (FER-7)",
        Description = "FER-7 referansından ölçülen sıklık (aynı kenar 0,30 mm): parlak tek renk saten süsleme, ince kumaş + stabilizer.",
        SatinSpacingMm = 0.30,
        SatinPullMm = 0.20,
        TatamiRowSpacingMm = 0.35,
        TatamiStitchLengthMm = 3.5,
        RopeSpacingMm = 0.30,
    };

    public static readonly StitchProfile Metallic = new()
    {
        Id = "metallic",
        Name = "Metalik iplik",
        Description = "Metalik iplik başlangıç değerleri: daha seyrek ve daha uzun dikiş, iplik kopmasını azaltmak için. Kalibrasyonla doğrulanmalı.",
        SatinSpacingMm = 0.45,
        SatinPullMm = 0.25,
        TatamiRowSpacingMm = 0.45,
        TatamiStitchLengthMm = 3.0,
        TatamiPullMm = 0.25,
        RunStitchLengthMm = 3.0,
        RopeSpacingMm = 0.40,
    };

    public static IReadOnlyList<StitchProfile> BuiltIn { get; } = [Standard, GlossySatin, Metallic];

    public static StitchProfile? Find(string? id) => BuiltIn.FirstOrDefault(p => p.Id == id);

    public EmbroideryObject Apply(EmbroideryObject item) => item switch
    {
        RunObject r => r with { Parameters = r.Parameters with { StitchLengthMm = RunStitchLengthMm } },
        SatinObject s => s with { Parameters = s.Parameters with { SpacingMm = SatinSpacingMm, PullCompensationMm = SatinPullMm } },
        TatamiObject t => t with
        {
            Parameters = t.Parameters with
            {
                RowSpacingMm = TatamiRowSpacingMm,
                StitchLengthMm = TatamiStitchLengthMm,
                PullCompensationMm = TatamiPullMm,
            },
        },
        RopeObject rope => rope with { Parameters = rope.Parameters with { SpacingMm = RopeSpacingMm } },
        _ => item,
    };
}
