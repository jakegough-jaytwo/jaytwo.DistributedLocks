using Xunit;
using Xunit.Abstractions;

namespace jaytwo.BrailleCharts.Tests;

public class Tests
{
    private readonly ITestOutputHelper _output;

    public Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("World")]
    public void HelloWorldTheory(string value)
    {
        _output.WriteLine(value);
    }

    [Fact]
    public void HelloWorld()
    {
        _output.WriteLine("hello wortld");
    }
}
