using CharacterSelect.Presenter;
using VContainer;
using VContainer.Unity;

namespace CharacterSelect.Injector
{
    public class CharacterSelectLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<CharacterSelectPresenter>().AsSelf();
        }
    }
}
