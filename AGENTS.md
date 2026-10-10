# Repository Guidelines

## Project Structure & Module Organization

`BackupZCrypt.sln` contains six .NET 10 projects. `BackupZCrypt.Domain` defines contracts, enums, factories, and value objects; `BackupZCrypt.Application` contains commands, queries, validators, and backup orchestration; `BackupZCrypt.Infrastructure` implements cryptography, compression, chunking, settings, and file access. Dependency injection lives in `BackupZCrypt.Composition`. Project references point inward only: Domain references no project, Application and Infrastructure reference only Domain, Composition references Application and Infrastructure, Desktop references Application and Composition, and the test project references all five; `LayerDependencyTests` enforces these rules. The Avalonia/MVVM client is in `BackupZCrypt.Desktop`, with UI code split among `Views`, `ViewModels`, `Services`, and `Resources`. Tests are centralized in `BackupZCrypt.Test/Unit` and `BackupZCrypt.Test/Integration`; shared fixtures belong in `Common`. Committed on-disk format archives live in `BackupZCrypt.Test/TestData/OnDiskFormatArchives`; `BackupZCrypt.Test/Tools` holds their shared definitions and the explicit-only generator that rewrites them. Treat `dist`, `bin`, and `obj` as generated output.

## Build, Test, and Development Commands

- `dotnet restore BackupZCrypt.sln` restores centrally versioned NuGet packages.
- `dotnet build BackupZCrypt.sln` compiles every project; analyzers, code style, nullable checks, and warnings-as-errors are enabled.
- `dotnet run --project BackupZCrypt.Desktop` launches the desktop application locally.
- `dotnet test BackupZCrypt.sln` runs the complete xUnit v3 suite.
- `dotnet format whitespace BackupZCrypt.sln --verify-no-changes` verifies formatting before review.
- `dotnet run --file scripts/render-icon.cs` regenerates `BackupZCrypt.png` and `BackupZCrypt.ico` from `BackupZCrypt.Desktop/Resources/BackupZCrypt.svg`; edit the SVG, not the rasters. The sidebar logo is the `AppLogoImage` drawing in `App.axaml` and must be kept in step with the SVG by hand.

Use the .NET 10 SDK or newer. Publishing and distributable packaging are handled by the release workflow, not normal builds.

## Coding Style & Naming Conventions

Follow standard C# formatting with four-space indentation, file-scoped organization where already used, nullable annotations, and implicit usings. Use PascalCase for types, methods, properties, and public members; camelCase for parameters and locals; prefix interfaces with `I`. Keep filenames aligned with their primary type (`ManifestService.cs`) and use descriptive suffixes such as `*CommandHandler`, `*QueryHandler`, `*Strategy`, and `*ViewModel`. Add XML documentation to new public APIs. Do not suppress analyzer findings without a narrow, documented reason.

## Testing Guidelines

Use xUnit v3 and NSubstitute. Name test classes `TypeNameTests` and test methods after observable behavior. Place isolated tests under the matching `Unit/<Layer>` folder and filesystem or end-to-end scenarios under `Integration`. Tests must be deterministic, locale-independent, and own and clean up temporary files. CI requires at least 90% line, branch, and method coverage of the `Application`, `Composition`, `Domain`, and `Infrastructure` assemblies (see `BackupZCrypt.Test/CodeCoverage.config` and the local gate in `README.md`); cover new behavior, failure paths, archive compatibility, and security-sensitive edge cases. Regenerate the format fixtures only for a deliberate format change with `dotnet run --project BackupZCrypt.Test -- -explicit only -class "*OnDiskFormatFixtureGenerator"`, and review the resulting diff.

## Commit & Pull Request Guidelines

Use Conventional Commits, consistent with history: `feat:`, `fix:`, `perf:`, `refactor:`, `chore:`, `ci:`, or `bump:` for dependency updates. Mark breaking changes with `!` in the subject; the release workflow reads only commit subjects, so a `BREAKING CHANGE:` footer alone does not bump the major version. Only breaking, `feat:`, `fix:`, and `perf:` commits publish a release. Branch from `develop` and open pull requests against `develop`; `master` is reserved for releases. PRs should explain intent and risk, link relevant issues, list verification commands, and include screenshots for Avalonia UI changes. Run build, tests, and the whitespace check before requesting review.

## Security & Configuration

Never commit passwords, keys, plaintext backup data, or user settings. Preserve archive-format compatibility and path-confinement checks; cryptography, manifest, and restore changes require focused regression tests. Update shared package versions only in `Directory.Packages.props`.
