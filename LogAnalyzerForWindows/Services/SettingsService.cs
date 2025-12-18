using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models;
using Microsoft.Win32;

namespace LogAnalyzerForWindows.Services;

internal sealed class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string AutoStartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AzioEventLogAnalyzer";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
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

            UpdateAutoStartRegistry(settings.General.AutoStartWithWindows);
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

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _cachedSettings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

            if (loadedSettings is not null)
            {
                loadedSettings.Smtp.Password = DecryptPassword(loadedSettings.Smtp.Password);
                _cachedSettings = loadedSettings;
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

    private string EncryptPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        try
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var encryptedBytes = ProtectedData.Protect(
                passwordBytes,
                _encryptionKey,
                DataProtectionScope.CurrentUser);
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
            var decryptedBytes = ProtectedData.Unprotect(
                encryptedBytes,
                _encryptionKey,
                DataProtectionScope.CurrentUser);
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

    private static void UpdateAutoStartRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Cannot modify registry: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Registry IO error: {ex.Message}");
        }
    }
}
