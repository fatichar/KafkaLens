using System.Collections.Generic;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Core.Tests.Models;

public class MessageTests
{
    [Fact]
    public void KeyText_ForBigEndianIntegerKey_ReturnsIntegerText()
    {
        var key = new byte[] { 0, 0, 0, 42 };
        var message = new Message(0, new Dictionary<string, byte[]>(), key, null);

        Assert.Equal("42", message.KeyText);
    }

    [Fact]
    public void KeyText_DoesNotMutateKeyBytes()
    {
        var key = new byte[] { 0, 0, 0, 42 };
        var original = (byte[])key.Clone();
        var message = new Message(0, new Dictionary<string, byte[]>(), key, null);

        _ = message.KeyText;

        Assert.Equal(original, key);
    }

    [Fact]
    public void KeyText_ForLongIntegerKey_PreservesExistingByteOrderBehavior()
    {
        var key = new byte[] { 1, 2, 3, 4, 0, 0, 0, 42 };
        var message = new Message(0, new Dictionary<string, byte[]>(), key, null);

        Assert.Equal("42", message.KeyText);
    }
}
