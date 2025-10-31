using System.Diagnostics;
using System.IO.Compression;
using LogAnalyzerForWindows.Interfaces;

namespace LogAnalyzerForWindows.Services;

internal sealed class FileSystemService : IFileSystemService
{
    public void OpenFolder(string path, Action<string> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        try
        {
            if (!Directory.Exists(path))
            {
                callback($"Folder does not exist: {path}");
                return;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            };

            Process.Start(processStartInfo);
            callback($"Opened folder: {path}");
        }
        catch (UnauthorizedAccessException ex)
        {
            callback($"Access denied opening folder: {ex.Message}");
        }
        catch (IOException ex)
        {
            callback($"IO error opening folder: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            callback($"Cannot open folder: {ex.Message}");
        }
    }

    public void ArchiveLatestFolder(string path, Action<string> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        try
        {
            if (!Directory.Exists(path))
            {
                callback("Base folder does not exist.");
                return;
            }

            var subFolders = Directory.GetDirectories(path);

            if (subFolders.Length == 0)
            {
                callback("No subfolders found to archive.");
                return;
            }

            var latestFolder = subFolders
                .Select(f => new DirectoryInfo(f))
                .OrderByDescending(d => d.CreationTimeUtc)
                .FirstOrDefault();

            if (latestFolder is null)
            {
                callback("Could not determine latest folder.");
                return;
            }

            string zipFileName = $"{latestFolder.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            string zipFilePath = Path.Combine(path, zipFileName);

            if (File.Exists(zipFilePath))
            {
                callback($"Archive already exists: {zipFileName}");
                return;
            }

            ZipFile.CreateFromDirectory(latestFolder.FullName, zipFilePath, CompressionLevel.Optimal, false);
            callback($"Archive created: {zipFileName}");
        }
        catch (UnauthorizedAccessException ex)
        {
            callback($"Access denied during archiving: {ex.Message}");
        }
        catch (IOException ex)
        {
            callback($"IO error during archiving: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            callback($"Invalid operation during archiving: {ex.Message}");
        }
    }
}
