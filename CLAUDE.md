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

`ChunkedBackupService` is one `internal sealed partial class` spread over four files, split purely to
get under S104's 1200-line limit — the seams are organizational, not architectural, and no behavior
moved with them: `ChunkedBackupService.cs` (create/update entry points and orchestration),
`.Keys.cs` (salt generation and the HKDF sub-key derivation, `DerivedKeySet`/`ChunkCipherSet`),
`.Chunks.cs` (chunking, per-chunk compress-then-encrypt, content-addressed store), and `.Restore.cs`
(restore and verify). A method belongs in the file matching its role; adding a fifth file is fine,
but do not let a partial split stand in for the extract-class redesign S1200 actually asks for.

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
  `ManifestPathPolicy.ValidateRelative` and restore paths confined by `ManifestPathPolicy.ResolveSafeDestination`
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
- **Rules scoped off, and why.** Each of these was triaged as unsatisfiable in code, not merely noisy;
  the rest of the analyzer set was fixed rather than suppressed. Do not re-enable one without
  addressing the reason. In the repo-local `[*.cs]` section:
  - `MA0181` (explicit casts) ships **disabled** upstream precisely because casts are sometimes
    necessary, and is live here only through `MeziantouAnalysisMode=all-warnings`. Every site is a
    value conversion (`(int)duration.TotalSeconds`, `(double)ProcessedFiles / TotalFiles`), an
    `enum`↔`byte` conversion that **is** the pinned preamble encoding (`ManifestService`, plus the
    golden vectors in `OnDiskFormatTests` and `ChunkedBackupSecurityTests`), `(byte[])Key.Clone()`, a
    target-typed collection expression, or `(Control)Activator.CreateInstance(type)!` in `ViewLocator`.
    The suggested `as` / `is` replacements do not apply to numeric or enum conversions, and at the
    reference sites would trade a hard failure for a silent `null`.
  - `MA0182` (type never used) is a false positive manufactured by the mandated architecture: 24 of the
    33 sites are `internal sealed` implementations whose only use site is `DependencyInjection.cs` in
    another assembly via `InternalsVisibleTo`, and the other 9 are Desktop types Roslyn cannot see
    referenced — the `x:Class` code-behind classes, `ViewLocator` (instantiated from `App.axaml`, and
    itself resolving views by `Type.GetType`), and `PasswordConverters` bound from markup. Acting on it
    deletes working code and breaks the app.
  - `MA0109` (consider a `Span`/`Memory` overload) is advisory, and every site is a `byte[]` contract at
    a layer boundary: `IEncryptionAlgorithmStrategy`, `IKeyDerivationAlgorithmStrategy`,
    `IManifestService`, `IFileOperationsService`, `ManifestPreamble`. Satisfying it widens the public
    Domain and Application surface and adds overloads to five cipher and three KDF strategies — an API
    design change on the crypto path whose `byte[]` shape the `CryptographicOperations.ZeroMemory`
    wiping and the pinned format code are built around.
  - `MA0173` (`LazyInitializer.EnsureInitialized`) pattern-matches the CAS shape without seeing intent.
    All four sites (`ChunkedBackupService` L204, L485, L686, L707) are one idiom —
    `Interlocked.CompareExchange(ref fatalError, …, null)` followed by `linkedCts.CancelAsync()` — a
    first-writer-wins fatal-error latch shared across `Parallel.ForEachAsync` workers, not lazy
    initialization: the return value is deliberately discarded and the message is built eagerly from the
    caught exception. The rewrite would allocate a closure per fault, add an unsynchronized fast-path
    read, and rename the latch after an API that means something else.
  - `MA0155` (`async void`) has one occurrence, `OperationStatusView.axaml.cs` L87, whose signature is
    fixed by the Avalonia `PropertyChanged` delegate — an event handler cannot return `Task`. Its
    `<remarks>` already records why the fault is swallowed: escaping it would be rethrown on the
    dispatcher and kill the process mid-backup. Fire-and-forget trades it for an unobserved exception
    plus `MA0042`/`CA2012`, and moving dialog ownership into the ViewModel is a redesign.
  - `MA0104` fires on `Domain/Enums/CompressionMode.cs` colliding **by name only** with
    `System.IO.Compression.CompressionMode`, which no file in the solution imports, so there is no
    ambiguity to resolve. The rename reaches 131 references across 39 files — the public Domain API, the
    encrypted manifest payload types, the persisted `BackupCreationSettings` JSON, the Desktop options
    and ViewModels, and 20 test files including `OnDiskFormatFixtures` — and degrades the domain
    vocabulary, since `CompressionMode` is exactly the right name for "which Zstd level, or none".
  - `MA0175`/`MA0174` and `MA0157`/`MA0156` are **mutually exclusive pairs**. Each pair ships disabled
    upstream and both halves go live together under `MeziantouAnalysisMode=all-warnings`, so no source
    text satisfies both and exactly one of each must be scoped off — silencing a rule here buys the
    other one's enforcement, it does not lose coverage. `MA0174` wants `record class`, `MA0175` wants
    bare `record`: `MA0175` is off, so the 25 record declarations carry the explicit `class` keyword,
    matching the `.editorconfig`'s explicit-accessibility house style. `MA0156` wants the `Async`
    suffix on an `IAsyncEnumerable<T>`-returning method, `MA0157` forbids it: `MA0157` is off, so
    `IChunkingStrategy.ChunkAsync` and `FastCdcChunkingStrategy.ChunkCoreAsync` keep the suffix every
    other asynchronous member in the solution uses. Flipping either choice is a mechanical rename plus
    swapping which id is `none`; do not change one without the other.
  - `S1200` (class coupling ≤ 30) targets the two classes that are high-coupling **by mandate**:
    `DependencyInjection`, the composition root whose job is to name all 41 concrete types, and
    `ChunkedBackupService`, whose 73 dependencies are dominated by the BCL primitives a file-format
    engine cannot avoid (`byte[]`, `Stream`, `SHA256`, `HMACSHA256`, `IncrementalHash`, `ArrayPool`,
    `Concurrent*`, seven exception types) plus the manifest value objects. Note that S1200 counts per
    partial **declaration**, not per class symbol — splitting either type across partial files silences
    it without removing one dependency, which is exactly why that must not be the remedy; getting under
    30 needs a real extract-class redesign of the pinned-format crypto path. `ChunkedBackupService` is
    now four partial files (see below), so this rule would fall silent on its own — the explicit `none`
    is what keeps that from reading as a fixed coupling problem.
  - `IDE0051` fires on `BackupRequest.PrintMembers`, which is not dead code but the record's
    compiler-recognized hook, hand-written so the generated `ToString()` prints
    `Password = ***, ConfirmPassword = ***`. It is invoked only from generated code the rule does not
    analyse; deleting it puts the user's password into every log line, exception message, and debugger
    watch that formats a request, and the only alternative is unsealing a Domain value object so the
    hook becomes `protected virtual`. Re-scope this one to `BackupRequest.cs` if genuine unused private
    members elsewhere need to warn again.

  In the `[BackupZCrypt.Test/**/*.cs]` section:
  - `MA0191` fires on the three argument-validation tests that pass `null!` to a non-nullable parameter
    to assert the guard fires (`BackupBenchmarkServiceTests` L118, `FastCdcChunkingTests` L305,
    `KeyDerivationStrategyTests` L83). The `!` is not optional — dropping it just trades `MA0191` for
    `CS8625`, which is also on.
  - `CA5394` (insecure randomness) has no site in the five src projects; all five are `new Random(seed)`
    in private test-data helpers whose whole purpose is a reproducible byte sequence, which the pinned
    FastCDC cut-point vectors and the compression-ratio assertions depend on. `RandomNumberGenerator`
    cannot be seeded, and every real key, salt, and nonce already comes from `RandomNumberGenerator.Fill`
    in Infrastructure. Scoped to the test section so a `new Random` in src still warns.
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
