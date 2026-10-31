using Velixo.Cli.Auth;

namespace Velixo.Cli.Tests;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_matches_the_RFC_7636_example()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ComputeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifier_is_43_base64url_characters_and_random()
    {
        var first = Pkce.CreateVerifier();
        var second = Pkce.CreateVerifier();

        Assert.Equal(43, first.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.NotEqual(first, second);
        Assert.Equal(43, Pkce.ComputeChallenge(first).Length);
    }
}
