using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LogAnalyzerForWindows.Interfaces;

namespace LogAnalyzerForWindows.Services;

public class FileSystemService : IFileSystemService
{
    public void OpenFolder(string path, Action<string> callback)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
                callback?.Invoke("Folder opened.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            callback?.Invoke($"An error occurred: {ex.Message}");
        }
    }

    public void ArchiveLatestFolder(string path, Action<string> callback)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var directories = new DirectoryInfo(path).GetDirectories();
                var latestDirectory = directories.OrderByDescending(d => d.CreationTime).FirstOrDefault();

                if (latestDirectory != null)
                {
                    string zipPath = Path.Combine(path, $"{latestDirectory.Name}.zip");

                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    ZipFile.CreateFromDirectory(latestDirectory.FullName, zipPath);
                    callback?.Invoke("Latest folder archived.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
            callback?.Invoke($"An error occurred: {ex.Message}");
        }
    }
}
