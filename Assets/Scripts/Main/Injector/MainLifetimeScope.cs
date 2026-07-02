using Main.Board;
using Main.Roulette;
using Main.Turn;
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

            // ターン進行（参加者・手番・オーケストレーション）。
            builder.Register<GameParticipants>(Lifetime.Scoped);
            builder.Register<TurnModel>(Lifetime.Scoped);
            builder.RegisterEntryPoint<GameFlowController>();

            builder.Register<RouletteModel>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<RoulettePresenter>().AsSelf();

            builder.Register<BoardModel>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<BoardPresenter>().AsSelf();

            builder.RegisterComponentInHierarchy<MiniGameTriggerPresenter>().AsSelf();
        }
    }
}
