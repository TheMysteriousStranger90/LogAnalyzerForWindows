using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LogAnalyzerForWindows.Interfaces;

public class FileSystemService : IFileSystemService
{
    public void OpenFolder(string path, Action<string> callback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            callback?.Invoke("Error: Path cannot be null or empty.");
            return;
        }

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
                callback?.Invoke("Folder opened successfully.");
            }
            else
            {
                callback?.Invoke($"Error: Folder does not exist at '{path}'.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred while opening folder '{path}': {ex.Message}");
            callback?.Invoke($"An error occurred: {ex.Message}");
        }
    }

    public void ArchiveLatestFolder(string path, Action<string> callback)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            callback?.Invoke("Error: Path cannot be null or empty.");
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                var latestDirectory = directoryInfo.GetDirectories()
                                                   .OrderByDescending(d => d.CreationTimeUtc)
                                                   .FirstOrDefault();

                if (latestDirectory != null)
                {
                    string zipFileName = $"{latestDirectory.Name}.zip";
                    string zipPath = Path.Combine(path, zipFileName);

                    if (File.Exists(zipPath))
                    {
                        try
                        {
                            File.Delete(zipPath);
                        }
                        catch (IOException ex)
                        {
                            Debug.WriteLine($"Error deleting existing zip file '{zipPath}': {ex.Message}");
                            callback?.Invoke($"Error: Could not delete existing archive '{zipFileName}'. {ex.Message}");
                            return;
                        }
                    }

                    ZipFile.CreateFromDirectory(latestDirectory.FullName, zipPath);
                    callback?.Invoke($"Folder '{latestDirectory.Name}' archived successfully as '{zipFileName}'.");
                }
                else
                {
                    callback?.Invoke("No subfolders found to archive.");
                }
            }
            else
            {
                callback?.Invoke($"Error: Directory does not exist at '{path}'.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred during archiving in '{path}': {ex.Message}");
            callback?.Invoke($"An error occurred during archiving: {ex.Message}");
        }
    }
}