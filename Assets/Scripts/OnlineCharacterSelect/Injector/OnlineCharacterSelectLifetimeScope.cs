using OnlineCharacterSelect.Presenter;
using OnlineCharacterSelect.Sync;
using VContainer;
using VContainer.Unity;

namespace OnlineCharacterSelect.Injector
{
    // Inspector で parentReference に CommonLifetimeScope を設定すること。
    public class OnlineCharacterSelectLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<CharacterLobbySync>(Lifetime.Scoped).AsSelf();
            builder.RegisterComponentInHierarchy<OnlineCharacterSelectPresenter>().AsSelf();
        }
    }
}
