using System;

namespace LogAnalyzerForWindows.Interfaces;

public interface IFileSystemService
{
    void OpenFolder(string path, Action<string> callback);
    void ArchiveLatestFolder(string path, Action<string> callback);
}