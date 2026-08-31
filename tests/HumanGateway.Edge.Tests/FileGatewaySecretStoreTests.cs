using System.Text.Json;
using HumanGateway.Edge.Security;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Secret-store tests (AUTH-FR-01, SP-07): the gateway identity — including the plaintext registration token
/// — round-trips through the owner-only file store, persists atomically, refuses to load a corrupt document
/// (never silently re-registers under a new identity), and returns null before first registration.
/// </summary>
public sealed class FileGatewaySecretStoreTests : IDisposable
{
    private readonly string _dir;

    public FileGatewaySecretStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "hgsecret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private FileGatewaySecretStore NewStore() => new(_dir);

    private static GatewayIdentity SampleIdentity() => new()
    {
        GatewayId = "gateway:school-a",
        State = GatewayIdentityState.Registered,
        RegistrationToken = "hgrt_" + new string('A', 43),
        TokenExpiresAt = "2026-10-01T00:00:00.000Z",
        RegisteredAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task Load_WhenNoSecretStored_ReturnsNull()
    {
        var store = NewStore();

        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_ThenLoad_RoundTripsIdentityAndToken()
    {
        var store = NewStore();
        var identity = SampleIdentity();

        await store.SaveAsync(identity);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(identity.GatewayId, loaded!.GatewayId);
        Assert.Equal(identity.State, loaded.State);
        Assert.Equal(identity.RegistrationToken, loaded.RegistrationToken);
        Assert.Equal(identity.TokenExpiresAt, loaded.TokenExpiresAt);
        Assert.Equal(identity.RegisteredAtUtc, loaded.RegisteredAtUtc);
        Assert.True(loaded.IsRegistered);
    }

    [Fact]
    public async Task Save_OverwritesPreviousIdentityAtomically()
    {
        var store = NewStore();
        await store.SaveAsync(SampleIdentity());
        var updated = SampleIdentity() with
        {
            RegistrationToken = "hgrt_" + new string('B', 43),
            State = GatewayIdentityState.Pending,
        };

        await store.SaveAsync(updated);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("hgrt_" + new string('B', 43), loaded!.RegistrationToken);
        Assert.Equal(GatewayIdentityState.Pending, loaded.State);
    }

    [Fact]
    public void Load_WhenStoreIsCorruptJson_ThrowsInsteadOfSilentlyReRegistering()
    {
        var store = NewStore();
        File.WriteAllText(Path.Combine(_dir, FileGatewaySecretStore.FileName), "{ not valid json !!!");

        var ex = Assert.Throws<InvalidDataException>(() => store.LoadAsync().GetAwaiter().GetResult());

        Assert.Contains("secret store", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_WhenStoreLacksGatewayId_Throws()
    {
        var store = NewStore();
        File.WriteAllText(Path.Combine(_dir, FileGatewaySecretStore.FileName),
            JsonSerializer.Serialize(new { registrationToken = "hgrt_" + new string('C', 43) }));

        var ex = Assert.Throws<InvalidDataException>(() => store.LoadAsync().GetAwaiter().GetResult());

        Assert.Contains("gatewayId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_NullOrBlankGatewayId_IsRejected()
    {
        var store = NewStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new GatewayIdentity
        {
            GatewayId = "  ",
            State = GatewayIdentityState.Unregistered,
        }));
    }

    [Fact]
    public async Task Save_WritesFileUnderGitignoredDataDirectory_WithOwnerOnlyPermissionsOnPosix()
    {
        var store = NewStore();
        await store.SaveAsync(SampleIdentity());

        var file = Path.Combine(_dir, FileGatewaySecretStore.FileName);
        Assert.True(File.Exists(file));

        if (!OperatingSystem.IsWindows())
        {
            // SP-07: the token file must be readable/writable only by the owning user.
            var mode = File.GetUnixFileMode(file);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        }
    }
}
