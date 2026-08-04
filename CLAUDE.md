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
dotnet build BackupZCrypt.sln            # build; analyzers report as warnings on EVERY build, never as errors
dotnet build BackupZCrypt.sln -p:AlwaysReportAnalyzerWarnings=false   # fast build, skips the forced recompile
dotnet run --project BackupZCrypt.Desktop # run the desktop app
dotnet test BackupZCrypt.sln             # run the full test suite
dotnet format BackupZCrypt.sln           # auto-format to .editorconfig

# Run a single test class or method (NUnit filter):
dotnet test --filter "FullyQualifiedName~BackupRestoreRoundtripTests"
dotnet test --filter "Name=EveryMessageCode_HasEnglishResxKey"
```

**Building never produces a distributable.** No configuration — Debug or Release — publishes
anything; there is no packaging MSBuild target in `BackupZCrypt.Desktop.csproj`. The self-contained
single-file bundles for `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64` are produced **only** by
the `build` job of [`.github/workflows/release.yml`](.github/workflows/release.yml), on a push to
`master` that raises `<Version>`. Keep it that way: packaging belongs to the release pipeline, not to
a local build.

## Architecture — Clean Architecture, dependencies point inward

```
Desktop ──> Composition ──> Application ──> Domain
   │             │                             ▲
   └─────────────┴──> Infrastructure ──────────┘
```

Every arrow points inward and there are no others. `Domain` is the centre and depends on nothing.

| Project | Role | May reference |
|---|---|---|
| `BackupZCrypt.Domain` | Enums, constants, value objects, and **interfaces** (strategies, services, factories). Three factory *implementations* also live here (`CompressionServiceFactory`, `EncryptionServiceFactory`, `KeyDerivationServiceFactory`). | **BCL only** — no other project, no runtime NuGet package |
| `BackupZCrypt.Application` | Use-case orchestration and business services, validators, `Result`/`Result<T>`, manifest value objects. | Domain |
| `BackupZCrypt.Infrastructure` | Concrete encryption / KDF / compression / chunking strategies + file, storage, and settings-persistence services. | Domain (+ BouncyCastle, ZstdSharp.Port) |
| `BackupZCrypt.Composition` | DI composition root wiring contracts → implementations. | Application, Infrastructure, Domain |
| `BackupZCrypt.Desktop` | Avalonia MVVM UI. **Owns all localized text.** | Application, Composition, Domain (+ Avalonia, CommunityToolkit.Mvvm) |
| `BackupZCrypt.Test` | NUnit + NSubstitute suite. | all of the above |

Non-negotiable boundary rules:
- **Domain references nothing** — no other project, and no NuGet package that ships anything. The
  Meziantou and Sonar references it inherits from `Directory.Build.props` are build-time analyzers
  (`PrivateAssets=all`) and contribute no runtime dependency.
- Concrete implementations are `internal sealed`, exposed to `Composition` and `Test` via
  `InternalsVisibleTo`. **Never `new` an implementation across a layer** — register it in
  `BackupZCrypt.Composition/DependencyInjection.cs` and resolve it through DI. The one deliberate
  exception is `App.ConfigureServices`, which registers the Desktop-only platform services and the
  ViewModels: `Composition` cannot reference Avalonia without inverting the dependency arrow.
- `BackupZCrypt.Desktop` declares **no public types at all** — it is a leaf application, so its
  ViewModels, Views, and models are `internal`, matching how the inner layers declare theirs.
- **No user-facing English/Spanish text below the Desktop layer** (see Localization below).

`LayerDependencyTests` enforces the table above by parsing the project files, so an illegal
reference fails `dotnet test` even before anything uses it.

## Cross-cutting patterns (read these before adding code)

**Strategy + factory selection.** Every encryption / KDF / compression algorithm is a strategy
carrying an enum `Id`. All implementations are registered as singletons against the same interface
(`services.AddSingleton<IEncryptionAlgorithmStrategy, ...>()`), then consumers inject the full
`IEnumerable<T>` and index it by `Id` into a dictionary. **To add an algorithm:** add an enum member,
implement an `internal sealed` strategy in Infrastructure, and register it in
`DependencyInjection.AddBackupZCryptServices`. Nothing else selects strategies.

**Chunking is the exception and must stay one implementation.** `IChunkingStrategy` carries no `Id`,
there is no `ChunkingAlgorithm` enum, and — critically — the manifest preamble records **no chunker
identifier**. A second chunking strategy would therefore change chunk boundaries with nothing on disk
to say which one produced them, destroying deduplication against every archive already written.
`StrategyRegistrationTests` asserts exactly one registration.

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
- `LocalizationParityTests` enforces this: every `MessageCode` must have an English key, the en/es
  key sets must be identical, and the English file must hold exactly the keys the application asks
  for — no missing entries and no orphans. Both `Strings.Get` and `Strings.GetByKey` fall back to
  returning the key itself, so a missing entry ships a raw identifier as visible UI text rather than
  failing; these tests are what turns that into a red `dotnet test`.

**Request flow.** A Desktop ViewModel builds a `BackupRequest` and calls
`IBackupOrchestrator.ExecuteAsync`. Only **Create** chooses its algorithms; restore, update, and
verify read the cipher and KDF from the manifest preamble and the compression mode from the manifest
header, because anything else would derive the wrong key. Build those with
`BackupRequest.ForRestore` / `.ForUpdate` / `.ForVerify` rather than passing values the operation
discards. The orchestrator validates (blocking errors vs. advisory
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
- **Entry paths.** A manifest entry path is portable data, not a host path: it is always written with
  `/` separators (`ToManifestPath`) and translated back to the host separator on restore
  (`ToPlatformPath`), so a Windows-written archive rebuilds the same tree on Unix. Both separators are
  recognized on every platform, which also keeps traversal detection platform-independent — a crafted
  `..\..\escape` is rejected on Unix, where `\` is otherwise a legal file-name character.
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
- **Analyzers report, they do not gate.** Every project gets `AnalysisMode=All`,
  `EnforceCodeStyleInBuild`, `MeziantouAnalysisMode=all-warnings`, `GenerateDocumentationFile`,
  **Meziantou.Analyzer** and **SonarAnalyzer.CSharp** — do not re-declare these per project. Violations
  are **warnings, never errors**, by explicit configuration, and they are **re-reported on every build**,
  including an up-to-date one (see below). The `.editorconfig` is strict (file-scoped namespaces, `var`
  always, explicit accessibility modifiers, private fields without an underscore prefix, `I`-prefixed
  interfaces, braces always, 140-col lines). `BackupZCrypt.Test` **no longer opts out** — it is analysed
  like everything else, with the NUnit-specific conflicts (`CA1707`, `MA0053`, fixture credentials,
  golden-vector duplication, `CS1591`) scoped off in its own `.editorconfig` section.
- **The analyzer configuration is shared with the `CodigoActivo` repository and must stay
  byte-identical.** Four artefacts: `Directory.Build.Analyzers.props` (all the MSBuild properties and
  the `SonarLint.xml` wiring), `Directory.Build.targets` (the always-report sentinel), `SonarLint.xml`
  (thresholds for Sonar's parameterized rules — **the only place they can be set**), and the single
  `[*.cs]` section at **lines 3–176** of `.editorconfig`. Their counterparts live under
  `CodigoActivo/backend/`, and the shared `.editorconfig` range sits at the same line numbers there so
  it can be compared mechanically. **Edit one repo without the other and they drift:**

  ```bash
  cd D:/WorkSpace && B=BackupZCrypt && C=CodigoActivo/backend
  for f in Directory.Build.Analyzers.props Directory.Build.targets SonarLint.xml; do diff "$C/$f" "$B/$f"; done
  diff <(sed -n '3,176p' "$C/.editorconfig") <(sed -n '3,176p' "$B/.editorconfig")
  ```

  Repo-local deviations go in the `.editorconfig` sections **after** line 176, never inside the shared
  range — and prefer fixing the code over adding a suppression. **None of the four files carries
  comments**: the rationale for a rule or a suppression belongs in this document, not inline.
- **Warnings print on every build.** MSBuild normally skips `CoreCompile` when a project is up to date,
  so the analyzers never run and a warm `dotnet build` reports `0 Warning(s)` with live violations
  present — which also made the old `-warnaserror` CI gate produce false greens over a warm `obj/`.
  `Directory.Build.targets` fixes that with an output file that is never created. Every build therefore
  pays for a full compile+analysis pass; opt out with `-p:AlwaysReportAnalyzerWarnings=false`.
- **Sonar's parameterized rules need two independent things**: a severity in `.editorconfig` *and* a
  threshold in `SonarLint.xml`. Most of them ship **disabled**, so an entry in `SonarLint.xml` alone is
  a silent no-op — which is exactly what happened before: `SonarLint.xml` did not exist, S103 ran at its
  built-in 200 rather than the documented 140, and S3776/S107/S1541 did not run at all.
- **Docs.** Every non-`private` member in the five src projects has an XML doc comment — with
  `GenerateDocumentationFile` on, a missing one is a CS1591 build warning. The test project is exempt
  (it is not an API surface). Update `README.md` when user-facing behavior changes.
- **Tests.** NUnit + NSubstitute; test methods use `Method_Scenario_Expected` naming.
  `Test/Common/TestHost.cs` builds a real DI provider
  (`AddBackupZCryptServices()`) — integration tests exercise the real
  crypto/chunking stack against temp directories rather than mocking it.
- **The on-disk format is pinned, and that is load-bearing.** `Test/Unit/Format/OnDiskFormatTests.cs`
  restores committed fixture archives written by an earlier build and asserts golden vectors for each
  KDF, the four HKDF sub-key labels, the chunk nonce, the AEAD associated data, and the chunk file
  name. **If one of these fails, assume a change broke the format, not that the test is stale** — a
  format change makes every archive a user already wrote permanently unreadable. The fixtures are
  regenerated only by the `[Explicit]` `Test/Tools/OnDiskFormatFixtureGenerator.cs`, and only on a
  deliberate format change.
- **Settings** persist as indented JSON under `%LocalAppData%/BackupZCrypt` (platform equivalent
  elsewhere), one file per settings type; defaults are recreated if a file is missing or corrupt.
- **Commits**: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
  Feature branches off `develop`; PRs target `master` only for releases. Commit or push **only when
  asked**.
