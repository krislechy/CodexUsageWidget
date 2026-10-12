# Package size

The v0.2.0 executable was 172,979,175 bytes (164.97 MiB). It was a self-contained, uncompressed single-file .NET 10 Windows desktop deployment. The embedded runtime, WPF, Windows Forms tray support and framework libraries dominate the executable; the widget's own compiled assemblies are a small part of it. Codex CLI and browser engines are not bundled.

Version 0.3.0 offers two local packages:

Measured Windows x64 Release output:

| Package | Executable | ZIP download |
| --- | ---: | ---: |
| v0.2.0 Portable, uncompressed executable | 164.97 MiB | 73.94 MiB |
| v0.3.0 Portable, compressed executable | 71.96 MiB | approximately 66.77 MiB |
| v0.3.0 Lite, external desktop runtime | 0.41 MiB | approximately 0.34 MiB |

The v0.3.0 application/core assemblies total approximately 0.23 MiB before bundling. ZIP values can vary slightly with documentation and screenshot updates. Both packaged executables passed the actual WPF preview/state checks on the development machine. The complete preview run took 4.33 seconds for Portable and 3.81 seconds for Lite in one smoke check; these include intentional animation/layout waits and PNG capture, so they are not startup benchmarks.

- **Portable** includes the runtime and uses single-file compression. It runs without installing .NET. Compression reduces disk size but adds decompression work at startup; it does not eliminate the framework's memory usage.
- **Lite** uses the installed **Microsoft .NET 10 Desktop Runtime, Windows x64**. The download is much smaller because it shares the system runtime. The plain .NET Runtime and ASP.NET Runtime do not supply WPF. Install the Desktop Runtime from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

Both packages include only user documentation and the English main/compact README images. Developer mockups, all-language QA captures and source code remain in the repository. WPF trimming is deliberately not enabled: removing dynamically accessed types can break XAML bindings and templates.

Build locally with `./scripts/publish.ps1 -Package Both`. Packaging never publishes to GitHub. See [Microsoft single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) for runtime inclusion, framework-dependent deployment and compression tradeoffs.
