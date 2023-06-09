using System;

using Microsoft.Extensions.DependencyInjection;

namespace R8.FileMonitor
{
    public static class DependencyRegistrar
    {
        /// <summary>
        /// Registers File Watcher services.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
        /// <param name="options">An <see cref="Action"/> to configure the provided <see cref="WatcherOptions"/>.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddFileMonitor(this IServiceCollection services, Action<WatcherOptions>? options = null)
        {
            var opt = new WatcherOptions();
            options?.Invoke(opt);

            services.AddSingleton<WatcherOptions>(opt);
            services.AddHostedService<FileWatcher>();

            return services;
        }
    }
}