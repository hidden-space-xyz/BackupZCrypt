# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**BackupZCrypt** is a cross-platform desktop app (.NET 10, Avalonia 12 / MVVM) that produces
**chunk-based encrypted backups**. Files are split into content-defined chunks (FastCDC),
deduplicated by content hash, optionally compressed (Zstandard), and individually encrypted with
authenticated encryption under a key derived from the user's password.

This is **security-critical** software handling passwords, key material, and the only copy of a
user's encrypted data. **There is no password recovery** — a bug can cause irreversible data loss.
When a change forces a trade-off, resolve it **security first, performance second, everything else
after**. Never weaken a cryptographic guarantee or leak key material for convenience.

## Commands

```bash
dotnet build BackupZCrypt.sln            # build (analyzers run during build; keep warnings at zero)
dotnet run --project BackupZCrypt.Desktop # run the desktop app
dotnet test BackupZCrypt.sln             # run the full test suite
dotnet format BackupZCrypt.sln           # auto-format to .editorconfig

# Run a single test class or method (NUnit filter):
dotnet test --filter "FullyQualifiedName~BackupRestoreRoundtripTests"
dotnet test --filter "Name=EveryMessageCode_HasEnglishResxKey"
```

A **Release** build of `BackupZCrypt.Desktop` additionally runs the `GeneratePortablePackages`
MSBuild target, which `dotnet publish`es self-contained single-file bundles for `win-x64`,
`linux-x64`, `osx-x64`, and `osx-arm64` into `dist/`. This is slow — use Debug for normal iteration.

## Architecture — Clean Architecture, dependencies point inward

```
Desktop ─┐
         ├─> Composition ─> Application ─> Domain
Infra ───┘        (Composition also ─> Infrastructure ─> Domain)
```

| Project | Role | May reference |
|---|---|---|
| `BackupZCrypt.Domain` | Enums, constants, value objects, and **interfaces** (strategies, services, factories). Two factory *implementations* also live here (`CompressionServiceFactory`, `KeyDerivationServiceFactory`). | **BCL only** — no NuGet, no other project |
| `BackupZCrypt.Application` | Use-case orchestration and business services, validators, `Result`/`Result<T>`, manifest value objects. | Domain |
| `BackupZCrypt.Infrastructure` | Concrete encryption / KDF / compression / chunking strategies + file & storage services. | Domain (+ BouncyCastle, ZstdSharp.Port) |
| `BackupZCrypt.Composition` | DI composition root wiring contracts → implementations. | Application, Infrastructure |
| `BackupZCrypt.Desktop` | Avalonia MVVM UI. **Owns all localized text.** | Application, Composition (+ Avalonia, CommunityToolkit.Mvvm) |
| `BackupZCrypt.Test` | NUnit + NSubstitute suite. | all of the above |

Non-negotiable boundary rules:
- **Domain references nothing** — no NuGet package, no other project (BCL only).
- Concrete implementations are `internal sealed`, exposed to `Composition` and `Test` via
  `InternalsVisibleTo`. **Never `new` an implementation across a layer** — register it in
  `BackupZCrypt.Composition/DependencyInjection.cs` and resolve it through DI.
- **No user-facing English/Spanish text below the Desktop layer** (see Localization below).

## Cross-cutting patterns (read these before adding code)

**Strategy + factory selection.** Every encryption / KDF / compression / chunking algorithm is a
strategy carrying an enum `Id`. All implementations are registered as singletons against the same
interface (`services.AddSingleton<IEncryptionAlgorithmStrategy, ...>()`), then consumers inject the
full `IEnumerable<T>` and index it by `Id` into a dictionary. **To add an algorithm:** add an enum
member, implement an `internal sealed` strategy in Infrastructure, and register it in
`DependencyInjection.AddDomainServices`. Nothing else selects strategies.

**Result over exceptions across layers.** Application operations return `Result` / `Result<T>`
(in `BackupZCrypt.Application/ValueObjects/`) carrying `LocalizableMessage` errors — they do not
throw across layer boundaries. Implicit conversions make this terse: returning a `T` yields success,
returning a `MessageCode` yields failure. Exceptions inside the engine are caught and mapped to
`MessageCode.UnexpectedErrorFormat` (fatal) or per-file error messages.

**Localization via message codes.** Lower layers never produce translated strings. They emit a
`MessageCode` (enum) wrapped in `LocalizableMessage` (code + format args). The Desktop
`MessageLocalizer` resolves the code to a resx key **of the same name** in `Strings.resx`. Codes
whose name ends in `Format` take `string.Format` arguments.
- Adding or changing a `MessageCode` requires the matching key in **both**
  `BackupZCrypt.Desktop/Resources/Strings.resx` (en) and `Strings.es.resx` (es).
- `LocalizationParityTests` enforces this: every `MessageCode` must have an English key, and the
  en/es key sets must be identical. These tests fail the build if you forget a translation.

**Request flow.** A Desktop ViewModel builds a `BackupRequest` and calls
`IBackupOrchestrator.ExecuteAsync`. The orchestrator validates (blocking errors vs. advisory
warnings gated by `ProceedOnWarnings`), normalizes paths, prepares the destination, then dispatches
by `BackupOperation` to `IChunkedBackupService.{Create,Update,Restore,Verify}Async`. Each op
processes files in parallel (`Parallel.ForEachAsync`, DOP = `ProcessorCount`) reporting through
`IProgress<BackupStatus>`. Verify is read-only — it reconstructs into `Stream.Null`.

## Cryptographic design (do not change casually)

`ChunkedBackupService` and `ManifestService` implement the on-disk format. Constants live in
`BackupZCrypt.Domain/Constants/EncryptionConstants.cs` (256-bit keys, 32-byte salt, 12-byte nonce,
128-bit tag).

- **Key hierarchy.** `password + 32-byte random salt` → 256-bit master key via the chosen KDF
  (Argon2id / Scrypt / PBKDF2). `HKDF.Expand(SHA-256)` then derives four purpose-bound sub-keys from
  the master with the context labels `chunk-encryption`, `chunk-nonce`, `chunk-naming`,
  `manifest-encryption`.
- **Chunks.** Content-defined (FastCDC), deduplicated by SHA-256 content hash. Per-chunk
  nonce = `HMAC-SHA256(chunkNonceKey, chunkHash)[:12]` (deterministic, so identical content
  dedupes, but key-dependent). On-disk chunk filename = `HMAC-SHA256(namingKey, chunkHash)` hex +
  `.bzc`, so filenames never leak the content hash. Optional Zstd compression is applied **before**
  encryption. AEAD associated data = `chunkHash ‖ nonce`.
- **Manifest.** `manifest.bzc` = unencrypted 34-byte preamble (`algo` byte, `kdf` byte, 32-byte
  salt) + 12-byte nonce + AEAD-encrypted JSON document. The preamble is the AEAD associated data;
  the master salt is echoed inside the encrypted document and `FixedTimeEquals`-checked against the
  preamble to detect tampering. Written atomically (temp file + rename).
- **Hardening already in place** — preserve it: key material is wiped with
  `CryptographicOperations.ZeroMemory`, hashes/salts compared with
  `CryptographicOperations.FixedTimeEquals`, manifest paths validated by
  `ValidateRelativeManifestPath` and restore paths confined by `ResolveSafeDestinationPath`
  (no traversal / no escaping the destination), and decompression is bounded to the manifest's
  declared size. Restore and verify re-check each file's total size and SHA-256 against the manifest.

## Conventions & build gates

- **Central package management** — all versions live in `Directory.Packages.props`; project files
  reference packages without versions. Keep packages on their latest stable compatible versions.
- **Analyzers are gates.** Every project sets `AnalysisMode=All` and includes Roslynator; the
  `.editorconfig` is strict (file-scoped namespaces, `var` always, explicit accessibility modifiers,
  underscore-prefixed private fields, trailing commas, `I`-prefixed interfaces, braces always,
  140-col lines). A clean change adds **zero** new analyzer/format warnings.
- **Docs.** Every non-`private` member has an XML doc comment (the existing code is thorough — match
  it). Update `README.md` when user-facing behavior or usage changes.
- **Tests.** NUnit + NSubstitute; test methods use `Method_Scenario_Expected` naming (the naming
  analyzer intentionally excludes methods to allow this). `Test/Common/TestHost.cs` builds a real DI
  provider (`AddDomainServices().AddApplicationServices()`) — integration tests exercise the real
  crypto/chunking stack against temp directories rather than mocking it.
- **Settings** persist as indented JSON under `%LocalAppData%/BackupZCrypt` (platform equivalent
  elsewhere), one file per settings type; defaults are recreated if a file is missing or corrupt.
- **Commits**: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
  Feature branches off `develop`; PRs target `master` only for releases. Commit or push **only when
  asked**.
