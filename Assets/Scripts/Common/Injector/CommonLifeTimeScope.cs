using Common.GameSession;
using Common.MiniGame;
using Common.Option;
using Common.SceneManagement;
using Common.SoundManagement;
using Common.Store;
using Common.Transition;
using VContainer;
using VContainer.Unity;

namespace Common.Injector
{
    public class CommonLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<GameSessionModel>(Lifetime.Singleton).AsSelf();
            builder.RegisterEntryPoint<ModalStore>(Lifetime.Singleton).AsSelf();
            builder.RegisterComponentInHierarchy<OptionPresenter>().AsSelf();
            builder.RegisterEntryPoint<OptionModel>(Lifetime.Singleton).AsSelf();
            builder.RegisterComponentInHierarchy<SoundPlayer>().AsSelf();
            builder.RegisterEntryPoint<SoundStore>(Lifetime.Singleton).AsSelf();
            builder.RegisterComponentInHierarchy<TransitionPresenter>().AsSelf();
            builder.RegisterComponentInHierarchy<SceneTransitioner>().AsSelf();
            builder.Register<MiniGameSessionModel>(Lifetime.Singleton).AsSelf();
            builder.Register<MiniGameLauncher>(Lifetime.Singleton).AsSelf();
        }
    }
}
