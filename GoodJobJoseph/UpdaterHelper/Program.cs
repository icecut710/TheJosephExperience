using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace JosephExperience.UpdaterHelper;

/// <summary>
/// Small executable that helps the main app replace itself.
/// The main app cannot overwrite its own executable while running,
/// so this helper handles the binary swap after the app exits.
/// </summary>
internal static class UpdaterMain
{
    private static int Main(string[] args)
    {
        try
        {
            // Parse arguments
            string? currentProcessIdStr = null;
            string? sourcePath = null;
            string? targetPath = null;

            string? action = "install";

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--current-pid":
                        currentProcessIdStr = args[i + 1];
                        i++;
                        break;
                    case "--source":
                        sourcePath = args[i + 1];
                        i++;
                        break;
                    case "--target":
                        targetPath = args[i + 1];
                        i++;
                        break;
                    case "--action":
                        action = args[i + 1];
                        i++;
                        break;
                }
            }

            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
            {
                Console.Error.WriteLine("Updater: Missing required arguments (--source and --target)");
                return 1;
            }

            if (action == "rollback")
            {
                Console.WriteLine("Updater: Rollback mode — restoring from backup.");
                try
                {
                    if (File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, targetPath, overwrite: true);
                        Console.WriteLine("Updater: Rollback complete.");
                        try { File.Delete(sourcePath); } catch { }
                    }
                    else
                    {
                        Console.Error.WriteLine("Updater: Rollback source not found: " + sourcePath);
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Updater: Rollback failed: " + ex.Message);
                    return 1;
                }

                // Launch the rolled-back executable
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var newProcess = Process.Start(startInfo);
                    Console.WriteLine("Updater: Rolled-back executable launched.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Updater: Failed to launch rolled-back executable: " + ex.Message);
                    return 1;
                }
                return 0;
            }

            int? currentProcessId = currentProcessIdStr != null ? (int?)int.Parse(currentProcessIdStr) : null;

            // Step 1: Wait for the current app process to exit
            if (currentProcessId.HasValue)
            {
                using var process = Process.GetProcessById(currentProcessId.Value);
                // Wait up to 30 seconds for the process to exit
                if (!process.WaitForExit(30000))
                {
                    Console.Error.WriteLine("Updater: Timed out waiting for app to exit");
                    return 1;
                }
            }

            // Step 2: Verify source exists
            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine("Updater: Source file not found: " + sourcePath);
                return 1;
            }

            // Step 3: Create a backup of the existing app executable
            string backupPath = targetPath + ".old";
            try
            {
                File.Copy(targetPath, backupPath, overwrite: true);
                Console.WriteLine("Updater: Backup created at " + backupPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Updater: Failed to create backup: " + ex.Message);
                // Continue anyway - we'll try to replace
            }

            // Step 4: Replace the old executable with the new one
            try
            {
                File.Move(sourcePath, targetPath, overwrite: true);
                Console.WriteLine("Updater: Executable replaced successfully");

                // Step 5: Verify replacement exists
                if (!File.Exists(targetPath))
                {
                    Console.Error.WriteLine("Updater: Replacement verification failed");
                    // Try to restore from backup
                    try { File.Move(backupPath, targetPath, overwrite: true); }
                    catch { }
                    return 1;
                }

                // Step 6: Launch the new executable
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var newProcess = Process.Start(startInfo);
                    Console.WriteLine("Updater: New executable launched");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Updater: Failed to launch new executable: " + ex.Message);
                    // Try to restore from backup
                    try { File.Move(backupPath, targetPath, overwrite: true); }
                    catch { }
                    return 1;
                }

                // Step 7: Remove backup after successful restart
                try
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                catch { }

                // Step 8: Clean any stale update files in the temp updates folder
                try
                {
                    string? appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string updatesPath = Path.Combine(appData, "JosephExperience", "updates");
                    if (Directory.Exists(updatesPath))
                    {
                        // Remove the specific version folder that was just used
                        // (the main app should have already cleaned up or the new app will)
                        var dirs = Directory.GetDirectories(updatesPath);
                        foreach (var dir in dirs)
                        {
                            try { Directory.Delete(dir, true); } catch { }
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Updater: Critical error during replacement: " + ex.Message);
                // Attempt rollback from backup
                try
                {
                    if (File.Exists(backupPath))
                    {
                        // Move backup back to original location
                        File.Move(backupPath, targetPath, overwrite: true);
                    }
                }
                catch { }
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Updater: Fatal error: " + ex.Message);
            return 1;
        }
    }
}