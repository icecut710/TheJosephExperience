# UIOverhaul Wire-Up Guide (Agents)

## 1. NuGet Restore
Run after build agents have pulled packages:
```powershell
dotnet restore GoodJobJoseph/JosephExperience2.csproj
```
**packages.config now includes:**
- `MahApps.Metro` (2.7.0) — modern control templates only, used as style reference
- `Microsoft.Extensions.Hosting` (8.0.0)
- `System.Drawing.Common` (8.0.0)

## 2. App.xaml Merge
Add the modern theme to `Styles/UIOverhaul/Theme.Manifest.xaml` **last** so new tokens win:
```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceDictionary Source="Styles/Colors.xaml"/>
  <ResourceDictionary Source="Styles/Typography.xaml"/>
  <ResourceDictionary Source="Styles/Buttons.xaml"/>
  <ResourceDictionary Source="Styles/Inputs.xaml"/>
  <ResourceDictionary Source="Styles/Navigation.xaml"/>
  <ResourceDictionary Source="Styles/UIOverhaul/Theme.Manifest.xaml"/>  <!-- NEW -->
</ResourceDictionary.MergedDictionaries>
```

## 3. Apply Modern Button Style to Primary CTA Buttons
In `MainWindow.xaml`, apply `Style="{StaticResource ModernPrimaryButton}"` to main action buttons (Celebrate, Activate CS2, etc.) in `MainWindow.xaml.cs` page builders.

## 4. Verify
- Build: `dotnet build GoodJobJoseph/JosephExperience2.csproj`
- Launch: confirm sidebar nav pills render MDL2 icons crisply, toast still slides, chart renders gradient bars with peak glow
- Check no "resource not found" warnings in VS output

## Files Delivered This Round
1. `Styles/UIOverhaul/Colors.Modern.xaml` — soft-lavender accent, temperature-corrected dark stack, unified shadows
2. `Styles/UIOverhaul/Buttons.Modern.xaml` — hover-lift primary, circular icon button for pills
3. `Styles/UIOverhaul/Theme.Manifest.xaml` — merge dictionary
4. `Views/BarChartView.cs` — typography polish (font sizes bumped, semi-bold value labels)
5. `packages.config` — package declarations
6. This guide: `Styles/UIOverhaul/WIREGUIDE.md`
