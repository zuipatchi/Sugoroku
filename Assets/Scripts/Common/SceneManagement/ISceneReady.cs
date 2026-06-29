using System.Threading;
using Cysharp.Threading.Tasks;

namespace Common.SceneManagement
{
    public interface ISceneReady
    {
        UniTask ReadyAsync(CancellationToken ct);
    }
}
