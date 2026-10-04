using Xunit;

// Each WebApplicationFactory boots the host's Program.Main, which configures the global Serilog logger.
// Running host-based test classes in parallel makes those boots interfere, so the suite runs sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
