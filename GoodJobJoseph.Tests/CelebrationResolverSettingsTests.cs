using JosephExperience.Models;
using JosephExperience.Services;

namespace JosephExperience.Tests;

public class CelebrationResolverSettingsTests
{
    [Fact]
    public void Resolve_AppliesUserImageAndTextScale()
    {
        var settings = new AppSettings
        {
            Preset = CelebrationPreset.ClassicJoseph,
            FxIntensity = FxIntensity.Normal,
            OverlayScale = 1.35,
            TextScale = 0.75,
            AnimationStyle = AnimationStyle.Fade
        };

        var resolved = CelebrationResolver.Resolve(settings, "Joseph");

        Assert.Equal(1.35, resolved.ImageScale, 2);
        Assert.Equal(0.75, resolved.TextScale, 2);
    }

    [Fact]
    public void Resolve_StrongIntensityScalesBothChannels()
    {
        var settings = new AppSettings
        {
            Preset = CelebrationPreset.ClassicJoseph,
            FxIntensity = FxIntensity.Strong,
            OverlayScale = 1.0,
            TextScale = 1.0,
            AnimationStyle = AnimationStyle.Pop
        };

        var resolved = CelebrationResolver.Resolve(settings, "Joseph");

        Assert.Equal(1.2, resolved.ImageScale, 2);
        Assert.Equal(1.2, resolved.TextScale, 2);
    }

    [Fact]
    public void Resolve_PreservesTextAnimationAndConfiguredDelay()
    {
        var settings = new AppSettings
        {
            Preset = CelebrationPreset.ClassicJoseph,
            TextAnimation = TextAnimationStyle.Static,
            TextDelayMs = 175,
            SynchronizeFx = false,
            AnimationStyle = AnimationStyle.Fade
        };

        var resolved = CelebrationResolver.Resolve(settings, "Joseph");

        Assert.Equal(TextAnimationStyle.Static, resolved.TextAnimation);
        Assert.Equal(175, resolved.TextDelayMs);
    }
}
