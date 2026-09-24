# AdmXmlDb - Desktop XML-to-Database Integration Agent

A deployable Windows application suite consisting of a management UI and a background worker service to parse local XML files and insert data into external SQL Server databases based on dynamic mapping rules.

## Architecture

- **AdmXmlDb.Management** - WPF application for configuration and monitoring
- **AdmXmlDb.Worker** - Windows Service that processes XML files on a schedule
- **AdmXmlDb.Core** - Shared library (entities, DbContext, XML parsing, SQL generation), `net10.0`
- **AdmXmlDb.Core.Tests** - xUnit tests for the core library
- **Shared SQLite** - Local configuration and execution logs at `%LocalAppData%\AdmXmlDb\admxmldb.db`

## Requirements

- Windows 10/11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source
- [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) to run the published apps
- SQL Server (target database)
- SMTP server (for error notifications, optional)

The solution targets `net10.0` (Core and tests) and `net10.0-windows` (Management UI and Worker). Publish is framework-dependent (`--self-contained false`). The Inno Setup installer refuses to continue unless the .NET 10 Desktop Runtime is already installed.

## Build

```powershell
cd src
dotnet build AdmXmlDb.sln
```

Tests (from the repository root):

```powershell
dotnet test AdmXmlDb.Core.Tests
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
- Stop if the .NET 10 Desktop Runtime (x64) is not installed
- Install the Management UI and Worker to `C:\Program Files\AdmXmlDb\`
- Register and start the `AdmXmlDbWorker` Windows Service (auto-start on boot)
- Create a desktop shortcut

## Uninstall

Use Windows "Add or remove programs" or run the uninstaller. The service will be stopped and removed automatically.

## Configuration

### General Settings (SMTP)
Configure SMTP for error notifications. Passwords are encrypted with Windows DPAPI before storage.

### Task Management
1. Create a task with a name, input/output/error folder paths, and the target SQL Server connection string
2. On Task Settings, set the Quartz cron expression (for example `0 0 * * * ?` for hourly), processing delay, error notification addresses, and whether the task is enabled
3. For UNC paths (`\\server\share`), set the Windows username and password used to reach the share
4. Build the output directory from a static base path plus optional XPath or fixed subdirectory parts
5. Add mappings: target table, column, SQL type, XML node (XPath), optional find/replace, default value, value template, literal flag, and Required
6. Save the task and the mappings. Config can be exported and loaded again from the task screen

A Required mapping fails the file when the XML node is missing or empty. Use Test on a mapping row, or Test on the task, to check XPath results against a sample file.

**Note:** Restart the Worker Service after adding or modifying tasks so the schedule picks up the changes.

### Simulation
Upload a sample XML, select a task for mappings, view parsed data and generated SQL. Use "Dry Run" to test the INSERT against the target database without persisting data (transaction is always rolled back).

## Security

- SMTP passwords and database connection strings are encrypted at rest using Windows DPAPI (CurrentUser scope)
- All configuration is stored locally in SQLite

## License

MIT
