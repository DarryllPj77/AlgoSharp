namespace AlgoSharp.Core.Tests;

/// <summary>The tracer (Tr) keeps one static trace buffer, as in the single-run sandbox; tests that run it must not overlap.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TracerCollection
{
    public const string Name = "Tracer";
}
