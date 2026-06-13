using NUnit.Framework;

// xUnit constructs a fresh test-class instance per test; NUnit reuses one instance per
// fixture by default. Restore per-test isolation so instance fields (notably NSubstitute
// mocks) never leak configured state between tests.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
