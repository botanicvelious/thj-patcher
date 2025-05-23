# May 23, 2025

## Trinidy

## UI Improvements

- Reduced all button heights by 10% (from 40px to 36px) to prevent layout issues with CPU optimization checkbox
- Maintained original button widths for consistent visual appearance
- Added "THJ LOG PARSER" button to the sidebar navigation

## Functionality Enhancements

- Added functionality to download and verify CHANGELOG.md file alongside THJLogParser.exe
- Implemented MD5 verification for CHANGELOG.md file
- Added launcher for THJLogParser.exe utility

## Bug Fixes

- Fixed timeout issues when downloading THJLogParser.exe by increasing HTTP client timeout from 100 seconds to 5 minutes
- Added improved error handling for network issues during downloads
- Added more detailed error messages for download failures
- Fixed critical error where download failures could crash the application
- Improved resilience by continuing operation even when network errors occur
- Added global exception handling to ensure log parser updates never interrupt the main program flow

---

# May 22, 2025

## Trinidy

- Added chunk "Fast Patch" feature

---

# April 4, 2025

## Trinidy

- Added "Patcher Changelog"
- Implemented automatic download of patcher changelog during self-updates
- Added error handling for dinput8.dll updates
- Addressed the Optimzations issues
