using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Services;

internal sealed class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string TaskName = "AzioEventLogAnalyzer";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;
    private readonly byte[] _encryptionKey;
    private AppSettings _cachedSettings = new();

    public SettingsService()
    {
        var appFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AzioEventLogAnalyzer");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, SettingsFileName);

        var machineId = Environment.MachineName + Environment.UserName;
        _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(machineId));

        LoadSettings();
    }

    public AppSettings GetSettings() => _cachedSettings;

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var settingsToSave = new AppSettings
            {
                Smtp = new SmtpSettings
                {
                    Server = settings.Smtp.Server,
                    Port = settings.Smtp.Port,
                    FromEmail = settings.Smtp.FromEmail,
                    FromName = settings.Smtp.FromName,
                    Password = EncryptPassword(settings.Smtp.Password),
                    UseTls = settings.Smtp.UseTls
                },
                General = settings.General
            };

            var json = JsonSerializer.Serialize(settingsToSave, JsonOptions);
            await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);

            _cachedSettings = settings;

            await UpdateAutoStartTaskAsync(settings.General.AutoStartWithWindows).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Error saving settings: {ex.Message}");
            throw;
        }
    }

    public SmtpSettings GetSmtpSettings() => _cachedSettings.Smtp;

    public GeneralSettings GetGeneralSettings() => _cachedSettings.General;

    public bool IsSmtpConfigured()
    {
        return !string.IsNullOrWhiteSpace(_cachedSettings.Smtp.Server) &&
               _cachedSettings.Smtp.Port > 0 &&
               !string.IsNullOrWhiteSpace(_cachedSettings.Smtp.FromEmail) &&
               !string.IsNullOrWhiteSpace(_cachedSettings.Smtp.Password);
    }

    public bool IsAutoStartEnabled()
    {
        return CheckScheduledTaskExists();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _cachedSettings = new AppSettings();
                SyncAutoStartSetting();
                return;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

            if (loadedSettings is not null)
            {
                loadedSettings.Smtp.Password = DecryptPassword(loadedSettings.Smtp.Password);
                _cachedSettings = loadedSettings;
                SyncAutoStartSetting();
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Error parsing settings file: {ex.Message}");
            _cachedSettings = new AppSettings();
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Error reading settings file: {ex.Message}");
            _cachedSettings = new AppSettings();
        }
    }

    private void SyncAutoStartSetting()
    {
        _cachedSettings.General.AutoStartWithWindows = CheckScheduledTaskExists();
    }

    private static bool CheckScheduledTaskExists()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\" /FO LIST",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking scheduled task: {ex.Message}");
            return false;
        }
    }

    private static async Task UpdateAutoStartTaskAsync(bool enable)
    {
        try
        {
            if (enable)
            {
                await CreateScheduledTaskAsync().ConfigureAwait(false);
            }
            else
            {
                await DeleteScheduledTaskAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating auto-start task: {ex.Message}");
            throw new InvalidOperationException($"Failed to update auto-start: {ex.Message}", ex);
        }
    }

    private static async Task CreateScheduledTaskAsync()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Cannot determine executable path");
        }

        await DeleteScheduledTaskAsync().ConfigureAwait(false);

        var arguments = $"/Create /TN \"{TaskName}\" " +
                        $"/TR \"\\\"{exePath}\\\"\" " +
                        "/SC ONLOGON " +
                        "/RL HIGHEST " +
                        "/F " +
                        "/DELAY 0000:30";

        await RunSchtasksAsync(arguments).ConfigureAwait(false);
        Debug.WriteLine($"Scheduled task '{TaskName}' created successfully");
    }

    private static async Task DeleteScheduledTaskAsync()
    {
        var arguments = $"/Delete /TN \"{TaskName}\" /F";

        try
        {
            await RunSchtasksAsync(arguments).ConfigureAwait(false);
            Debug.WriteLine($"Scheduled task '{TaskName}' deleted");
        }
        catch
        {
        }
    }

    private static async Task RunSchtasksAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start schtasks.exe");
        }

        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            Debug.WriteLine($"schtasks output: {output}");
            Debug.WriteLine($"schtasks error: {error}");

            if (process.ExitCode == 1 && arguments.Contains("/Delete", StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException($"schtasks.exe failed with exit code {process.ExitCode}: {error}");
        }
    }

    private string EncryptPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        try
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(
                passwordBytes,
                _encryptionKey,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (CryptographicException ex)
        {
            Debug.WriteLine($"Encryption failed: {ex.Message}");
            return string.Empty;
        }
    }

    private string DecryptPassword(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
            return string.Empty;

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedPassword);
            var decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                encryptedBytes,
                _encryptionKey,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (FormatException ex)
        {
            Debug.WriteLine($"Invalid encrypted password format: {ex.Message}");
            return string.Empty;
        }
        catch (CryptographicException ex)
        {
            Debug.WriteLine($"Decryption failed: {ex.Message}");
            return string.Empty;
        }
    }
}
