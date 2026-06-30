using MiniGame.TapGame;
using VContainer;
using VContainer.Unity;

namespace MiniGame.Injector
{
    // Inspector で parentReference に CommonLifetimeScope を設定すること
    public class MiniGameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<TapGameModel>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<MiniGameHostPresenter>().AsSelf();
        }
    }
}
