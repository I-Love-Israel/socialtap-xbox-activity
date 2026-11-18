using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace SocialTapXboxActivity;

/// <summary>A game running from an Xbox app install on this PC.</summary>
internal sealed record GameInfo(string Title, string ProcessName, DateTime StartedAt, string? CoverPath);

/// <summary>
/// Finds the game being played on this PC using only local data: running processes and the
/// game folders the Xbox app installs into (C:\XboxGames\&lt;Game&gt;\Content, on any drive).
/// It makes no network or Xbox Live calls.
/// </summary>
/// <remarks>
/// Store games installed under C:\Program Files\WindowsApps are not detected: that folder is
/// access-restricted and its contents are not listable without admin rights.
/// </remarks>
internal static class GameDetector
{
    private static readonly string[] IgnoredProcessNames =
    {
        "XboxPcApp", "XboxPcAppFT", "XboxPcTray", "GameBar", "GameBarFTServer",
        "GameBarPresenceWriter", "gamingservices", "gamingservicesnet", "XboxService", "SocialTapXboxActivity",
    };

    private sealed record ManifestInfo(string? DisplayName, string? Logo);

    /// <summary>Returns the first Xbox-installed game that has a visible window, or null.</summary>
    public static GameInfo? FindRunningGame()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (IgnoredProcessNames.Any(n => n.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var exePath = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    continue;
                }

                var gameFolder = GetXboxGameFolder(exePath);
                if (gameFolder is null)
                {
                    continue;
                }

                var contentFolder = Path.Combine(gameFolder, "Content");
                var manifest = ReadManifest(Path.Combine(contentFolder, "appxmanifest.xml"));
                var displayName = manifest?.DisplayName;
                var title = !string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
                    ? displayName
                    : Path.GetFileName(gameFolder);

                // Launchers are apps, not the game itself.
                if (title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var cover = ResolveCover(contentFolder, manifest?.Logo);
                return new GameInfo(title, process.ProcessName, StartTimeOf(process), cover);
            }
            catch
            {
                // Access denied for other users' processes, or the process exited during the scan.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// For an exe under ...\XboxGames\&lt;Game&gt;\..., returns ...\XboxGames\&lt;Game&gt;; otherwise null.
    /// </summary>
    private static string? GetXboxGameFolder(string exePath)
    {
        var parts = exePath.Split(Path.DirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Equals("XboxGames", StringComparison.OrdinalIgnoreCase))
            {
                return string.Join(Path.DirectorySeparatorChar, parts.Take(i + 2));
            }
        }

        return null;
    }

    private static ManifestInfo? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var doc = XDocument.Load(path);
            var properties = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties");
            var displayName = properties?.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value?.Trim();
            var logo = properties?.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value?.Trim();
            var visual = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            var square = visual?.Attribute("Square150x150Logo")?.Value?.Trim();
            return new ManifestInfo(displayName, !string.IsNullOrEmpty(square) ? square : logo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Store assets come in several scales (Logo.png, Logo.scale-200.png, ...). Picks the largest.
    /// </summary>
    private static string? ResolveCover(string contentFolder, string? relativeLogo)
    {
        if (string.IsNullOrWhiteSpace(relativeLogo))
        {
            return null;
        }

        var full = Path.Combine(contentFolder, relativeLogo);
        var dir = Path.GetDirectoryName(full);
        var stem = Path.GetFileNameWithoutExtension(full);
        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        var best = Directory.EnumerateFiles(dir, "*.png")
            .Where(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return name.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

        return best ?? (File.Exists(full) ? full : null);
    }

    private static DateTime StartTimeOf(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}
