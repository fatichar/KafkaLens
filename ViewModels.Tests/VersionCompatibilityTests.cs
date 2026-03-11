using FluentAssertions;
using KafkaLens.ViewModels.Services;
using Xunit;

namespace KafkaLens.ViewModels.Tests;

public class VersionCompatibilityTests
{
    // ── Empty / null constraints ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyConstraint_IsAlwaysCompatible(string constraint)
    {
        VersionCompatibility.IsCompatible(constraint, "1.0.0").Should().BeTrue();
    }

    // ── >= operator ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(">=1.0.0", "1.0.0", true)]
    [InlineData(">=1.0.0", "1.0.1", true)]
    [InlineData(">=1.0.0", "2.0.0", true)]
    [InlineData(">=1.0.0", "0.9.9", false)]
    [InlineData(">=1.2.3", "1.2.2", false)]
    public void GreaterThanOrEqual(string constraint, string version, bool expected)
    {
        VersionCompatibility.IsCompatible(constraint, version).Should().Be(expected);
    }

    // ── > operator ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(">1.0.0", "1.0.0", false)]
    [InlineData(">1.0.0", "1.0.1", true)]
    [InlineData(">1.0.0", "2.0.0", true)]
    [InlineData(">1.0.0", "0.9.9", false)]
    public void GreaterThan(string constraint, string version, bool expected)
    {
        VersionCompatibility.IsCompatible(constraint, version).Should().Be(expected);
    }

    // ── <= operator ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<=2.0.0", "2.0.0", true)]
    [InlineData("<=2.0.0", "1.9.9", true)]
    [InlineData("<=2.0.0", "2.0.1", false)]
    public void LessThanOrEqual(string constraint, string version, bool expected)
    {
        VersionCompatibility.IsCompatible(constraint, version).Should().Be(expected);
    }

    // ── < operator ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<2.0.0", "1.9.9", true)]
    [InlineData("<2.0.0", "2.0.0", false)]
    [InlineData("<2.0.0", "2.0.1", false)]
    public void LessThan(string constraint, string version, bool expected)
    {
        VersionCompatibility.IsCompatible(constraint, version).Should().Be(expected);
    }

    // ── = operator ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("=1.5.0", "1.5.0", true)]
    [InlineData("=1.5.0", "1.5.1", false)]
    [InlineData("=1.5.0", "1.4.9", false)]
    public void ExactMatch(string constraint, string version, bool expected)
    {
        VersionCompatibility.IsCompatible(constraint, version).Should().Be(expected);
    }

    // ── Four-component versions ────────────────────────────────────────────────

    [Fact]
    public void FourPartVersion_IsSupported()
    {
        VersionCompatibility.IsCompatible(">=1.0.0.0", "1.0.0.1").Should().BeTrue();
        VersionCompatibility.IsCompatible(">=1.0.0.5", "1.0.0.4").Should().BeFalse();
    }

    // ── Malformed constraints — treated as compatible ─────────────────────────

    [Theory]
    [InlineData("1.0.0")]         // missing operator
    [InlineData(">=1.0")]         // only two parts
    [InlineData("~=1.0.0")]       // unsupported operator
    [InlineData(">=abc")]         // non-numeric version
    [InlineData("latest")]        // free-text
    public void MalformedConstraint_TreatedAsCompatible(string constraint)
    {
        // IsCompatible should return true and not throw.
        // The warning log is a side-effect we cannot assert on here without a mock logger.
        VersionCompatibility.IsCompatible(constraint, "1.0.0").Should().BeTrue();
    }

    // ── Whitespace tolerance ───────────────────────────────────────────────────

    [Fact]
    public void WhitespaceAroundConstraint_IsHandled()
    {
        VersionCompatibility.IsCompatible("  >=1.0.0  ", "1.0.0").Should().BeTrue();
    }

    [Fact]
    public void WhitespaceBetweenOperatorAndVersion_IsHandled()
    {
        VersionCompatibility.IsCompatible(">= 1.0.0", "1.0.0").Should().BeTrue();
    }
}
