using MiniGame.OverlapGame;
using MiniGame.RaceGame;
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
            builder.Register<TapGamePlay>(Lifetime.Scoped);
            builder.Register<RaceGameModel>(Lifetime.Scoped);
            builder.Register<RaceGamePlay>(Lifetime.Scoped);
            builder.Register<OverlapGameModel>(Lifetime.Scoped);
            builder.Register<OverlapGamePlay>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<MiniGameHostPresenter>().AsSelf();
        }
    }
}
