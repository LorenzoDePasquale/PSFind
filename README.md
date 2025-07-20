# PSFind

A high-performance PowerShell cmdlet that leverages the NTFS Master File Table (MFT) to quickly search for files and folders across entire drives. 
This tool provides almost instant search capabilities by directly reading the MFT index instead of traversing the file system tree, without requiring a separate index.


## Features

- **Lightning Fast**: Uses MFT (Master File Table) for fast file system searches
- **Flexible Pattern Matching**: Supports glob patterns, regex, and fuzzy search
- **Multi-Drive Support**: Can search across multiple NTFS volumes in parallel
- **Rich Output**: Clickable hyperlinks and colored highlighting in the terminal


## Requirements

- Windows operating system with NTFS drives
- Administrator privileges (required for MFT access)
- .NET 9.0 runtime


## Installation

1. Build the project or download the compiled module
2. Import the module in PowerShell:
   ```powershell
   Import-Module .\PSFind.dll
   ```


## Usage

### Basic Syntax

```powershell
Find-Files [-Name] [-Regex] [-Folders] [-Volume] [-Distance] [-NoStats]
```

### Alias

The cmdlet can also be called using the shorter alias `powershell find`

### Parameters

| Parameter  | Type   | Required | Description                                                                          |
|------------|--------|----------|--------------------------------------------------------------------------------------|
| `Name`     | String | Yes      | Name pattern to search for. Supports glob patterns (`*`, `?`)                        |
| `Regex`    | Switch | No       | Treat the Name parameter as a regular expression                                     |
| `Folders`  | Switch | No       | Search for folders instead of files                                                  |
| `Volume`   | Char   | No       | Restrict search to a specific drive letter (e.g., 'C'). Supports tab auto-completion |
| `Distance` | Byte   | No       | Enable fuzzy search with Levenshtein distance                                        |
| `NoStats`  | Switch | No       | Suppress search statistics (useful for piping)                                       |


## Examples

### Basic File Search

```powershell
# Find all .log files
find *.log

# Find a file with a specific file name
find file.txt

# Find files with wildcard patterns
find setup*.exe

# Find files with single character wildcard
find config.???
```

### Folder Search

```powershell
# Find folders containing "temp"
find *temp* -Folders

# Find folders starting with "Project"
find Project* -Folders
```

### Regex Search

```powershell
# Find files matching a regex pattern
find ".*.(jpg|png|gif)$" -Regex
```

### Fuzzy search with Levenshtein distance

```powershell
# Find files with names similar to "config" (distance of 2)
find config -Distance 2
```

### Volume-Specific Search

```powershell
# Search only on C: drive
find *.dll -Volume C

# Search for folders on D: drive
find *backup* -Folders -Volume D
```
