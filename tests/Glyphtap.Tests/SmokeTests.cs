using Xunit;

namespace Glyphtap.Tests;

public class SmokeTests
{
    [Fact]
    public void Wpf_Types_Are_Referenceable()
    {
        var color = System.Windows.Media.Colors.Red;
        Assert.Equal(255, color.R);
    }
}