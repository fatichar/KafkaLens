using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Models;

public class MessageStreamTests
{
    [Fact]
    public void HasMore_WhenSetFalseMoreThanOnce_FiresFinishedOnce()
    {
        var stream = new MessageStream();
        var finishedCount = 0;
        stream.Finished += () => finishedCount++;

        stream.HasMore = false;
        stream.HasMore = false;

        Assert.Equal(1, finishedCount);
    }
}
