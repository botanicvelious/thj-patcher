# THJ Patcher

A custom patcher for The Heroes Journey EverQuest server using WPF. This application maintains the game client by downloading updates, checking file integrity, and providing optimization tools.

## Application Structure

The codebase is divided into several specialized services:

### Services

#### PatcherService (`Utilities/PatcherService.cs`)
- Downloads and patches game files
- Handles locked DLLs
- Manages file deletions
- Implements download retry logic

#### FileSystemService (`Utilities/FileSystemService.cs`)
- Performs file integrity scanning
- Detects missing or modified files
- Handles critical files (dinput8.dll)
- Maintains file download queue
- Builds file lists for patching

#### OptimizationService (`Utilities/OptimizationService.cs`)
- Applies 4GB patch to EverQuest executable
- Fixes UI scale issues
- Optimizes graphics settings
- Clears game caches
- Resets game settings

#### GameLaunchService (`Utilities/GameLaunchService.cs`)
- Launches EverQuest
- Manages game processes
- Checks for running instances
- Handles silent mode operations

#### SelfUpdateService (`Utilities/SelfUpdateService.cs`)
- Checks for patcher updates
- Downloads updated executables
- Manages version comparisons
- Performs file hash checks

#### NavigationService (`Utilities/NavigationService.cs`)
- Manages UI panel navigation
- Opens external resources (folders, URLs)

#### ChangelogService (`Utilities/ChangelogService.cs`)
- Fetches changelogs from server
- Formats changelog content
- Tracks new entries
- Displays changelog data

#### InitializationService (`Utilities/InitializationService.cs`)
- Manages startup sequence
- Cleans outdated files
- Configures system components
- Displays welcome messages

#### ClientVersionService (`Utilities/ClientVersionService.cs`)
- Handles version detection
- Performs compatibility checks
- Maintains version information

#### CommandLineService (`Utilities/CommandLineService.cs`)
- Parses command line arguments
- Configures runtime options
- Provides access to parameters

### Models

- `FileList` & `FileEntry` - File download information
- `ChangelogInfo` - Changelog data structure
- `LoadingMessages` - Random loading messages
- `ServerStatus` - Server status information

### Main UI

- `MainWindow.xaml.cs` - Primary UI controller
- Uses services to coordinate application functionality

## Development Tasks

1. Create interfaces for services
2. Extract additional specialized services
3. Implement dependency injection
4. Add unit tests
5. Replace MD5 with SHA-256 