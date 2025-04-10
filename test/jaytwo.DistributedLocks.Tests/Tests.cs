using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class Tests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Tests(TestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void HelloWorld()
    {
        _output.WriteLine("Test Environment: " + _fixture.TestEnvironment);
    }
}
