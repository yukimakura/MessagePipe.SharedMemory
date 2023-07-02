using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePipe.SharedMemory.Tests;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        var provider = TestHelper.BuildSharedMemoryServiceProvider();

        var publisher = provider.GetRequiredService<IDistributedPublisher<string, string>>();
        var subscriber = provider.GetRequiredService<IDistributedSubscriber<string, string>>();
     
        var results = new List<string>();
        await using var _ = await subscriber.SubscribeAsync("Foo", x => results.Add(x));

        await publisher.PublishAsync("Foo", "Bar");
        await Task.Delay(250);
        results.FirstOrDefault().Should().Be("Bar");
    }
}