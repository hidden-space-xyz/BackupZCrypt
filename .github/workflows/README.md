# GitHub Actions workflow

BackupZCrypt ships through a single gated flow that ends in cross-platform desktop packaging:

```text
push to master
  -> Release build, tests, >= 90% core coverage and formatting
  -> Conventional Commits version resolution
  -> four self-contained portable packages with build provenance
  -> tag and GitHub Release
```

The entry point is [`ci.yml`](ci.yml). It is the only workflow that publishes and calls
[`release.yml`](release.yml) as a reusable workflow, so a release cannot run when validation fails.
The chain deliberately does not cancel an in-progress run, so a release cannot be interrupted halfway
through by a newer push. [`codeql.yml`](codeql.yml) runs on its own and does not gate publishing.

## Quality gate

The `quality` job uses .NET 10 and the Release configuration. It restores the solution and local
tools, builds with the repository analyzers and warnings-as-errors settings, runs all tests with
Microsoft Testing Platform, creates a Cobertura report and verifies whitespace formatting. NuGet
packages are cached by the hash of `Directory.Packages.props`, the project files and the local tool
manifest.

Coverage is required to stay at or above 90% for lines, branches and methods. The measurement covers
the platform-independent `Application`, `Composition`, `Domain` and `Infrastructure` assemblies.
Avalonia UI composition is still compiled and its view-model tests still run, but the `Desktop`
assembly is not part of the threshold because it also contains generated XAML and native UI adapters
that cannot be exercised by the headless Linux runner. The exact filter lives in
[`BackupZCrypt.Test/CodeCoverage.config`](../../BackupZCrypt.Test/CodeCoverage.config).

## CodeQL

Pushes to `develop` or `master` that change a project, the solution, the shared build files,
`global.json` or the workflow itself start CodeQL, which is independent of CI and does not gate
publishing. A newer push to the same branch cancels the scan in progress. Findings never fail the run;
review them in the repository's code scanning alerts or in the `codeql-sarif-csharp` workflow
artifact, retained for 7 days. Only a broken build, analysis or upload fails it.

C# analysis uses the extended security and quality query suites over a full .NET 10 build under
CodeQL tracing, so the analyzed program is the same solution that ships. Results located in `obj/` or
`bin/` (generated code) are dropped before upload.

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

Each build job signs a SLSA build provenance attestation for its package, which anyone can check with
`gh attestation verify <asset> --repo hidden-space-xyz/BackupZCrypt`.

The final job re-runs version resolution to protect against concurrent publication, verifies that all
four packages exist, and creates the tag and GitHub Release with generated notes. Only this final job
receives `contents: write`; build jobs additionally get `id-token: write` and `attestations: write`
to sign provenance, and validation is read-only.

## Dependency updates

Dependabot ([`dependabot.yml`](../dependabot.yml)) checks NuGet packages and GitHub Actions and opens
one grouped pull request against `develop` every Saturday at 00:00 (Europe/Madrid). Its commits use
the `bump` prefix, which never publishes a release by itself.

## Cutting a release

1. Merge normal work into `develop` using Conventional Commits.
2. Merge `develop` into `master`.
3. The workflow validates the exact `master` commit and publishes only if its unreleased history
   contains a breaking, feature, fix or performance commit.

There is no version file to raise. `Directory.Build.props` uses `0.0.0-local` for developer builds;
the release job supplies the calculated stable version to `dotnet publish`.
