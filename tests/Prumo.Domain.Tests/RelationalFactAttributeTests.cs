namespace Prumo.Domain.Tests;

public sealed class RelationalFactAttributeTests
{
    [Theory]
    [InlineData("PRUMO_RUN_RELATIONAL_TESTS=1")]
    [InlineData("PRUMO_RUN_RELATIONAL_TESTS = '1'")]
    [InlineData("PRUMO_RUN_RELATIONAL_TESTS=\"1\" # local")]
    public void Accepts_enabled_flag_from_dot_env(string line)
        => Assert.True(RelationalFactAttribute.IsEnabled(null, [line]));

    [Fact]
    public void Process_environment_overrides_dot_env()
        => Assert.False(RelationalFactAttribute.IsEnabled("0", ["PRUMO_RUN_RELATIONAL_TESTS=1"]));

    [Fact]
    public void Ignores_comments_and_other_keys()
        => Assert.False(RelationalFactAttribute.IsEnabled(null, ["# PRUMO_RUN_RELATIONAL_TESTS=1", "OTHER=1"]));
}
