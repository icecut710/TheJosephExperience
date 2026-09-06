using JosephExperience.Models;

namespace JosephExperience.Services;

/// <summary>
/// Resolves a user's configured preset + intensity into concrete animation + text-FX settings.
/// Called once per trigger so the hotkey, manual button, tray and GSI all share one code path.
/// </summary>
public static class CelebrationResolver
{
    public record ResolvedResult(
        AnimationStyle ImageAnimation,
        TextFxStyle TextFx,
        FxIntensity Intensity,
        TextMode TextMode,
        int DurationMs,
        TextPosition TextPosition,
        TextAnimationStyle TextAnimation,
        double TextDelayMs,
        double ImageScale,
        double TextScale,
        string ResolvedQuote);

    public static ResolvedResult Resolve(AppSettings settings, string? imageQuote)
    {
        var (imageAnim, textFx, durationMul, intensityMod) = ResolvePreset(settings.Preset);
        var intensity = settings.FxIntensity;

        if (textFx == TextFxStyle.Random) textFx = PickRandomTextFx();

        // Apply user overrides: if they pinned a specific animation, respect it.
        if (settings.AnimationStyle is not AnimationStyle.Random)
        {
            imageAnim = settings.AnimationStyle;
        }

        var baseDuration = settings.OverlayDurationMs;
        var duration = (int)(baseDuration * durationMul);
        duration = intensity switch
        {
            FxIntensity.Subtle => (int)(duration * 0.7),
            FxIntensity.Strong => (int)(duration * 1.3),
            FxIntensity.Unhinged => (int)(duration * 1.6),
            _ => duration
        };
        duration = Math.Clamp(duration, 400, 6000);

        var textMode = settings.TextMode;
        var quote = ResolveQuote(settings, imageQuote, textMode);

        // Synchronized FX: text delay derives from image entry duration.
        var entryMs = settings.EntryDurationMs > 0
            ? settings.EntryDurationMs
            : (int)(Math.Max(80, duration / 5) * EntrySpeedMul(settings.EntrySpeed));
        var textDelay = settings.SynchronizeFx
            ? Math.Max(settings.TextDelayMs, entryMs * 0.5)
            : settings.TextDelayMs;

        var imgScale = intensity switch
        {
            FxIntensity.Subtle => Math.Min(settings.OverlayScale, 0.85),
            FxIntensity.Strong => settings.OverlayScale * 1.2,
            FxIntensity.Unhinged => settings.OverlayScale * 1.5,
            _ => settings.OverlayScale
        };
        imgScale = Math.Clamp(imgScale, 0.25, 3.0);

        var txtScale = intensity switch
        {
            FxIntensity.Subtle => 0.8,
            FxIntensity.Strong => 1.2,
            FxIntensity.Unhinged => 1.5,
            _ => 1.0
        };

        // Apply preset-derived intensity modifier.
        var effectiveIntensity = ApplyIntensityModifier(intensity, intensityMod);

        return new ResolvedResult(
            ImageAnimation: imageAnim,
            TextFx: textFx,
            Intensity: effectiveIntensity,
            TextMode: textMode,
            DurationMs: duration,
            TextPosition: settings.TextPosition,
            TextAnimation: settings.TextAnimation,
            TextDelayMs: textDelay,
            ImageScale: imgScale,
            TextScale: txtScale,
            ResolvedQuote: quote);
    }

    private static FxIntensity ApplyIntensityModifier(FxIntensity base_, double modifier)
    {
        if (modifier == 0) return base_;
        var vals = Enum.GetValues<FxIntensity>().Order().ToArray();
        var idx = Array.IndexOf(vals, base_);
        var newIdx = Math.Clamp(idx + (int)Math.Round(modifier), 0, vals.Length - 1);
        return vals[newIdx];
    }

    private static (AnimationStyle img, TextFxStyle txt, double dur, double intensityMod) ResolvePreset(CelebrationPreset preset) =>
        preset switch
        {
            CelebrationPreset.ClassicJoseph => (AnimationStyle.Pop, TextFxStyle.Fade, 1.0, 0),
            CelebrationPreset.ITWizard => (AnimationStyle.ScaleIn, TextFxStyle.Impact, 1.3, 0.5),
            CelebrationPreset.ShawarmaMode => (AnimationStyle.Bounce, TextFxStyle.Bounce, 1.4, 0.5),
            CelebrationPreset.CivicDeployment => (AnimationStyle.SlideRight, TextFxStyle.Stamp, 1.5, 0.5),
            CelebrationPreset.MassageChairRecovery => (AnimationStyle.Fade, TextFxStyle.Fade, 0.7, -1),
            CelebrationPreset.PrinterBossFight => (AnimationStyle.Shake, TextFxStyle.Impact, 2.0, 0.5),
            CelebrationPreset.MaximumNaddaf => (AnimationStyle.Jumpscare, TextFxStyle.Stamp, 2.2, 1),
            _ => (PickRandomImageAnim(), PickRandomTextFx(), 1.0, 0)
        };

    private static AnimationStyle PickRandomImageAnim()
    {
        var candidates = Enum.GetValues<AnimationStyle>()
            .Where(v => v is not AnimationStyle.Random and not AnimationStyle.None)
            .ToArray();
        return candidates[Random.Shared.Next(candidates.Length)];
    }

    private static TextFxStyle PickRandomTextFx()
    {
        var candidates = Enum.GetValues<TextFxStyle>()
            .Where(v => v is not TextFxStyle.None and not TextFxStyle.Random)
            .ToArray();
        return candidates[Random.Shared.Next(candidates.Length)];
    }

    private static string ResolveQuote(AppSettings settings, string? imageQuote, TextMode mode) => mode switch
    {
        TextMode.AssignedQuote => !string.IsNullOrWhiteSpace(imageQuote)
            ? imageQuote!
            : LorePools.RandomQuote(),
        TextMode.RandomQuote => LorePools.RandomQuote(),
        TextMode.CustomGlobal => string.IsNullOrWhiteSpace(settings.CelebrationText)
            ? "The Joseph Experience 2.0"
            : settings.CelebrationText,
        TextMode.NoText => string.Empty,
        _ => "The Joseph Experience 2.0"
    };

    private static double EntrySpeedMul(EntrySpeed speed) => speed switch
    {
        EntrySpeed.Slow => 1.6,
        EntrySpeed.Fast => 0.6,
        EntrySpeed.Instant => 0.1,
        _ => 1.0
    };
}
