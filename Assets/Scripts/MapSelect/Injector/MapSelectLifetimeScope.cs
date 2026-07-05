using MapSelect.Presenter;
using VContainer;
using VContainer.Unity;

namespace MapSelect.Injector
{
    public class MapSelectLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<MapSelectPresenter>().AsSelf();
        }
    }
}
