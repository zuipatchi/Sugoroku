using VContainer;
using VContainer.Unity;

namespace MiniGame.Injector
{
    // Inspector で parentReference に CommonLifetimeScope を設定すること。
    // MiniGameLauncher / SoundStore / SoundPlayer は Common で登録済みのため親から解決する。
    public class MiniGameTestLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<MiniGameTestPresenter>().AsSelf();
        }
    }
}
