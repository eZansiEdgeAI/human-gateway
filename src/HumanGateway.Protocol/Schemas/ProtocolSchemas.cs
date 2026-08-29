using System.Reflection;
using System.Text.Json;

namespace HumanGateway.Protocol;

/// <summary>
/// Exposes the versioned protocol schema documents (release v1.0.0, JSON Schema Draft 2020-12) embedded in
/// this assembly, keyed by their versioned <c>$id</c>. The schemas under <c>schemas/</c> are the single
/// source of truth (NF-06); shipping them with the protocol assembly lets any component reference, package,
/// or forward the exact documents a given binary was validated against.
/// </summary>
public static class ProtocolSchemas
{
    private const string ResourcePrefix = "HumanGateway.Protocol.Schemas.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LazyDocuments = new(BuildDocuments);

    /// <summary>All embedded schema documents, keyed by their <c>$id</c> (raw JSON text).</summary>
    public static IReadOnlyDictionary<string, string> Documents => LazyDocuments.Value;

    /// <summary>Returns the schema document with the given <c>$id</c> (raw JSON text).</summary>
    /// <exception cref="KeyNotFoundException">No embedded schema carries that <c>$id</c>.</exception>
    public static string Get(string id) => LazyDocuments.Value[id];

    /// <summary>The number of embedded schema documents.</summary>
    public static int Count => LazyDocuments.Value.Count;

    private static IReadOnlyDictionary<string, string> BuildDocuments()
    {
        var assembly = typeof(ProtocolSchemas).Assembly;
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resourceName in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("$id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Embedded schema '{resourceName}' is missing its $id.");
            }

            documents.Add(idElement.GetString()!, json);
        }

        return documents;
    }
}
