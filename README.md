# AdmXmlDb - Desktop XML-to-Database Integration Agent

A deployable Windows application suite consisting of a management UI and a background worker service to parse local XML files and insert data into external SQL Server databases based on dynamic mapping rules.

## Architecture

- **AdmXmlDb.Management** - WPF application for configuration and monitoring
- **AdmXmlDb.Worker** - Windows Service that processes XML files on a schedule
- **AdmXmlDb.Core** - Shared library (entities, DbContext, XML parsing, SQL generation)
- **Shared SQLite** - Local configuration and execution logs at `%LocalAppData%\AdmXmlDb\admxmldb.db`

## Requirements

- Windows 10/11 (x64)
- .NET 10.0 Desktop Runtime
- SQL Server (target database)
- SMTP server (for error notifications, optional)

## Build

```powershell
cd src
dotnet build
```

## Run from Source

**Management UI (development):**
```powershell
cd src
dotnet run --project AdmXmlDb.Management
```

**Worker Service (development - run as console):**
```powershell
cd src
dotnet run --project AdmXmlDb.Worker
```

## Publish for Installer

```powershell
cd src
dotnet publish AdmXmlDb.Management -c Release -r win-x64 --self-contained false -o ../installer/out/Management
dotnet publish AdmXmlDb.Worker -c Release -r win-x64 --self-contained false -o ../installer/out/Worker
```

**Note:** When releasing a new version, update both `AdmXmlDb.Management.csproj` (`<Version>`) and `installer/setup.iss` (`MyAppVersion`) to the same value.

## Install with Inno Setup

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php)
2. Publish the applications (see above)
3. Open `installer/setup.iss` in Inno Setup Compiler
4. Build the installer (requires Administrator for service installation)

The installer will:
- Install the Management UI and Worker to `C:\Program Files\AdmXmlDb\`
- Register and start the `AdmXmlDbWorker` Windows Service (auto-start on boot)
- Create a desktop shortcut

## Uninstall

Use Windows "Add or remove programs" or run the uninstaller. The service will be stopped and removed automatically.

## Configuration

### General Settings (SMTP)
Configure SMTP for error notifications. Passwords are encrypted with Windows DPAPI before storage.

### Task Management
1. Create a task with name, input/output/error folder paths, and target SQL Server connection string
2. Set cron expression (e.g. `0 0 * * * ?` for hourly)
3. Add data mappings: XPath → Column Name → SQL Type (INT, VARCHAR, DATETIME, BIT)
4. Save task and mappings

**Note:** Restart the Worker Service after adding or modifying tasks to pick up changes.

### Simulation
Upload a sample XML, select a task for mappings, view parsed data and generated SQL. Use "Dry Run" to test the INSERT against the target database without persisting data (transaction is always rolled back).

## Security

- SMTP passwords and database connection strings are encrypted at rest using Windows DPAPI (CurrentUser scope)
- All configuration is stored locally in SQLite

## License

MIT
