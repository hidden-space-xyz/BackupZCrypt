# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BackupZCrypt is a cross-platform desktop application (.NET 10, Avalonia) that creates encrypted,
deduplicated backups: files are split into content-defined chunks (FastCDC), optionally compressed
(Zstandard), and each chunk is sealed with an AEAD cipher under keys derived from the user's
password. There is no networking code anywhere in the product.

## Commands

```bash
dotnet build BackupZCrypt.sln                                        # build everything
dotnet run --project BackupZCrypt.Desktop                            # run the desktop app
dotnet test BackupZCrypt.sln                                         # full test suite
dotnet test BackupZCrypt.sln --filter "FullyQualifiedName~ManifestServiceTests"          # one class
dotnet test BackupZCrypt.sln --filter "FullyQualifiedName~ManifestServiceTests.MethodName" # one test
dotnet format whitespace BackupZCrypt.sln --verify-no-changes        # format gate (CI runs exactly this)
```

CI (`.github/workflows/ci.yml`) runs build, test, and the whitespace format check — on pull requests
into `master` only. Nothing runs on `develop` or feature branches, so run the three commands locally
before opening a PR. Use `dotnet format whitespace` (not bare `dotnet format`) to fix formatting:
the style/analyzer passes of bare `dotnet format` are intentionally not part of the gate.

Local builds never produce a distributable; portable packages come only from `release.yml`
(see `.github/workflows/README.md` for the release pipeline and versioning gate).

## Architecture

Clean Architecture + CQRS in the core, MVVM in the UI. Six projects with a strict, inward-pointing
dependency graph.

### Layer dependency table (non-negotiable)

| Project | May reference only |
|---|---|
| `BackupZCrypt.Domain` | nothing |
| `BackupZCrypt.Application` | Domain |
| `BackupZCrypt.Infrastructure` | Domain |
| `BackupZCrypt.Composition` | Application, Infrastructure |
| `BackupZCrypt.Desktop` | Application, Composition |
| `BackupZCrypt.Test` | all of the above |

`BackupZCrypt.Test/Unit/Architecture/LayerDependencyTests.cs` enforces exactly this table by parsing
the `.csproj` files — adding an edge fails the build. The same tests enforce: Domain declares no
runtime NuGet package (BCL only), and no project pins its own package `Version` (all versions live
in `Directory.Packages.props`, central package management).

### What lives where

- **Domain** — enums, constants, strategy/service interfaces, value objects, and the algorithm
  factories that index strategies by their enum `Id`.
- **Application** — CQRS handlers (`Commands/`, `Queries/`), the chunked backup engine
  (`ChunkedBackupService`, split across partial-class files), manifest and password services,
  `Result`/`Result<T>` for error flow.
- **Infrastructure** — concrete strategies: encryption (AES/ChaCha20 via platform APIs, Twofish/
  Serpent/Camellia via BouncyCastle), key derivation (Argon2id/Scrypt/PBKDF2), Zstd compression,
  FastCDC chunking; file-system and settings adapters.
- **Composition** — the single composition root, `DependencyInjection.AddBackupZCryptServices()`.
  Must not reference Avalonia.
- **Desktop** — Avalonia UI, MVVM with CommunityToolkit.Mvvm, compiled bindings by default.
  Desktop-only services (file picker, clipboard) and all ViewModels are registered in
  `App.ConfigureServices` (`App.axaml.cs`), not in Composition.

### CQRS registration is explicit

Every command/query handler is registered as a closed generic interface
(`ICommandHandler<TCommand, TResult>`, `IQueryHandler<...>`, `ISyncQueryHandler<...>`), one line per
message, in `BackupZCrypt.Composition/DependencyInjection.cs`. There is no assembly scanning — the
composition root is deliberately the single honest list of supported messages, and
`StrategyRegistrationTests` covers it. Adding a new command/query means adding its registration
there.

### Strategy pattern

Encryption, key-derivation, and compression strategies are singletons registered against a shared
interface; factories in Domain resolve the `IEnumerable<T>` and index by enum `Id`. Chunking is
deliberately a single strategy: the manifest records no chunker identifier, so a second
implementation would silently break deduplication against existing archives.

### Localization

Lower layers carry no user-facing text. They return `LocalizableMessage` (a `MessageCode` enum
member plus format args); the Desktop layer resolves it through `MessageLocalizer` and
`Resources/Strings.resx` / `Strings.es.resx`. `LocalizationParityTests` fails if a `MessageCode` has
no English key or if the English and Spanish key sets differ — so adding a message means adding the
enum member **and** a key in both resx files.

## On-disk format is pinned

`OnDiskFormatTests` asserts against committed fixture archives and golden key-schedule vectors. The
HKDF sub-key labels, header layout, nonce derivation, and chunk-naming scheme are all part of the
format: an unexpected format-test failure is a data-loss bug in your change, not a test to update.
Only on a **deliberate** format change, regenerate fixtures with the explicit maintenance tool and
treat the diff as the review artefact:

```bash
dotnet test BackupZCrypt.sln --filter "FullyQualifiedName~OnDiskFormatFixtureGenerator"
```

## Conventions

- Every member — including private ones — carries XML documentation; `GenerateDocumentationFile` is
  on and the docs explain *why*, not just *what*. Match this density in new code.
- Analyzers: the built-in .NET analyzers run at `AnalysisLevel=10-recommended` with
  `AnalysisLevelSecurity=10-all` and `EnforceCodeStyleInBuild`; `TreatWarningsAsErrors` is on, so
  any new warning fails the build. `.editorconfig` exists only to suppress diagnostics whose
  correct resolution is to ignore them — each entry carries its justification, and anything
  fixable is fixed in code instead.
- Tests are NUnit + NSubstitute, all in `BackupZCrypt.Test` (`Unit/<Layer>/`, `Integration/`,
  `Tools/`). No test may depend on wall-clock timing, throughput, or locale; every temporary file
  lives in a directory the test owns and deletes (`Common/TempDir`).
- Commit messages follow Conventional Commits; `feat:`, `fix:`, `refactor:` and `bump:` are the
  prefixes release notes are built from.
- Branch from `develop` and open PRs against `develop`; `master` only receives release merges.
  Raising `<Version>` in `Directory.Build.props` is what cuts a release when merged to `master`.
