# THJPatcher Codebase Overview

This document provides a summary of the `thj-patcher` codebase based on initial exploration.

## Project Overview

*   **Purpose:** A custom game patcher for the "The Heroes Journey" EverQuest private server.
*   **Goal:** Keep the user's EverQuest client installation up-to-date with the server's required files by comparing local files against a master list (`filelist.yml`), downloading updates, and deleting obsolete files.

## Technology Stack

*   **Language:** C#
*   **Framework:** .NET with Windows Presentation Foundation (WPF) for the UI.
*   **Key Libraries:**
    *   `YamlDotNet`: For parsing the YAML configuration files (`filelist.yml`, `thjpatcher.yml`, `changelog.yml`).
    *   `System.Text.Json`: For parsing JSON responses from web APIs (server status, changelogs).
    *   `System.Net.Http.HttpClient`: For network requests (downloads, API calls).
    *   `MaterialDesignThemes.Wpf`: For UI styling (theme files extracted from embedded resources at startup).
    *   `Costura.Fody`: Used to embed .NET dependencies directly into the final executable, simplifying deployment.

## Dependency Management

*   NuGet packages are used for external libraries.
*   Package versions are managed centrally using `Directory.Packages.props`.
*   Key Dependencies (from `Directory.Packages.props`):
    *   `YamlDotNet`: YAML parsing.
    *   `MaterialDesignThemes` / `MaterialDesignColors`: WPF UI components and styling.
    *   `Costura.Fody` / `Fody`: IL Weaving to embed dependencies into the output `.exe`.
    *   `LibGit2Sharp`: Git repository interaction (**Unused** based on code search).
    *   `Microsoft-WindowsAPICodePack-Shell`: Windows Shell integration features.

## Project Structure

```
.
├── .git/
├── .github/
├── rof/                  # Contains patch files served by the download prefix URL
├── THJPatcher/           # Main Solution Folder (.NET)
│   ├── THJPatcher/       # C# Project Folder (THJPatcher.csproj)
│   │   ├── Models/       # Data structures (POCOs)
│   │   │   ├── ChangelogModels.cs   # Defines ChangelogInfo for changelog data
│   │   │   ├── FileList.cs          # Defines FileList & FileEntry for filelist.yml
│   │   │   ├── LoadingMessages.cs   # Random loading messages
│   │   │   ├── ServerStatus.cs      # Defines classes for server status API JSON
│   │   │   └── VersionTypes.cs      # Defines client version enum/constants
│   │   ├── Properties/   # Standard VS project properties (AssemblyInfo.cs etc.)
│   │   ├── Resources/    # Embedded resources (theme XAML files, icons)
│   │   ├── Utilities/    # Helper classes and services
│   │   │   ├── ChangelogService.cs      # Handles changelog data and formatting
│   │   │   ├── ClientVersionService.cs  # Manages client version detection
│   │   │   ├── CommandLineService.cs    # Processes command line arguments
│   │   │   ├── Converters/              # WPF value converters
│   │   │   ├── FileSystemService.cs     # Handles file integrity scanning
│   │   │   ├── GameLaunchService.cs     # Manages game launching
│   │   │   ├── InitializationService.cs # Coordinates application startup
│   │   │   ├── NavigationService.cs     # Manages UI navigation
│   │   │   ├── OptimizationService.cs   # Handles game optimizations
│   │   │   ├── PatcherService.cs        # Handles file downloads and patching
│   │   │   ├── PEModifier.cs            # Modifies PE header flags (for 4GB patch)
│   │   │   ├── SelfUpdateService.cs     # Manages patcher updates
│   │   │   └── ServerStatusService.cs   # Fetches server status information
│   │   ├── App.config          # Standard .NET application config
│   │   ├── ChangelogWindow.xaml(.cs) # UI for full changelog history
│   │   ├── CustomMessageBox.xaml(.cs) # Reusable confirmation dialog
│   │   ├── FodyWeavers.xml     # Configures Fody (e.g., for Costura)
│   │   ├── IniLibrary.cs       # Manages local state/config (thjpatcher.yml) & cached changelog (changelog.yml)
│   │   ├── LatestChangelogWindow.xaml(.cs) # UI for showing *new* changelogs
│   │   ├── MainWindow.xaml     # Main UI definition (WPF)
│   │   ├── MainWindow.xaml.cs  # Main UI controller (orchestrates services)
│   │   ├── Program.cs          # Application entry point & initial setup
│   │   ├── README.md           # Documentation of application structure
│   │   ├── StatusLibrary.cs    # Handles logging/progress reporting to UI
│   │   ├── UtilityLibrary.cs   # Static helpers (download, MD5, process start, Win32 API calls)
│   │   ├── ... (Other .cs, .xaml files)
│   ├── build.bat             # Command-line build script (runs MSBuild)
│   ├── Directory.Build.props # MSBuild common properties
│   ├── Directory.Packages.props # MSBuild central package management
│   └── THJPatcher.sln      # Visual Studio Solution File (contains only THJPatcher project)
├── CHANGELOG.md            # Manual project changelog
├── CODEBASE_OVERVIEW.md    # This document
├── eqemupatcher.png        # Patcher UI image asset
├── filelist.yml          # Example/potentially outdated master file list? (large, purpose uncertain relative to dynamic download)
├── filelistbuilder.yml   # Configuration (client type, download prefix) for the tool that generates filelist.yml.
├── LICENSE
├── manifest.json         # Generated by nexus.js; for server-side tooling (XXH64 hashes), NOT used by C# patcher.
├── nexus.js              # Node.js script (using xxhash-wasm) to generate manifest.json from rof/ contents.
├── package.json          # Basic Node.js package file for nexus.js script.
└── README.md               # Project description, build steps, basic config info.
```

## Configuration & State

The patcher uses several sources for configuration and state management:

*   **Remote Configuration:**
    *   `filelist.yml` (Dynamic): Downloaded from `filelistUrl` (defined in `FileListUrl.cs` but can be overridden by fallback). This is the **primary driver** for patching. Contains file lists (`downloads`, `deletes`), MD5 hashes, version, and the base URL for downloads (`downloadprefix`).
    *   `delete.txt` (Dynamic): Optional file downloaded from the server (URL derived from `filelist.yml`). Provides an additional list of files to delete.
    *   Web APIs (Dynamic): Used to fetch server status and changelog entries (URLs hardcoded in `MainWindow.xaml.cs`).
    *   Patcher Hash File (Dynamic): Text file (`<fileName>-hash.txt`) downloaded from `patcherUrl` containing the MD5 hash of the latest patcher executable for self-updating.
*   **Hardcoded Configuration (in C# code):**
    *   Default URLs (`PatcherUrl.cs`, `FileListUrl.cs`), Server Name (`ServerName.cs`), File Name (`FileName.cs`).
    *   Changelog/Server Status API endpoints (`MainWindow.xaml.cs`).
    *   Supported client versions (`ClientVersionService.cs`).
*   **Local State (`thjpatcher.yml`):**
    *   Managed by `IniLibrary.cs` (using `YamlDotNet`). Stored next to the executable.
    *   Tracks `LastPatchedVersion` (compared against `filelist.yml`).
    *   Stores user preferences (`AutoPlay`, `AutoPatch`).
    *   Stores other state like `LastIntegrityCheck` timestamp, `DeleteChangelog` flag.
*   **Cached Data (`changelog.yml`):**
    *   Managed by `IniLibrary.cs`. Stores previously fetched changelog entries locally.
*   **Target Game Files:**
    *   `eqclient.ini`: Modified by optimization features.
    *   `eqgame.exe`: Modified by the 4GB Patch feature.
    *   `dxvk.conf`: Created by the patcher for Linux/Proton compatibility.

## Core Functionality Flow

### 1. Startup (`Program.cs` -> `MainWindow.xaml.cs` -> `MainWindow_Loaded`)

1.  **Environment Setup:** Configures assembly resolving (`CurrentDomain_AssemblyResolve`) and global exception handling (`CurrentDomain_UnhandledException`, `application.DispatcherUnhandledException`).
2.  **Theme Extraction:** Ensures Material Design theme `.xaml` files exist in a `themes` subfolder, extracting them from embedded resources if necessary.
3.  **Launch `MainWindow`:** Sets `MainWindow.xaml` as the `StartupUri`.
4.  **Service Initialization:**
    *   Initializes all service components:
        *   `CommandLineService`: Processes command-line arguments and sets debug/silent mode flags.
        *   `ClientVersionService`: Manages client version detection and compatibility.
        *   `ServerStatusService`: Fetches and displays server status information.
        *   `ChangelogService`: Handles changelog retrieval and display.
        *   `PatcherService`: Manages downloading and patching of game files.
        *   `FileSystemService`: Handles file integrity scanning and verification.
        *   `OptimizationService`: Provides game optimization features.
        *   `GameLaunchService`: Manages game launching and process checks.
        *   `SelfUpdateService`: Handles patcher update checks and installation.
        *   `NavigationService`: Manages UI navigation between panels.
        *   `InitializationService`: Coordinates application startup sequence.
5.  **Patcher Self-Update Check:**
    *   Uses the `SelfUpdateService` to check for updates to the patcher itself.
    *   If an update is available, the UI is configured to show the update message and enable the patch button.
6.  **Game State Preparation:**
    *   Uses the `InitializationService` to perform initialization tasks:
        *   Cleans up outdated files
        *   Configures system components
        *   Initiates file integrity scan through the `FileSystemService`
        *   Updates UI elements based on scan results

### 2. Game Update Process

1.  **File Integrity Scan (`FileSystemService`):**
    *   Downloads and parses `filelist.yml`
    *   Compares local files against the expected state
    *   Builds a list of files needing updates
    *   Handles special cases like `dinput8.dll`
    *   Updates UI based on scan results

2.  **Patching Process (`StartPatch` -> `PatcherService`):**
    *   For self-updates, uses `SelfUpdateService` to download and install the updated patcher
    *   For game updates:
        *   Verifies critical files using `FileSystemService.ForceDinput8CheckAsync`
        *   Downloads the current file list
        *   Builds the download queue with `FileSystemService.BuildFileList`
        *   Downloads and patches files using `PatcherService.DownloadAndPatchFilesAsync`
        *   Deletes obsolete files using `PatcherService.DeleteFilesAsync`
        *   Updates patcher state and UI

3.  **Game Launch (`GameLaunchService`):**
    *   Verifies the game is not already running
    *   Launches the game with appropriate parameters
    *   Handles silent mode operations
    *   Closes the patcher after successful launch if configured

## Key Components & Responsibilities

*   **`MainWindow.xaml.cs`:** UI controller that orchestrates the various services.
    *   Handles UI event handlers (button clicks, checkbox changes)
    *   Manages and coordinates the various service components
    *   Updates UI elements based on service outputs
    *   No longer contains direct business logic for patching, file scanning, etc.

*   **Service Components:**
    *   **`PatcherService`:** Handles downloading and patching game files.
        *   Downloads files based on the file list
        *   Manages special cases for locked DLLs
        *   Handles file deletion operations
        *   Implements retry logic for failed downloads
    
    *   **`FileSystemService`:** Manages file integrity scanning and verification.
        *   Performs quick and full file integrity scans
        *   Detects missing or modified files
        *   Handles critical files like dinput8.dll
        *   Builds file lists for patching operations
    
    *   **`OptimizationService`:** Provides game optimization features.
        *   Applies 4GB patch to the EverQuest executable
        *   Fixes UI scale issues in configuration files
        *   Optimizes graphics settings
        *   Clears game caches and resets settings
    
    *   **`GameLaunchService`:** Manages game launching and process checks.
        *   Checks if the game is already running
        *   Launches the game with appropriate parameters
        *   Handles silent mode operations
    
    *   **`SelfUpdateService`:** Manages patcher updates.
        *   Checks for updates to the patcher
        *   Downloads and installs updated patcher versions
        *   Verifies update integrity with hash checks
    
    *   **`NavigationService`:** Handles UI navigation.
        *   Manages switching between panels
        *   Opens external resources (game folder, URLs)
    
    *   **`ChangelogService`:** Manages changelog data.
        *   Fetches changelog entries from the server
        *   Formats changelog content for display
        *   Tracks new changelog entries
    
    *   **`InitializationService`:** Coordinates application startup.
        *   Manages the startup sequence
        *   Cleans up outdated files
        *   Displays welcome messages
    
    *   **`ClientVersionService`:** Handles client version detection.
        *   Manages client version information
        *   Performs compatibility checks
    
    *   **`CommandLineService`:** Processes command line arguments.
        *   Parses arguments from the command line
        *   Configures runtime options like debug mode

*   **Support Components:**
    *   **`Program.cs`:** Application entry point. Sets up basic environment and starts the WPF application.
    *   **`StatusLibrary.cs`:** Provides logging and progress reporting facilities used by all services.
    *   **`IniLibrary.cs`:** Manages local state persistence and user settings.
    *   **`UtilityLibrary.cs`:** Collection of static utility methods used across the application.

*   **Models:**
    *   **`Models/FileList.cs`:** Defines data structures for file listings (`FileList`, `FileEntry`).
    *   **`Models/ChangelogModels.cs`:** Defines data structures for changelog information.
    *   **`Models/ServerStatus.cs`:** Defines data structures for server status information.
    *   **`Models/LoadingMessages.cs`:** Contains random loading messages displayed during initialization.

## Features Summary

*   **Core:** Game File Patching (Download/Update/Delete based on `filelist.yml`), Patcher Self-Update.
*   **Informational:** Changelog Display (API fetch + local cache), Server Status Check (API fetch).
*   **Automation:** Game Launch, Auto-Patch option, Auto-Play option.
*   **Optimizations/Utilities:** Apply 4GB Patch (modifies `eqgame.exe`), Optimize Graphics (modifies `eqclient.ini`), Fix UI Scale (modifies `eqclient.ini`), Clear Cache (deletes specific dirs), File Integrity Scan (manual full check).

## Security Considerations

*   **Hashing:** Uses MD5 for file integrity and self-update checks. MD5 is known to be vulnerable to collisions and is not cryptographically secure for verification. Using SHA-256 would be significantly better.
*   **Transport Security:** Downloads use HTTPS, providing protection against passive eavesdropping and man-in-the-middle attacks during transit. Certificate pinning is not used.
*   **File Path Handling:** File paths from `filelist.yml` are treated as relative. `UtilityLibrary.IsPathChild` attempts to prevent path traversal attacks (`..\`) for local file operations, but server-side validation of `filelist.yml` content is crucial.
*   **Executable Modification:** The patcher modifies `eqgame.exe` (4GB patch) and itself (self-update). While specific filenames are targeted, the risk of modifying unintended files exists if the `filelist.yml` or download sources were compromised.
*   **Dependencies:** Relies on third-party libraries (NuGet). Supply chain security depends on the integrity of these packages.

## Error Handling

*   Uses `try-catch` blocks around I/O and network operations.
*   Errors are generally logged to the UI via `StatusLibrary` or displayed in `MessageBox` popups.
*   Specific exception details may be shown to the user, potentially lacking user-friendliness.
*   The PatcherService implements retry logic for failed downloads.
*   Handles corrupt local config (`thjpatcher.yml`) by resetting to defaults.

## Logging

*   Logging is primarily directed to the UI textbox via `StatusLibrary`.
*   `Debug.WriteLine` is used in some areas (e.g., `IniLibrary`).
*   **No persistent file logging** is implemented, making post-mortem debugging difficult.
*   Log messages include Info, Warning, Error, and Debug levels, but lack runtime configuration.

## Performance & Concurrency

*   **Scanning/Hashing:** File existence checks and MD5 hashing during update checks are performed sequentially per file. This can be I/O bound and slow for many files. `Task.Run` is used to avoid blocking the UI thread.
*   **Downloads:** Files are downloaded sequentially in the patching process. Downloading multiple files concurrently could improve speed.
*   **Concurrency:** Uses `async/await` and `Task.Run` appropriately to keep the UI responsive. Cancellation tokens seem correctly propagated for network operations. Race conditions seem unlikely given the primary state management on the UI thread.
*   **Resource Management:** `IDisposable` resources (streams) seem correctly handled with `using` statements. `HttpClient` instances are created per request rather than reused (minor inefficiency, potential socket exhaustion under extreme load not expected here).
*   **UI Feedback:** Progress reporting is based on file counts or total bytes, which might appear unresponsive during single large file downloads.

## Build/Release Process (Developer Workflow)

Based on the project structure and included scripts/configuration files, the developer process involves:

1.  **Update Patch Files:** Place new/updated game files into the `rof/` directory structure.
2.  **Generate Manifests/Lists:**
    *   Run `nexus.js` (Node.js script) to scan `rof/` and generate/update `manifest.json` with XXH64 hashes (purpose/use of this manifest unclear in C# patcher context; perhaps for separate tooling).
    *   Use an unspecified tool or script, likely configured by `filelistbuilder.yml`, to generate `filelist.yml` with MD5 hashes and the correct `downloadprefix` / `version`.
    *   Prepare `delete.txt` if files need removal.
3.  **Build Patcher:** Compile the C# solution (`THJPatcher.sln`) using Visual Studio or `build.bat` to produce `THJPatcher.exe` (with dependencies embedded via Costura.Fody).
4.  **Prepare Release Artifacts:** Create the patcher executable hash file (`<fileName>-hash.txt`), potentially update `patcher_changelog.md`.
5.  **Upload:** Upload the new patcher executable, hash file, changelog, `filelist.yml`, `delete.txt`, and the contents of `rof/` (or equivalent) to the respective web/download servers specified by the URLs in the code/config.

## Implemented Improvements

*   **Refactoring (SRP):** The codebase has been refactored to follow the Single Responsibility Principle by extracting functionality from the monolithic `MainWindow.xaml.cs` into specialized service classes:
    *   `PatcherService` - Handles file downloading and patching
    *   `FileSystemService` - Manages file integrity scanning
    *   `OptimizationService` - Handles game optimizations
    *   `GameLaunchService` - Manages game launching
    *   `SelfUpdateService` - Handles patcher updates
    *   `NavigationService` - Manages UI navigation
    *   `ChangelogService` - Handles changelog data
    *   `InitializationService` - Coordinates application startup
    *   `ClientVersionService` - Handles client version detection
    *   `CommandLineService` - Processes command line arguments
    *   `ServerStatusService` - Fetches server status information

*   **Error Handling:** Improved error handling with retry logic for network operations in the `PatcherService`.

*   **DRY Principle:** Eliminated code duplication by centralizing functionality in dedicated services, particularly for file operations and update checks.

## Potential Future Improvements

*   **Interfaces & Dependency Injection:** Create interfaces for all services and implement a proper dependency injection container.

*   **Testing:** Introduce automated unit and integration tests to improve reliability and facilitate safer refactoring.

*   **Security:** Replace MD5 with SHA-256 for file verification. Perform stricter validation of paths from `filelist.yml`. Review dependency security.

*   **Additional Service Extraction:** Extract remaining functionality into specialized services (e.g., ConfigurationService, LoggingService).

*   **Logging:** Implement persistent file logging using a standard library (Serilog, NLog) with configurable levels.

*   **Performance:** Consider parallelizing file scanning/hashing and downloads. Reuse `HttpClient` instances.

*   **Dependencies:** Remove the unused `LibGit2Sharp` NuGet package.

This overview provides a comprehensive understanding of the `thj-patcher` codebase, including the recent architectural improvements that enhance maintainability and adherence to SOLID principles. 