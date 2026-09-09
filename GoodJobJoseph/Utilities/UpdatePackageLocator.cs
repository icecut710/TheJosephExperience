using System.IO;

namespace JosephExperience.Utilities;

/// <summary>
/// Locates the real app executable inside an extracted update package.
/// Handles root layouts and wrapper folders ("The Joseph Experience/…"), and
/// never mistakes the updater helper for the main app.
/// </summary>
public static class UpdatePackageLocator
{
    public static string? FindExecutable(string extractedDir)
    {
        try
        {
            var exes = Directory.EnumerateFiles(extractedDir, "*.exe", SearchOption.AllDirectories)
                .Where(p => !Path.GetFileName(p).Contains("Updater", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (exes.Count == 0) return null;

            var preferred = exes.FirstOrDefault(p =>
            {
                var name = Path.GetFileName(p);
                return name.Equals("JosephExperience.exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("TheJosephExperience.exe", StringComparison.OrdinalIgnoreCase);
            });
            return preferred ?? exes[0];
        }
        catch
        {
            return null;
        }
    }
}
