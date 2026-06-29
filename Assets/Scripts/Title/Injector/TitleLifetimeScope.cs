using Title.GameStartButton;
using Title.Sound;
using VContainer;
using VContainer.Unity;

namespace Title.Injector
{
    public class TitleLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<GameStartButtonPresenter>().AsSelf();
            builder.RegisterEntryPoint<AudioManager>(Lifetime.Scoped).AsSelf();
        }
    }
}
