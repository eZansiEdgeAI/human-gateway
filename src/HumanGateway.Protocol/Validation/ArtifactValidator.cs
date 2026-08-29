using HumanGateway.Protocol.Models;

namespace HumanGateway.Protocol.Validation;

/// <summary>
/// Validates an <see cref="Artifact"/> against artifact.schema.json and an <see cref="ArtifactReference"/>
/// against its <c>$defs/artifactReference</c> (PROTO-FR-04): ID + content hash, size bounds, MIME pattern,
/// filename bounds. Artifact bytes are never validated here — they are never part of the envelope.
/// </summary>
public sealed class ArtifactValidator : IProtocolValidator<Artifact>
{
    internal static readonly ArtifactValidator Instance = new();

    /// <inheritdoc />
    public ProtocolValidationResult Validate(Artifact? value)
    {
        if (value is null)
        {
            return ProtocolValidationResult.Invalid(
                new ProtocolValidationError(ValidationErrorCodes.Required, "#", "Artifact is required (artifact.schema.json)."));
        }

        var sink = new ErrorSink();
        ValidateInto(value, "", sink);
        return sink.ToResult();
    }

    internal void ValidateInto(Artifact? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Artifact is required.");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);
        CommonRules.ContentHash(value.Hash, $"{path}hash", sink);
        CommonRules.Range(value.SizeBytes, $"{path}sizeBytes", sink, 0, CommonRules.MaxArtifactSizeBytes, "sizeBytes");
        // mimeType is schema-required (artifact.schema.json "required"); a missing value is REQUIRED, not a pass.
        CommonRules.RequiredPattern(value.MimeType, $"{path}mimeType", sink, CommonRules.MimeTypeRegex(), "mimeType");
        CommonRules.Text(value.Filename, $"{path}filename", sink, true, 1, 255, "filename");
        CommonRules.Text(value.Description, $"{path}description", sink, false, 0, 2048, "description");
        CommonRules.Timestamp(value.CreatedAt, $"{path}createdAt", sink);
    }

    /// <summary>Validates an artifact reference inside an envelope (artifact.schema.json#/$defs/artifactReference).</summary>
    internal void ValidateReferenceInto(ArtifactReference? value, string path, ErrorSink sink)
    {
        if (value is null)
        {
            sink.Add(ValidationErrorCodes.Required, path, "Artifact reference is required (artifact.schema.json#/$defs/artifactReference).");
            return;
        }

        CommonRules.Id(value.Id, $"{path}id", sink);
        CommonRules.ContentHash(value.Hash, $"{path}hash", sink);
        CommonRules.Text(value.Filename, $"{path}filename", sink, false, 1, 255, "filename");
        // artifactReference.mimeType is bounded by maxLength only (no pattern in the schema).
        CommonRules.Text(value.MimeType, $"{path}mimeType", sink, false, 1, 127, "mimeType");
        if (value.SizeBytes is { } size)
        {
            // artifactReference.sizeBytes has minimum 0 only (no ceiling in the schema).
            CommonRules.Range(size, $"{path}sizeBytes", sink, 0, long.MaxValue, "sizeBytes");
        }
    }
}
