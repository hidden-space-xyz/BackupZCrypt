# GitHub Actions workflow

BackupZCrypt follows the same gated release flow as CodigoActivo, with the Docker publication stage
replaced by cross-platform desktop packaging:

```text
push to master
  -> Release build, tests, >= 90% core coverage and formatting
  -> CodeQL for C# and GitHub Actions
  -> Conventional Commits version resolution
  -> four self-contained portable packages
  -> tag and GitHub Release
```

The entry point is [`ci.yml`](ci.yml). It is the only workflow triggered by a push and calls
[`codeql.yml`](codeql.yml) and [`release.yml`](release.yml) as reusable workflows. This keeps the
validation, security and publication jobs in one dependency chain: a later stage cannot run when an
earlier one fails. The chain deliberately does not cancel an in-progress run, so a release cannot be
interrupted halfway through by a newer push.

## Quality gate

The `quality` job uses .NET 10 and the Release configuration. It restores the solution and local
tools, builds with the repository analyzers and warnings-as-errors settings, runs all tests with
Microsoft Testing Platform, creates a Cobertura report and verifies whitespace formatting.

Coverage is required to stay at or above 90% for lines, branches and methods. The measurement covers
the platform-independent `Application`, `Composition`, `Domain` and `Infrastructure` assemblies.
Avalonia UI composition is still compiled and its view-model tests still run, but the `Desktop`
assembly is not part of the threshold because it also contains generated XAML and native UI adapters
that cannot be exercised by the headless Linux runner. The exact filter lives in
[`BackupZCrypt.Test/CodeCoverage.config`](../../BackupZCrypt.Test/CodeCoverage.config).

## CodeQL gate

CodeQL scans both C# and GitHub Actions with the extended security and quality query suites. C# uses
a manual traced build so the analyzed program is the same solution that ships. The generated SARIF
is also checked locally by [`check-codeql.mjs`](../scripts/check-codeql.mjs): findings with a security
severity of at least 7, findings at error level, and incomplete analyses block the release. SARIF
artifacts are retained for 14 days for diagnosis.

## Versioning

[`release-version.mjs`](../scripts/release-version.mjs) finds the highest stable `vMAJOR.MINOR.PATCH`
tag and inspects every commit after it. The highest applicable Conventional Commit change wins:

| Commit | Version change |
| --- | --- |
| `type!:` | major |
| `feat:` | minor |
| `fix:` or `perf:` | patch |
| other types | none |

Scopes such as `feat(ui):` are supported. If there is no releasable commit, the release workflow
stops after its short version job and succeeds without creating a tag. The previous tag must be an
ancestor of the commit being built, which prevents an old workflow rerun from moving `latest`
backwards.

## Desktop publication

For a new version, [`release.yml`](release.yml) publishes self-contained, compressed single-file
builds and stamps the calculated version into each executable. The build matrix produces:

| Runtime | Asset |
| --- | --- |
| Windows x64 | `BackupZCrypt-v<version>-win-x64.zip` |
| Linux x64 | `BackupZCrypt-v<version>-linux-x64.tar.gz` |
| macOS Intel | `BackupZCrypt-v<version>-osx-x64.tar.gz` |
| macOS Apple Silicon | `BackupZCrypt-v<version>-osx-arm64.tar.gz` |

The final job re-runs version resolution to protect against concurrent publication, verifies that all
four packages exist, and creates the tag and GitHub Release with generated notes. Only this final job
receives `contents: write`; every validation and build job is read-only.

## Cutting a release

1. Merge normal work into `develop` using Conventional Commits.
2. Merge `develop` into `master`.
3. The workflow validates the exact `master` commit and publishes only if its unreleased history
   contains a breaking, feature, fix or performance commit.

There is no version file to raise. `Directory.Build.props` uses `0.0.0-local` for developer builds;
the release job supplies the calculated stable version to `dotnet publish`.
