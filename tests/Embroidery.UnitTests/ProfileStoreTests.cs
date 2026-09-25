using Embroidery.Application.Projects;
using Embroidery.Core.Model;
using Embroidery.Core.Objects;

namespace Embroidery.UnitTests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "embroidery-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static StitchProfile Calibrated => StitchProfile.Standard with
    {
        Id = "kirmizi-krep", Name = "Kırmızı krep + parlak sarı", Description = "Kalibrasyon A1.2, B2, D2",
        SatinSpacingMm = 0.33, SatinPullMm = 0.25, TatamiRowSpacingMm = 0.38,
    };

    [Fact]
    public void Saved_profiles_survive_a_restart()
    {
        new StitchProfileStore(_dir).Save(Calibrated);
        var reloaded = new StitchProfileStore(_dir);
        Assert.Equal(Calibrated, reloaded.Find("kirmizi-krep"));
        Assert.Equal(StitchProfile.BuiltIn.Count + 1, reloaded.All.Count);
    }

    [Fact]
    public void Invalid_or_built_in_profiles_are_rejected()
    {
        var store = new StitchProfileStore(_dir);
        Assert.Throws<DesignValidationException>(() => store.Save(Calibrated with { Id = "Bad Id" }));
        Assert.Throws<DesignValidationException>(() => store.Save(Calibrated with { SatinSpacingMm = 0.05 }));
        Assert.Throws<DesignValidationException>(() => store.Save(Calibrated with { Id = "standard" }));
    }

    [Fact]
    public void Broken_files_are_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        Assert.Equal(StitchProfile.BuiltIn.Count, new StitchProfileStore(_dir).All.Count);
    }

    [Fact]
    public void Project_service_applies_a_custom_profile()
    {
        var store = new StitchProfileStore(_dir);
        store.Save(Calibrated);
        var service = new ProjectService(profiles: store);
        var design = service.ImportSvg("t.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" width="50mm" height="20mm" viewBox="0 0 50 20">
              <path d="M5,10 L45,10" stroke="#d4a53c" stroke-width="4" fill="none"/>
            </svg>
            """, stitchProfileId: "kirmizi-krep").Design;
        Assert.Equal(0.33, design.Objects.OfType<SatinObject>().Single().Parameters.SpacingMm);
        Assert.Equal("kirmizi-krep", design.StitchProfileId);
    }
}
