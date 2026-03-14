# Among Us dumper

An external offset/interface dumper for the Windows version of Among Us, focused on Unity IL2CPP metadata and native exports. Powered by LibCpp2IL.

## Getting Started

You can compile it yourself with the .NET 8 SDK.

This repository currently depends on a local copy of `Cpp2IL-new-analysis`, because current Among Us builds use IL2CPP metadata version `31`, which older public NuGet builds of `LibCpp2IL` do not support.

Before building, make sure this path exists relative to the project:

```text
..\Cpp2IL-new-analysis\LibCpp2IL\LibCpp2IL.csproj
```

## Usage

If the game is running, the dumper can usually auto-detect the installation directory and required files.

If the game is not running, or auto-detection fails, you can pass `--game-dir` or provide the exact file paths manually with `--exe-path`, `--game-assembly`, and `--metadata`.

The dumper reads:

- `Among Us.exe`
- `GameAssembly.dll`
- `UnityPlayer.dll`
- `global-metadata.dat`

It generates:

- native exports (`offsets.*`)
- class field offsets
- static field offsets
- method RVAs
- enum members
- one file per managed assembly, such as `Assembly_CSharp_dll.cs`

By default, generated files are written to the `output` directory next to the executable.

### Examples

```powershell
Among Us dumper.exe
```

```powershell
Among Us dumper.exe --game-dir "D:\GAMES\Among Us"
```

```powershell
Among Us dumper.exe --game-dir "D:\GAMES\Among Us" --unity-version 2022.3.44f1
```

## Available Arguments

- `-f, --file-types <file-types>`: The types of files to generate. Default: `cs, hpp, json, rs, zig`.
- `-i, --indent-size <indent-size>`: The number of spaces to use per indentation level. Default: `4`.
- `-o, --output <output>`: The output directory to write the generated files to. Default: `output` next to the executable.
- `-g, --game-dir <game-dir>`: The Among Us installation directory.
- `-e, --exe-path <exe-path>`: The full path to `Among Us.exe`.
- `-a, --game-assembly <game-assembly>`: The full path to `GameAssembly.dll`.
- `-m, --metadata <metadata>`: The full path to `global-metadata.dat`.
- `-p, --process-name <process-name>`: The running process name to search for. Default: `Among Us`.
- `-u, --unity-version <unity-version>`: Override the detected Unity version.
- `-v`: Increase logging verbosity. Can be specified multiple times.
- `--no-log-file`: Do not create `amongus-dumper.log`.
- `-h, --help`: Print help.
- `-V, --version`: Print version.

## Building

To build a standard release executable, use:

```powershell
dotnet build -c Release
```

The executable will be available at:

```text
bin\Release\net8.0\Among Us dumper.exe
```

To build a single-file self-contained executable, use:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The executable will be available at:

```text
bin\Release\net8.0\win-x64\publish\Among Us dumper.exe
```

To build a framework-dependent executable instead, use:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Running Tests

There is currently no dedicated automated test suite in this repository.

For a basic verification, build the project with:

```powershell
dotnet build
```

## Notes

Among Us uses Unity IL2CPP, so the project extracts equivalent data from `GameAssembly.dll` and `global-metadata.dat` instead.

## License

Licensed under the MIT license.