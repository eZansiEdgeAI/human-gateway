using Xunit;

namespace HumanGateway.Security.Tests;

/// <summary>
/// Unit tests for the PHC password verifier (identity-security §6: token signing, authn rules, hashes — xUnit).
/// The verifier must be self-describing, salt each hash independently, and compare in constant time so no
/// timing side-channel reveals the digest (AUTH-FR-02, SP-07).
/// </summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesPhcStringWithAlgorithmCostSaltAndDigest()
    {
        var verifier = PasswordHasher.Hash("correct horse battery staple");

        Assert.StartsWith($"${PasswordHasher.Algorithm}$i={PasswordHasher.DefaultIterations},l=32$", verifier);
        // Five $-separated segments: [empty, algorithm, i/l, salt, digest] from the leading '$'.
        Assert.Equal(5, verifier.Split('$').Length);
        Assert.True(PasswordHasher.IsPhcVerifier(verifier));
    }

    [Fact]
    public void Verify_RoundTripsCorrectPassword()
    {
        var verifier = PasswordHasher.Hash("s3cret!");

        Assert.True(PasswordHasher.Verify("s3cret!", verifier));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var verifier = PasswordHasher.Hash("right-password");

        Assert.False(PasswordHasher.Verify("wrong-password", verifier));
    }

    [Fact]
    public void Hash_ProducesUniqueSalts_SoIdenticalPasswordsNeverMatchAsStrings()
    {
        var first = PasswordHasher.Hash("same-password");
        var second = PasswordHasher.Hash("same-password");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("same-password", first));
        Assert.True(PasswordHasher.Verify("same-password", second));
    }

    [Fact]
    public void Verify_RejectsMalformedVerifiers_WithoutThrowing()
    {
        Assert.False(PasswordHasher.Verify("password", "not-a-phc-string"));
        Assert.False(PasswordHasher.Verify("password", "$argon2id$broken"));
        Assert.False(PasswordHasher.Verify("password", string.Empty));
        Assert.False(PasswordHasher.Verify("password", "$pbkdf2-sha256$i=0,l=32$c2FsdA$c2FsdA"));
        Assert.False(PasswordHasher.Verify("password", "$pbkdf2-sha256$i=1000,l=32$!!invalid-base64!!$c2FsdA"));
    }

    [Fact]
    public void Verify_RejectsEmptyPassword_AndRejectsNullVerifier()
    {
        var verifier = PasswordHasher.Hash("non-empty");

        Assert.False(PasswordHasher.Verify(string.Empty, verifier));
        // A null verifier is a programming error, not a credential mismatch — guard explicitly.
        Assert.Throws<ArgumentNullException>(() => PasswordHasher.Verify("password", null!));
    }

    [Fact]
    public void Hash_RejectsNonPositiveIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("password", iterations: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("password", iterations: -1));
    }

    [Fact]
    public void IsPhcVerifier_MatchesSchemaPatternBounds()
    {
        // user.schema.json passwordVerifier pattern: ^\$[A-Za-z0-9+./=,$-]{19,511}$
        Assert.False(PasswordHasher.IsPhcVerifier(null));
        Assert.False(PasswordHasher.IsPhcVerifier(""));
        Assert.False(PasswordHasher.IsPhcVerifier("plaintext"));
        Assert.True(PasswordHasher.IsPhcVerifier(PasswordHasher.Hash("x")));
    }
}
