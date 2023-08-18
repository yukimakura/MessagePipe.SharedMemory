using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace MessagePipe.SharedMemory.Tests;

public class UnitTest1
{
    [Fact]
    public async Task Basic_SuccessCase()
    {
        for (int i = 0; i < 10; i++)
        {
            await testBase<string, string>(("Foo", "Bar"), "Foo", resultList =>
            {
                resultList.First().Should().Be("Bar");
            });
        }
    }

    private record recordForTestCase(int arg1, string arg2);
    private class classForTestCase
    {
        public int arg1;
        public string arg2;

        public classForTestCase(int arg1, string arg2)
        {
            this.arg1 = arg1;
            this.arg2 = arg2;
        }
    };
    /// <summary>
    /// 様々な型のKeyでテストする
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Variouskeys_SuccessCase()
    {

        // await testBase((new recordForTestCase(100, "100"), "Baaar"), new recordForTestCase(100, "100"), resultList =>
        // {
        //     resultList.First().Should().Be("Baaar");
        // },1000);

        await testBase((new classForTestCase(100, "100"), "Booor"), new classForTestCase(100, "100"), resultList =>
        {
            resultList.First().Should().Be("Booor");
        }, 1000);
    }

    private async Task testBase<KEYTYPE, DATATYPE>((KEYTYPE key, DATATYPE data) pubData, KEYTYPE subscribeKey, Action<List<DATATYPE>> validationAction, int publishDelayTime = 250)
    {
        var provider = TestHelper.BuildSharedMemoryServiceProvider();

        var publisher = provider.GetRequiredService<IDistributedPublisher<KEYTYPE, DATATYPE>>();
        var subscriber = provider.GetRequiredService<IDistributedSubscriber<KEYTYPE, DATATYPE>>();

        var results = new List<DATATYPE>();
        await using var _ = await subscriber.SubscribeAsync(subscribeKey, x => results.Add(x));

        await publisher.PublishAsync(pubData.key, pubData.data);
        await Task.Delay(publishDelayTime);

        validationAction(results);
    }
}