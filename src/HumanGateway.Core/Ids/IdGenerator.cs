namespace HumanGateway.Core.Ids;

/// <summary>
/// Durable identifier generation (SYNC-FR-01, schemas/common.schema.json#/$defs/id). IDs are globally
/// unique and never reused after deletion; UUIDv4 is the recommended shape (dash-separated, matches the
/// <c>id</c> pattern <c>^[A-Za-z0-9][A-Za-z0-9._:-]{7,127}$</c>).
/// </summary>
public static class IdGenerator
{
    /// <summary>Returns a new durable UUIDv4 identifier in canonical dashed form.</summary>
    public static string NewId() => Guid.NewGuid().ToString("D");
}
