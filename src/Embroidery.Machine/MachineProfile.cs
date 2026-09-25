using Embroidery.Core.Model;

namespace Embroidery.Machine;

/// <summary>How a trim intent is realised for machines whose format has no trim command.</summary>
public sealed record TrimPolicy
{
    /// <summary>Number of consecutive jumps the machine interprets as a trim (Tajima convention: 3).</summary>
    public int JumpCount { get; init; } = 3;

    /// <summary>Jump size in machine units for the zero-sum trim jumps.</summary>
    public int JumpSize { get; init; } = 2;

    /// <summary>The format has a trim command: emit <see cref="EncodedCommand.Trim"/> instead of jumps.</summary>
    public bool Native { get; init; }
}

/// <summary>Physical and format limits of a target machine.</summary>
public sealed record MachineProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Machine units per millimetre (DST: 10, i.e. 0.1 mm).</summary>
    public int UnitsPerMm { get; init; } = 10;

    /// <summary>Largest per-axis move in one record, in machine units (DST: 121).</summary>
    public int MaxRecordDelta { get; init; } = 121;

    /// <summary>Longest stitch the machine sews in one penetration.</summary>
    public double MaxStitchMm { get; init; } = 12.1;

    public TrimPolicy Trim { get; init; } = new();

    /// <summary>Length of each tie (lock) stitch.</summary>
    public double TieStitchMm { get; init; } = 0.7;

    /// <summary>Overrides the design's connection policy when set.</summary>
    public ConnectionPolicy? Connections { get; init; }

    public static readonly MachineProfile GenericDst = new() { Id = "generic-dst", Name = "Generic Tajima DST" };

    /// <summary>Brother/Babylock PES (PEC stitch block, 0.1 mm).</summary>
    public static readonly MachineProfile BrotherPes = new() { Id = "brother-pes", Name = "Brother PES", Trim = new TrimPolicy { Native = true } };

    /// <summary>Janome JEF (0.1 mm, ±127 per record).</summary>
    public static readonly MachineProfile JanomeJef = new() { Id = "janome-jef", Name = "Janome JEF", Trim = new TrimPolicy { Native = true } };

    /// <summary>Melco/Bernina EXP (0.1 mm, ±127 per record).</summary>
    public static readonly MachineProfile MelcoExp = new() { Id = "melco-exp", Name = "Melco EXP", Trim = new TrimPolicy { Native = true } };

    public static IReadOnlyList<MachineProfile> BuiltIn { get; } = [GenericDst, BrotherPes, JanomeJef, MelcoExp];

    public static MachineProfile Find(string id) => BuiltIn.FirstOrDefault(p => p.Id == id) ?? GenericDst;
}
