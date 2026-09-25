using Embroidery.Formats.Dst;
using Embroidery.Formats.Home;
using Embroidery.Machine;

namespace Embroidery.Formats;

/// <summary>A machine file format: how to encode for it and how to write the file.</summary>
public sealed record StitchFileFormat(
    string Id,
    string Name,
    string Extension,
    MachineProfile Profile,
    Func<EncodedStitchPlan, IReadOnlyList<string>, byte[]> Write);

public static class StitchFormats
{
    public static readonly StitchFileFormat Dst = new("dst", "Tajima DST", ".dst", MachineProfile.GenericDst, (plan, _) => DstWriter.Write(plan));
    public static readonly StitchFileFormat Pes = new("pes", "Brother PES", ".pes", MachineProfile.BrotherPes, HomeFormats.WritePes);
    public static readonly StitchFileFormat Jef = new("jef", "Janome JEF", ".jef", MachineProfile.JanomeJef, (plan, colors) => HomeFormats.WriteJef(plan, colors));
    public static readonly StitchFileFormat Exp = new("exp", "Melco EXP", ".exp", MachineProfile.MelcoExp, (plan, _) => HomeFormats.WriteExp(plan));

    public static IReadOnlyList<StitchFileFormat> All { get; } = [Dst, Pes, Jef, Exp];

    public static StitchFileFormat? Find(string id) =>
        All.FirstOrDefault(f => f.Id.Equals(id.TrimStart('.'), StringComparison.OrdinalIgnoreCase));
}
