using System;
using Blip.Ai.Bot.Monitoring.Logging.Interface;
using Blip.Ai.Bot.Monitoring.Logging.Services;
using Serilog;
using SimpleInjector;
using Take.Blip.Builder.Hosting;
using Take.Blip.Client;
using Take.Blip.Client.Activation;

namespace Builder.Console
{
    public class BuilderServiceProvider : Container, IServiceContainer
    {
        public BuilderServiceProvider()
        {
            Options.EnableAutoVerification = false;
            Options.SuppressLifestyleMismatchVerification = true;
            Options.AllowOverridingRegistrations = true;
            this.RegisterBuilder();
            RegisterSingleton<IConfiguration, BuilderConfiguration>();
            RegisterSingleton<ILogger>(() => new LoggerConfiguration().WriteTo.Trace().CreateLogger());
            RegisterSingleton<IBlipLogger, NullBlipLogger>();
        }

        public void RegisterService(Type serviceType, object instance) => RegisterSingleton(serviceType, () => instance);

        public void RegisterService(Type serviceType, Func<object> instanceFactory) => RegisterSingleton(serviceType, instanceFactory);
    }
}