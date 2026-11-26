using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoSharp.Playground.Library;

public enum ProblemMode { Practice, Challenge }

public enum ProblemStatus { NotStarted, InProgress, Solved }

/// <summary>An example as written in the source material, plus typed arguments when they could be read.</summary>
public sealed record UserExample
{
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
    public string? Explanation { get; init; }
}

/// <summary>A test case: one JSON value per parameter and the expected result as JSON.</summary>
public sealed record UserTest
{
    public List<string> Args { get; init; } = [];
    public string Expected { get; init; } = "";

    /// <summary>Only verified tests are used for grading. Imported tests start unverified unless the user confirms them.</summary>
    public bool Verified { get; init; }
}

/// <summary>
/// A problem in the user's personal library. Stored locally (IndexedDB). <see cref="Owner"/> and
/// <see cref="Revision"/> exist so sign-in and cloud sync can be added later without a schema change.
/// </summary>
public sealed record UserProblem
{
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Owner { get; init; } = "local";
    public int Revision { get; init; } = 1;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string Title { get; init; } = "";
    public int? Level { get; init; }
    public string? Difficulty { get; init; }
    public string? Category { get; init; }
    public List<string> Concepts { get; init; } = [];
    public string Collection { get; init; } = "";
    public string Description { get; init; } = "";
    public string? InputSpec { get; init; }
    public string? OutputSpec { get; init; }
    public List<UserExample> Examples { get; init; } = [];
    public List<string> Constraints { get; init; } = [];
    public string? Instructions { get; init; }

    /// <summary>C# method signature, e.g. "public int CountLocalMaximums(int[] nums)".</summary>
    public string? Signature { get; init; }

    /// <summary>True when the signature was suggested from examples rather than provided by the source.</summary>
    public bool SignatureSuggested { get; init; }

    public string? StarterCode { get; init; }
    public List<UserTest> Tests { get; init; } = [];
    public ProblemMode Mode { get; init; } = ProblemMode.Practice;

    /// <summary>"text" or "algosharp-json".</summary>
    public string Source { get; init; } = "text";
}

/// <summary>Per-problem progress: status and the latest C# draft.</summary>
public sealed record ProblemProgress
{
    public string ProblemId { get; init; } = "";
    public ProblemStatus Status { get; init; } = ProblemStatus.NotStarted;
    public string? Draft { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SolvedAt { get; init; }
}

/// <summary>A full backup of the local library.</summary>
public sealed record LibraryBackup
{
    public string Format { get; init; } = "algosharp/library-backup";
    public int Version { get; init; } = 1;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<UserProblem> Problems { get; init; } = [];
    public List<ProblemProgress> Progress { get; init; } = [];
}

public static class LibraryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
