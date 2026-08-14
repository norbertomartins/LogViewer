using Xunit;

// ResponseLimits holds process-wide mutable configuration (the configured row/text caps), so tests that
// touch it must not run concurrently with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
