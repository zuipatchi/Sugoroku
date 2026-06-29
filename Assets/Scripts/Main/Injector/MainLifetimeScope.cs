using VContainer;
using VContainer.Unity;

namespace Main.Injector
{
    // Inspector で parentReference に CommonLifetimeScope を設定すること
    public class MainLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<NetworkModel>(Lifetime.Scoped);
            builder.Register<NgoMessenger>(Lifetime.Scoped);
            builder.RegisterEntryPoint<NetworkSessionStartup>();
        }
    }
}
