# Log Analyzer for Windows

![Screenshot of the application](Screenshots/Screen1.png)

A Windows Event Log monitoring and analysis tool with real-time tracking, database storage, and advanced filtering capabilities.

## Features

### **Dual Operation Modes**
- **Real-time Monitoring Mode**: Continuously monitor Windows Event Logs with automatic updates
- **Database Mode**: View and analyze historical logs with pagination and session-based filtering

### **Advanced Filtering**
- Filter by log source (System, Application, etc.)
- Filter by log level (Error, Warning, etc.)
- Time-based filtering (Last hour, 24 hours, 3 days, 7 days)
- Session-based filtering for historical data

### **Data Management**
- Save logs in multiple formats (TXT, JSON)
- Persistent SQLite database for log history
- Session tracking with unique identifiers

### **Technical Features**
- Asynchronous operations for responsive UI
- Batch processing for large log volumes
- Efficient memory management
- Error handling and user feedback
- FileSystemWatcher for folder monitoring
- WMI (Windows Management Instrumentation) integration

## Requirements

- **Operating System**: Windows (tested on Windows 11)
- **System Language**: English or Russian (for proper event type detection)
- **.NET**: .NET 9.0
- **Administrator Rights**: Required for reading certain event logs (Security log)

## Usage

### Real-time Monitoring
1. Select a **Log Source** (e.g., System, Application)
2. Choose a **Log Level** to monitor (e.g., Error, Warning)
3. Set a **Time Range** (e.g., Last 24 hours)
4. Click **Start** to begin monitoring
5. View logs in real-time in the output window
6. Click **Stop** to end monitoring
7. Click **Save Logs** to export the current session

### Database Mode
1. Toggle **Database Mode** to view historical logs
2. Select a **Session** from the dropdown
3. Navigate through pages using pagination controls
4. Adjust **items per page** for comfortable viewing
5. Click **Export Session** to save all logs from a session
6. Use **Clear All History** to delete all stored logs

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## Author

Bohdan Harabadzhyu

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## SourceForge

[![Download LogAnalyzerForWindows](https://a.fsdn.com/con/app/sf-download-button)](https://sourceforge.net/projects/loganalyzerforwindows/files/latest/download)
