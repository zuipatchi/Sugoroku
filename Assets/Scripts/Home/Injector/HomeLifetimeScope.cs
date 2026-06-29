using Home.Presenter;
using VContainer;
using VContainer.Unity;

namespace Home.Injector
{
    public class HomeLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<HomePresenter>().AsSelf();
        }
    }
}
