using Main.Board;
using Main.Money;
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

            // 所持金（お金マス・将来のミニゲーム報酬で増減）。
            builder.Register<MoneyModel>(Lifetime.Scoped);

            builder.Register<BoardModel>(Lifetime.Scoped);
            // 陣地マスの占拠状態（過半数占拠で勝敗が決まる）。盤面 index 一覧は BoardPresenter が Initialize で渡す。
            builder.Register<TerritoryModel>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<BoardPresenter>().AsSelf();
        }
    }
}
