using MessagePipe;
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
            services.AddSingleton(options); // add as singleton instance
            services.AddSingleton<ISharedMemorySerializer>(options.SharedMemorySerializer);

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

