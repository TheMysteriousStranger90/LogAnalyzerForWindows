namespace LogAnalyzerForWindows.Interfaces;

/// <summary>
/// Defines the contract for file system operations.
/// </summary>
/// <remarks>
/// This interface provides functionality for common file system operations such as
/// opening folders in the system file explorer and creating archives of directories.
/// Implementations should handle platform-specific behaviors and error scenarios.
/// </remarks>
internal interface IFileSystemService
{
    /// <summary>
    /// Opens the specified folder in the file explorer.
    /// </summary>
    /// <param name="path">The path to the folder to open.</param>
    /// <param name="callback">Callback to receive status updates.</param>
    void OpenFolder(string path, Action<string> callback);

    /// <summary>
    /// Archives the latest subfolder in the specified path.
    /// </summary>
    /// <param name="path">The base path containing subfolders.</param>
    /// <param name="callback">Callback to receive status updates.</param>
    void ArchiveLatestFolder(string path, Action<string> callback);
}
