using AvaloniaAppMPV.Infrastructure.Mpv;
using Xunit;

namespace AvaloniaAppMPV.Tests;

public sealed class MpvConfigValidatorTests
{
    [Fact]
    public void FindsOptionsThatWouldBreakEmbeddedRendering()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mpv-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, "hwdec=auto\nvo=gpu-next\n# gpu-api=vulkan\n");

        try
        {
            var forbidden = MpvConfigValidator.FindForbiddenOptions(path);
            Assert.Contains("vo", forbidden);
            Assert.DoesNotContain("gpu-api", forbidden);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
