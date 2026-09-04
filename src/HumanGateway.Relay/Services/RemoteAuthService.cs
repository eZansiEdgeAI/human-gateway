using HumanGateway.Core.Ids;
using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;
using HumanGateway.Relay.Storage;
using HumanGateway.Relay.Storage.Entities;
using HumanGateway.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HumanGateway.Relay.Services;

/// <summary>
/// Remote user identity + authentication at the Relay (AUTH-FR-02, SP-03, external-web-access): the remote
/// login gate for users outside the school. Account provisioning, username + password login issuing signed
/// opaque session tokens, session validation, and logout — over the PostgreSQL store, sharing the identical
/// token/verifier semantics as the Edge (IUserSessionService). Passwords are stored only as PHC verifiers
/// (SP-07); session rows hold only the token fingerprint (SP-07). HumanGateway performs no role-checking here
/// (SP-09); the returned identity feeds the authorisation middleware (AUTH-FR-03) and the Relay-side remote
/// access gates.
/// </summary>
public sealed class RemoteAuthService : IUserSessionService
{
    private readonly IDbContextFactory<RelayDbContext> _factory;
    private readonly AuthOptions _options;
    private readonly ILogger<RemoteAuthService> _logger;

    /// <summary>Creates the service over the durable store factory and shared auth options.</summary>
    public RemoteAuthService(
        IDbContextFactory<RelayDbContext> factory,
        IOptions<AuthOptions> options,
        ILogger<RemoteAuthService> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Provisions a remote user account (AUTH-FR-02). The username is normalised to lowercase; the password is
    /// stored as a PHC verifier only (SP-07). DISABLED users may not log in.
    /// </summary>
    public async Task<UserView> CreateUserAsync(string username, string displayName, string password, CancellationToken ct)
    {
        var normalised = NormaliseUsername(username);
        var now = ProtocolTime.Now();

        var user = new User
        {
            Id = IdGenerator.NewId(),
            Username = normalised,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalised : displayName,
            PasswordVerifier = PasswordHasher.Hash(password),
            Status = UserStatus.Active,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ProtocolValidator.Default.User.Validate(user).ThrowIfInvalid();

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Username == normalised, ct).ConfigureAwait(false);
        if (exists)
        {
            throw GatewayServiceException.Conflict($"A user named '{normalised}' already exists.");
        }

        db.Users.Add(UserRecord.FromEnvelope(user));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Remote user account created: username {Username}", normalised);
        return ToView(user);
    }

    /// <summary>
    /// Authenticates a username + password and, on success, issues a signed session token (AUTH-FR-02). The
    /// remote login gate used by external-web-access.
    /// </summary>
    /// <exception cref="GatewayServiceException">401 AUTH_REJECTED on unknown user / wrong password (never
    /// reveals which); 403 FORBIDDEN on a DISABLED account.</exception>
    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct)
    {
        var normalised = NormaliseUsername(username);
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var record = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Username == normalised, ct)
            .ConfigureAwait(false);
        if (record is null || !PasswordHasher.Verify(password, record.Envelope.PasswordVerifier))
        {
            // Deliberately identical for unknown user and wrong password (no account enumeration, SP-07).
            throw new GatewayServiceException(StatusCodes.Status401Unauthorized, ErrorCodes.AuthRejected,
                "Invalid username or password.");
        }

        var user = record.Envelope;
        if (user.Status != UserStatus.Active)
        {
            throw GatewayServiceException.Forbidden(ErrorCodes.Forbidden,
                $"Account '{user.Username}' is disabled.");
        }

        var issued = await IssueSessionAsync(user.Id, ct).ConfigureAwait(false);

        // Record the login timestamp on the user record.
        var now = ProtocolTime.Now();
        var tracked = await db.Users.SingleAsync(u => u.Id == user.Id, ct).ConfigureAwait(false);
        tracked.Envelope = user with { LastLoginAt = now, UpdatedAt = now };
        tracked.RefreshDenormalised();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new LoginResult { Session = issued, User = ToView(user) };
    }

    /// <inheritdoc />
    public async Task<AuthenticatedUser?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (!SessionTokens.IsWellFormed(token))
        {
            return null;
        }

        var fingerprint = SessionTokens.Fingerprint(token);
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var session = await db.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.TokenFingerprint == fingerprint, ct)
            .ConfigureAwait(false);
        if (session is null || session.RevokedAt is not null)
        {
            return null;
        }

        if (ProtocolTime.TryParse(session.ExpiresAt, out var expiresAt) && expiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => u.Envelope)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (user is null || user.Status != UserStatus.Active)
        {
            return null;
        }

        return new AuthenticatedUser
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role == UserRole.Admin ? "ADMIN" : "USER",
            ExpiresAt = session.ExpiresAt,
        };
    }

    /// <inheritdoc />
    public async Task RevokeSessionAsync(string token, CancellationToken ct)
    {
        if (!SessionTokens.IsWellFormed(token))
        {
            return;
        }

        var fingerprint = SessionTokens.Fingerprint(token);
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var session = await db.Sessions.SingleOrDefaultAsync(s => s.TokenFingerprint == fingerprint, ct).ConfigureAwait(false);
        if (session is { RevokedAt: null })
        {
            session.RevokedAt = ProtocolTime.Now();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Seeds the configured bootstrap user on first boot (AUTH-FR-02, SP-07: credentials from env).</summary>
    public async Task SeedBootstrapUserAsync(CancellationToken ct)
    {
        var bootstrap = _options.BootstrapUser;
        if (bootstrap is null || string.IsNullOrWhiteSpace(bootstrap.Username) || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            return;
        }

        var normalised = NormaliseUsername(bootstrap.Username);
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var exists = await db.Users.AsNoTracking().AnyAsync(u => u.Username == normalised, ct).ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        var user = new User
        {
            Id = IdGenerator.NewId(),
            Username = normalised,
            DisplayName = string.IsNullOrWhiteSpace(bootstrap.DisplayName) ? normalised : bootstrap.DisplayName!,
            PasswordVerifier = PasswordHasher.Hash(bootstrap.Password),
            Status = UserStatus.Active,
            Role = UserRole.Admin,
            CreatedAt = ProtocolTime.Now(),
            UpdatedAt = ProtocolTime.Now(),
        };
        ProtocolValidator.Default.User.Validate(user).ThrowIfInvalid();

        db.Users.Add(UserRecord.FromEnvelope(user));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Bootstrap remote user seeded: username {Username}", normalised);
    }

    /// <inheritdoc />
    public async Task<IssuedSession> IssueSessionAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var token = SessionTokens.Generate();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(_options.SessionTtl);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Sessions.Add(new SessionRecord
        {
            TokenFingerprint = SessionTokens.Fingerprint(token),
            UserId = userId,
            IssuedAt = ProtocolTime.Format(now),
            ExpiresAt = ProtocolTime.Format(expiresAt),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new IssuedSession { Token = token, ExpiresAt = ProtocolTime.Format(expiresAt) };
    }

    /// <summary>Returns the public view of a remote user by id, or null when the account does not exist.</summary>
    public async Task<UserView?> GetUserByIdAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Envelope)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return user is null ? null : ToView(user);
    }

    /// <summary>Returns the public view of a remote user by (case-insensitive) username, or null when absent.</summary>
    public async Task<UserView?> GetUserByUsernameAsync(string username, CancellationToken ct)
    {
        var normalised = NormaliseUsername(username);

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == normalised)
            .Select(u => u.Envelope)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return user is null ? null : ToView(user);
    }

    /// <summary>Lists all remote user accounts (public views, no verifier — SP-07), ordered by username.</summary>
    public async Task<IReadOnlyList<UserView>> ListUsersAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => u.Envelope)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return users.Select(ToView).ToList();
    }

    /// <summary>Builds the public user view (no verifier — SP-07).</summary>
    public static UserView ToView(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Status = user.Status switch
        {
            UserStatus.Disabled => "DISABLED",
            _ => "ACTIVE",
        },
        Role = user.Role == UserRole.Admin ? "ADMIN" : "USER",
        LastLoginAt = user.LastLoginAt,
        DisabledAt = user.DisabledAt,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };

    private static string NormaliseUsername(string username)
        => string.IsNullOrWhiteSpace(username) ? string.Empty : username.Trim().ToLowerInvariant();
}
