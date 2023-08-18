using MessagePipe;
using MessagePipe.SharedMemory.InternalClasses;
using MessagePipe.SharedMemory.InternalClasses.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace MessagePipe.SharedMemory
{

    public static class ServiceCollectionSharedMemoryExtensions
    {
        public static IServiceCollection AddMessagePipeSharedMemory(this IServiceCollection services)
         => AddMessagePipeSharedMemory(services, _ => { });

        public static IServiceCollection AddMessagePipeSharedMemory(this IServiceCollection services, Action<MessagePipeSharedMemoryOptions> configure)
        {
            var options = new MessagePipeSharedMemoryOptions();
            configure(options);
            services.AddSingleton(options);
            services.AddSingleton(options.SharedMemorySerializer);
            var mmfFactory = new MappedFileFactory();
            services.AddSingleton<ICircularBuffer>(
                new CircularBuffer(mmfFactory.Create(new QueueOptions(options.QueueName, options.QueueSize)), options.QueueSize)
                );

            services.Add(typeof(IDistributedPublisher<,>), typeof(SharedMemoryPublisher<,>), InstanceLifetime.Singleton);
            services.Add(typeof(IDistributedSubscriber<,>), typeof(SharedMemorySubscriber<,>), InstanceLifetime.Singleton);

            return services;
        }

        public static void Add(this IServiceCollection services, Type serviceType, Type implementationType, InstanceLifetime scope)
        {
            var lifetime = (scope == InstanceLifetime.Scoped) ? ServiceLifetime.Scoped
                : (scope == InstanceLifetime.Singleton) ? ServiceLifetime.Singleton
                : ServiceLifetime.Transient;
            services.Add(new ServiceDescriptor(serviceType, implementationType, lifetime));
        }
    }
}

