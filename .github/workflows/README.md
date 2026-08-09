# Workflows

Three workflows cover the whole path from a pull request to a published release. None of them runs
on `develop` or on feature branches: everything is verified on the way into `master`, and `master` is
what publishes.

| Workflow | Runs on | Purpose |
| --- | --- | --- |
| [`ci.yml`](ci.yml) | pull requests targeting `master`, manual dispatch | Build, test and format gate |
| [`codeql.yml`](codeql.yml) | pushes to `master`, pull requests targeting `master`, weekly | CodeQL code scanning |
| [`release.yml`](release.yml) | pushes to `master`, manual dispatch | Publish the declared version as a GitHub Release |

`ci.yml` and `release.yml` pin `DOTNET_VERSION` to `10.0.x`, restore the `~/.nuget/packages` cache
keyed on `Directory.Packages.props` plus every `*.csproj`, and check out with
`persist-credentials: false` so the checkout token is not left in `.git/config` for later steps to
reuse. `codeql.yml` needs none of that — see below.

## `ci.yml` — build and test

Four steps, in order, all against the Debug configuration:

1. `dotnet restore`.
2. `dotnet build`. The built-in .NET analyzers (`AnalysisLevel=10-recommended`,
   `AnalysisLevelSecurity=10-all`, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`) run with
   `TreatWarningsAsErrors` set in `Directory.Build.props`, so any analyzer or compiler
   warning fails the build. The switch lives in project configuration rather than on the command
   line on purpose: every build — local or CI, cold or incremental — applies it identically, which
   a `-warnaserror` CLI flag could not guarantee.
3. `dotnet test --no-build`.
4. `dotnet format whitespace --verify-no-changes`. Scoped to the whitespace pass on purpose: a bare
   `dotnet format` also runs the style and analyzer passes, which duplicate what step 2 already
   enforces through the build.

The zero-warning gate is therefore the build itself, in CI and locally alike.

Debug is deliberate: the pull-request gate only has to prove the code builds clean and the suite is
green. Nothing here produces a distributable binary — packaging happens exclusively in `release.yml`.

There is no `push` trigger. Pushing to `develop` or to a feature branch runs nothing, so the gate
fires once per release pull request rather than once per commit. Use the manual dispatch to run the
same checks against an arbitrary branch.

## `codeql.yml` — code scanning

GitHub's advanced-setup template, kept close to its generated form so it stays cheap to diff against
upstream when the template moves. Two languages are analysed in a matrix — `csharp` for the product
and `actions` for these workflow files themselves — with `fail-fast: false`, so a finding in one does
not hide the other's result.

Both run with `build-mode: none`: CodeQL extracts the C# sources directly instead of observing a
compilation. That is what the earlier default setup already did, and it is why this workflow needs no
SDK, no restore and no NuGet cache — the build is already gated by `ci.yml`, and repeating it here
would only add a second way for the scan to go red for reasons that have nothing to do with security.

The weekly `schedule` matters more than the push trigger: it is what re-runs the analysis against new
CodeQL queries on unchanged code, which is where most findings on a quiet repository come from.

Advanced setup **replaces** default setup rather than complementing it. If default setup is ever
re-enabled under *Settings → Code security → Code scanning*, this workflow's upload is rejected and
the badge in the root README goes red — the two cannot both publish results.

## `release.yml` — publish a release

### The version is declared, not derived

The release version is the `<Version>` property in
[`Directory.Build.props`](../../Directory.Build.props). Raising it by hand is what publishes a
release; the workflow never invents a number.

The value is read with `dotnet msbuild -getProperty:Version`, which returns the *evaluated* property
rather than a line scraped out of a file. Moving `<Version>` into a `.csproj`, or making it
conditional, therefore needs no change here. Evaluating a property runs no targets, so this needs no
`restore` — only the SDK. If the result is not a `MAJOR.MINOR.PATCH` triple the job fails with what
MSBuild actually returned.

### The gate

`v<version>` is the tag. What happens next depends only on that tag and on the highest version
already released:

| Situation | Outcome |
| --- | --- |
| The tag does not exist and the version is the highest so far | Publish |
| No release tags exist at all | Publish — first release |
| The tag already exists, on a push to `master` | Notice, nothing published |
| The tag already exists, on a manual dispatch | Failure — a release was asked for explicitly |
| The version is below the latest release | Failure — a typo, not a decision |

So a merge to `master` that leaves the property untouched publishes nothing and stays green.

Versions are compared component by component as integers, not as strings, so `10.0.0` correctly
outranks `9.9.9`. The comparison uses the **highest** release tag in the repository rather than the
nearest ancestor of `HEAD`, so an older number can never be published as `--latest`.

### Jobs

```
prepare ──> test ──> build (4 runtimes, matrix) ──> release
```

Every job after `prepare` is gated on `should_release`, so a push that changes nothing costs one
short job.

**`prepare`** checks out the full history (`fetch-depth: 0`) because it needs tags and the commit
range. It reads the version, applies the gate, and only then builds the release notes — the notes
step is skipped when nothing will be published.

Notes are grouped from the Conventional Commit subjects between the previous tag and `HEAD`, into
Features (`feat:`), Fixes (`fix:`), Refactoring (`refactor:`) and Dependencies (`bump:`). Other
prefixes are ignored. A `!` marker or a `BREAKING CHANGE:` footer adds a warning banner at the top.
The subject pattern accepts `;` as well as `:` after the type, because that has been typoed before.
When no commit matches, the notes fall back to a maintenance-release line rather than shipping empty.

**`test`** runs the suite in Release, the configuration that is about to be published.
`--blame-hang-timeout 5m` turns a deadlocked test into a dump plus a clear failure instead of a job
that burns the full six-hour runner budget. The suite finishes in well under a minute.

The `.trx` log is uploaded on success and on failure (`if: always()`).

**`build`** is a matrix over `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64`, with `fail-fast: true`
so one broken runtime does not spend runner time on the other three. **This job is the only place
distributable binaries are produced** — no local or CI build of
[`BackupZCrypt.Desktop.csproj`](../../BackupZCrypt.Desktop/BackupZCrypt.Desktop.csproj) publishes
anything, in any configuration, so the `PublishSingleFile` /
`IncludeNativeLibrariesForSelfExtract` / `EnableCompressionInSingleFile` / `DebugType=embedded` set
lives here and nowhere else. `-p:Version` is passed explicitly from `prepare`, so the version stamped
into the executable provably equals the tag.

Each runtime is staged as `BackupZCrypt-<tag>-<rid>` next to `LICENSE` and `README.md`, symbols are
dropped, and the result is zipped for Windows or tarred with `--owner=0 --group=0` elsewhere so the
archive carries no build-account metadata. The Unix binary is marked executable before archiving,
since neither `tar` nor a self-contained publish sets that bit for the user.

**`release`** downloads every package and creates the GitHub Release with `gh`, targeting the exact
commit that was built. It fails loudly rather than publishing an empty release if no assets arrived.
No checksum file is published: GitHub already exposes the SHA-256 digest of every release asset.

`contents: write` is granted to this job alone; the workflow's default is `contents: read`.
`concurrency` does **not** cancel in-progress runs — a half-published release is worse than a queued
one.

## Cutting a release

1. On `develop`, raise `<Version>` in `Directory.Build.props`.
2. Open a pull request to `master`. `ci.yml` runs the build, test and format gates.
3. Merge. `release.yml` publishes `v<version>` with notes and the four portable packages.

If a release run fails after the gate passed, fix the cause and re-run the workflow manually — no
empty commit needed. The dispatch fails fast if the version has meanwhile been published.
