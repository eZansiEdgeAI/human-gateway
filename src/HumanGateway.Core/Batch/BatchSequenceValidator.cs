using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Batch;

/// <summary>
/// The result of validating a batch's cross-field shape invariants (syncbatch.schema.json, schemas/README.md
/// "Batch-shape invariants"). These are the checks that require looking at more than one property at once, so
/// they cannot be expressed as independent <c>properties</c> entries in the schema and are enforced here —
/// and by the sync engine before any item is applied.
/// </summary>
public sealed record BatchValidationResult
{
    /// <summary>The violation codes (empty when valid). See <see cref="BatchSequenceValidator"/> for the catalog.</summary>
    public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();

    /// <summary>True when there are no violations.</summary>
    public bool IsValid => Violations.Count == 0;

    /// <summary>The canonical valid result.</summary>
    public static readonly BatchValidationResult Valid = new();

    /// <summary>Builds a result from the given violation codes.</summary>
    public static BatchValidationResult Invalid(params string[] violations) => new() { Violations = violations };
}

/// <summary>
/// Enforces the sync-batch cross-field invariants (SYNC-FR-01, SYNC-FR-07, syncbatch.schema.json):
/// <list type="bullet">
/// <item>An empty (keepalive) batch MUST leave <c>sequenceStart</c>/<c>sequenceEnd</c> null.</item>
/// <item>A non-empty batch MUST declare its <c>sequenceStart..sequenceEnd</c> span.</item>
/// <item><c>sequenceStart &lt;= sequenceEnd</c>.</item>
/// <item>Every item's <c>sequence</c> falls within the declared span.</item>
/// <item>At most <see cref="MaxItemsPerBatch"/> items per batch.</item>
/// </list>
/// </summary>
public static class BatchSequenceValidator
{
    /// <summary>Maximum items per batch (syncbatch.schema.json <c>maxItems</c>).</summary>
    public const int MaxItemsPerBatch = 1000;

    /// <summary>Empty batch declared a sequence range it must not have.</summary>
    public const string SequenceRangeForbidden = "SEQUENCE_RANGE_FORBIDDEN";

    /// <summary>Non-empty batch did not declare its sequence range.</summary>
    public const string SequenceRangeRequired = "SEQUENCE_RANGE_REQUIRED";

    /// <summary>Declared sequence range is inverted (<c>sequenceStart &gt; sequenceEnd</c>).</summary>
    public const string SequenceRangeInverted = "SEQUENCE_RANGE_INVERTED";

    /// <summary>An item's sequence falls outside the declared span.</summary>
    public const string ItemSequenceOutOfRange = "ITEM_SEQUENCE_OUT_OF_RANGE";

    /// <summary>Batch exceeds the item cap.</summary>
    public const string TooManyItems = "TOO_MANY_ITEMS";

    /// <summary>Validates the batch and returns a <see cref="BatchValidationResult"/> listing any violations.</summary>
    public static BatchValidationResult Validate(SyncBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var violations = new List<string>();
        var items = batch.Items ?? new List<SyncItem>();

        if (items.Count > MaxItemsPerBatch)
        {
            violations.Add(TooManyItems);
        }

        var hasItems = items.Count > 0;
        var hasRange = batch.SequenceStart is not null || batch.SequenceEnd is not null;

        if (hasItems && !hasRange)
        {
            violations.Add(SequenceRangeRequired);
        }
        else if (!hasItems && hasRange)
        {
            violations.Add(SequenceRangeForbidden);
        }

        if (hasItems && batch.SequenceStart is { } start && batch.SequenceEnd is { } end)
        {
            if (start > end)
            {
                violations.Add(SequenceRangeInverted);
            }

            if (start <= end)
            {
                foreach (var item in items)
                {
                    if (item.Sequence < start || item.Sequence > end)
                    {
                        violations.Add(ItemSequenceOutOfRange);
                        break;
                    }
                }
            }
        }

        return violations.Count == 0 ? BatchValidationResult.Valid : BatchValidationResult.Invalid(violations.ToArray());
    }
}
