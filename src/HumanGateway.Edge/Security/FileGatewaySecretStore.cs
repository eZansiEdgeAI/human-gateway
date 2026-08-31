using System.Runtime.InteropServices;
using System.Text.Json;
using HumanGateway.Edge.Api;

namespace HumanGateway.Edge.Security;

/// <summary>
/// File-backed <see cref="IGatewaySecretStore"/> (SP-07): the gateway identity — including the plaintext
/// registration token — lives at <c>&lt;dataDirectory&gt;/gateway-identity.json</c> with owner-only
/// permissions (POSIX 0600). The default data directory is the same one the SQLite store uses
/// (<c>&lt;ContentRoot&gt;/data</c>, configurable via <c>Edge:DataDirectory</c>); that directory is already
/// gitignored, so the secret can never be committed (SP-07).
/// </summary>
/// <remarks>
/// Writes are atomic (temp file + rename) so a power loss mid-write cannot leave a half-written token; the
/// store refuses to load a corrupt document rather than silently re-registering under a new identity.
/// </remarks>
public sealed class FileGatewaySecretStore : IGatewaySecretStore
{
    /// <summary>File name of the secret store document inside the data directory.</summary>
    public const string FileName = "gateway-identity.json";

    private readonly string _filePath;
    private readonly string _tempPath;

    /// <summary>Creates the store rooted at <paramref name="dataDirectory"/> (created if absent).</summary>
    public FileGatewaySecretStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, FileName);
        _tempPath = _filePath + ".tmp";
    }

    /// <inheritdoc />
    public Task<GatewayIdentity?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return Task.FromResult<GatewayIdentity?>(null);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<GatewaySecretDocument>(json, GatewaySecretJson.Options);
            if (document is null || string.IsNullOrWhiteSpace(document.GatewayId))
            {
                throw new InvalidDataException(
                    $"The Edge secret store at '{_filePath}' is corrupt (missing gatewayId). " +
                    "Fix or remove the file manually; a fresh registration would change the gateway identity.");
            }

            return Task.FromResult<GatewayIdentity?>(document.ToIdentity());
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"The Edge secret store at '{_filePath}' is not valid JSON; refusing to load it " +
                "(SP-07: never silently re-register under a new identity).", ex);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(GatewayIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.GatewayId))
        {
            throw new ArgumentException("Gateway identity requires a gatewayId.", nameof(identity));
        }

        var json = JsonSerializer.Serialize(GatewaySecretDocument.From(identity), GatewaySecretJson.Options);

        // Atomic replace: write the temp file (owner-only), then rename over the real file. A crash before
        // the rename leaves the previous store intact; a crash after leaves the new one (SP-07).
        File.WriteAllText(_tempPath, json);
        RestrictToOwner(_tempPath);
        File.Move(_tempPath, _filePath, overwrite: true);
        RestrictToOwner(_filePath);
        return Task.CompletedTask;
    }

    /// <summary>Applies owner-only (0600) permissions where the platform supports it (POSIX).</summary>
    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows() && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // POSIX: user read/write only — the file holds a registration-token secret (SP-07). Best-effort:
            // on filesystems that do not support UnixFileMode the call is a no-op and the default umask applies.
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
                // Not a UnixFileMode-capable filesystem; the atomic write + gitignore still apply.
            }
            catch (UnauthorizedAccessException)
            {
                // Some filesystems (e.g. certain network mounts) reject the mode change; keep the default.
            }
        }
    }
}
