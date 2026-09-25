using System.Text.Json;
using System.Text.RegularExpressions;
using Embroidery.Application.Serialization;
using Embroidery.Core.Model;

namespace Embroidery.Application.Projects;

/// <summary>
/// Built-in stitch profiles plus user profiles stored as JSON files (one per profile) in a
/// directory. This is how calibration results become production settings without code
/// changes: sew the calibration sheet, pick the best variants, save them as a profile.
/// </summary>
public sealed partial class StitchProfileStore
{
    private readonly string? _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, StitchProfile> _custom = new(StringComparer.Ordinal);

    /// <param name="directory">Folder for user profiles; null keeps only built-ins (tests, CLI without --profiles).</param>
    public StitchProfileStore(string? directory = null)
    {
        _directory = directory;
        if (directory is null || !Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<StitchProfile>(File.ReadAllText(file), EmbroideryJson.Options);
                if (profile is not null && Validate(profile) is null && StitchProfile.Find(profile.Id) is null)
                {
                    _custom[profile.Id] = profile;
                }
            }
            catch (JsonException)
            {
                // A broken file must not stop the application; it is simply not offered.
            }
        }
    }

    public IReadOnlyList<StitchProfile> All
    {
        get
        {
            lock (_gate) return StitchProfile.BuiltIn.Concat(_custom.Values.OrderBy(p => p.Name)).ToList();
        }
    }

    public StitchProfile? Find(string id)
    {
        lock (_gate) return StitchProfile.Find(id) ?? _custom.GetValueOrDefault(id);
    }

    /// <summary>Adds or replaces a user profile and writes it to the directory (if configured).</summary>
    public StitchProfile Save(StitchProfile profile)
    {
        if (Validate(profile) is { } error) throw new DesignValidationException(error);
        if (StitchProfile.Find(profile.Id) is not null)
        {
            throw new DesignValidationException($"'{profile.Id}' is a built-in profile and cannot be replaced.");
        }

        lock (_gate)
        {
            _custom[profile.Id] = profile;
            if (_directory is not null)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(Path.Combine(_directory, profile.Id + ".json"), JsonSerializer.Serialize(profile, EmbroideryJson.Indented));
            }
        }

        return profile;
    }

    /// <summary>Returns an error message, or null when the profile is usable.</summary>
    public static string? Validate(StitchProfile p)
    {
        if (!IdRegex().IsMatch(p.Id)) return "Profile id must be 2-40 characters: lower-case letters, digits and '-'.";
        if (string.IsNullOrWhiteSpace(p.Name)) return "Profile name is required.";
        static bool In(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
        if (!In(p.SatinSpacingMm, 0.15, 5)) return "Satin spacing must be 0.15-5 mm.";
        if (!In(p.SatinPullMm, -1, 3)) return "Satin pull compensation must be -1..3 mm.";
        if (!In(p.TatamiRowSpacingMm, 0.15, 5)) return "Tatami row spacing must be 0.15-5 mm.";
        if (!In(p.TatamiStitchLengthMm, 0.5, 12)) return "Tatami stitch length must be 0.5-12 mm.";
        if (!In(p.TatamiPullMm, -1, 3)) return "Tatami pull compensation must be -1..3 mm.";
        if (!In(p.RunStitchLengthMm, 0.5, 12)) return "Run stitch length must be 0.5-12 mm.";
        if (!In(p.RopeSpacingMm, 0.15, 5)) return "Rope spacing must be 0.15-5 mm.";
        return null;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,39}$")]
    private static partial Regex IdRegex();
}
