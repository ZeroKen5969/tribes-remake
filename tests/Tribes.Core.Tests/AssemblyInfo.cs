using Xunit;

// The original game (Tribes.Legacy, in the tests under Original/) keeps process-wide state, and
// several tests share the loaded content: tests must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
