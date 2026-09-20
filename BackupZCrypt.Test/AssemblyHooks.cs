// xUnit runs test collections — one per test class by default — in parallel, which the suite this
// assembly holds cannot take: PathNormalizationHelperTests writes process-wide environment
// variables and PasswordStrengthFormatterTests reassigns the ambient culture, and both are only
// safe while nothing else is running. Serialising the whole assembly also keeps the heavy
// crypto and file-system integration tests from competing for the same disk, which is what the
// NUnit runner did before the migration.
using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(Mode = ParallelMode.None)]
