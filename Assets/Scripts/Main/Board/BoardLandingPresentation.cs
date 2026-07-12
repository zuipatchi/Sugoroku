using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 着地演出のビュー。画面中央のマス画像ポップアップ（表示・フェードアウト）、
    /// お金マスの増減額の浮遊テキスト、陣地マスの旗トゥイーン（中央表示→マスへ縮小移動）を担う。
    /// UI 要素（CellPopup / FlagPopup / MoneyFloat）の表示制御だけを持ち、
    /// ゲームロジック（お金加算・占拠確定・勝者判定）と「どの演出をいつ呼ぶか」の統括は
    /// <see cref="BoardPresenter"/> 側に残す。
    /// </summary>
    public sealed class BoardLandingPresentation
    {
        private readonly VisualElement _cellPopup;
        private readonly VisualElement _flagPopup;
        private readonly Label _moneyFloat;

        public BoardLandingPresentation(VisualElement cellPopup, VisualElement flagPopup, Label moneyFloat)
        {
            _cellPopup = cellPopup;
            _flagPopup = flagPopup;
            _moneyFloat = moneyFloat;
        }

        /// <summary>
        /// 画像 <paramref name="sprite"/> を画面中央のポップアップに拡大表示する（消さない）。
        /// マス画像とアイテム絵で共用する。表示できたら true。画像が null なら false を返し何もしない。
        /// 消すのは呼び出し側（<see cref="HideCellPopupAsync"/> / <see cref="ShowMoneyFloatAsync"/>）。
        /// </summary>
        public async UniTask<bool> ShowCellPopupAsync(Sprite sprite, CancellationToken ct)
        {
            if (_cellPopup == null || sprite == null)
            {
                return false;
            }

            _cellPopup.style.backgroundImage = new StyleBackground(sprite);
            _cellPopup.RemoveFromClassList("cell-popup--visible");
            _cellPopup.style.display = DisplayStyle.Flex;

            // 次フレームまで待ってから --visible を付け、縮小→等倍の transition を効かせる。
            await UniTask.NextFrame(ct);
            _cellPopup.AddToClassList("cell-popup--visible");
            return true;
        }

        /// <summary>表示中のマス画像ポップアップをフェードアウトして非表示にする。既に非表示なら何もしない。</summary>
        public async UniTask HideCellPopupAsync(CancellationToken ct)
        {
            if (_cellPopup == null || _cellPopup.style.display == DisplayStyle.None)
            {
                return;
            }
            // 等倍→縮小のフェードアウト（USS transition）ぶん待ってから非表示にする。
            _cellPopup.RemoveFromClassList("cell-popup--visible");
            await UniTask.Delay(TimeSpan.FromSeconds(0.2f), cancellationToken: ct);
            _cellPopup.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// お金マスの増減額を画面中央から上へ浮かび上がらせながらフェードアウトさせる演出。
        /// <paramref name="delta"/> が正なら「+ n」を緑、負なら「- n」を赤で表示する。0 なら何もしない。
        /// <paramref name="hidePopup"/> が true なら、表示中のマス画像ポップアップを
        /// 浮遊テキストが消えるのと同じタイミングでフェードアウトさせる。
        /// </summary>
        public async UniTask ShowMoneyFloatAsync(int delta, bool hidePopup, float duration, CancellationToken ct)
        {
            if (_moneyFloat == null || delta == 0)
            {
                // 浮遊テキストが出せない場合でも、保持していた画像は消す。
                if (hidePopup)
                {
                    await HideCellPopupAsync(ct);
                }
                return;
            }

            bool up = delta > 0;
            _moneyFloat.text = up ? $"+ ${delta}" : $"- ${-delta}";
            _moneyFloat.EnableInClassList("money-float--up", up);
            _moneyFloat.EnableInClassList("money-float--down", !up);
            _moneyFloat.style.display = DisplayStyle.Flex;

            // 開始位置はポップ画像の中央やや下（中央 top:50% + 画像高さの一部）。画像が無ければ中央から。
            float startY = hidePopup && _cellPopup != null ? _cellPopup.resolvedStyle.height * 0.1f : 0f;
            const float rise = 170f;
            // 画像ポップアップのフェードアウト（USS transition）ぶん手前で消し始め、テキストと同時に消えるようにする。
            const float PopupFadeLead = 0.2f;
            bool popupHideStarted = false;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 前半は不透明のまま読ませ、後半でフェードアウトする。
                _moneyFloat.style.opacity = t < 0.4f ? 1f : 1f - (t - 0.4f) / 0.6f;
                // ポップ画像の底から上へ上昇する。
                _moneyFloat.style.translate = new Translate(0f, startY - rise * t);

                // 浮遊テキストが消えきるのに合わせて画像もフェードアウトを開始する。
                if (hidePopup && !popupHideStarted && elapsed >= duration - PopupFadeLead)
                {
                    popupHideStarted = true;
                    _cellPopup?.RemoveFromClassList("cell-popup--visible");
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _moneyFloat.style.display = DisplayStyle.None;
            _moneyFloat.style.opacity = 0f;
            _moneyFloat.style.translate = new Translate(0f, 0f);

            // フェードが終わった画像を非表示にする。
            if (hidePopup && _cellPopup != null)
            {
                _cellPopup.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// 陣地マス着地の旗演出。<paramref name="flag"/> を画面中央に 1 秒表示してから、
        /// 対象の陣地マス <paramref name="targetCell"/> へ縮小移動して重ね、
        /// 重なった瞬間に <paramref name="onLanded"/>（占拠確定＝マス画像が旗に替わる）を呼ぶ。
        /// 旗が未ロード（null）のときは演出をスキップして <paramref name="onLanded"/> だけ呼ぶ。
        /// </summary>
        public async UniTask PlayTerritoryFlagSequenceAsync(
            Sprite flag, VisualElement targetCell, Action onLanded, CancellationToken ct)
        {
            VisualElement root = _flagPopup?.parent;
            if (_flagPopup == null || root == null || flag == null)
            {
                onLanded();
                return;
            }

            // 表示・移動の基準になる座標（root ローカル）。
            Vector2 center = new(root.contentRect.width * 0.5f, root.contentRect.height * 0.5f);

            _flagPopup.style.backgroundImage = new StyleBackground(flag);
            SetFlagTransform(center, 0.55f, 0f);
            _flagPopup.style.display = DisplayStyle.Flex;

            // ① 中央にポップイン（拡大＋フェードイン）→ 1.0 秒ホールド。
            await AnimateFlagAsync(center, center, 0.55f, 1f, 0f, 1f, 0.15f, false, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(1f), cancellationToken: ct);

            // ② 対象の陣地マス中心へ移動しつつ、マス幅に合わせて縮小する。
            Vector2 target = center;
            float targetScale = 0.3f;
            if (targetCell != null)
            {
                target = root.WorldToLocal(targetCell.worldBound.center);
                float cellWidth = targetCell.resolvedStyle.width;
                if (cellWidth > 1f)
                {
                    // 旗ポップの基準サイズは USS の 200px。マス幅に収まる倍率へ縮める。
                    targetScale = Mathf.Clamp(cellWidth / 200f, 0.1f, 1f);
                }
            }
            await AnimateFlagAsync(center, target, 1f, targetScale, 1f, 1f, 0.5f, true, ct);

            // ③ マスに重なったところで占拠を確定（マス画像が旗に替わる）→ 旗ポップをフェードアウト。
            onLanded();
            await AnimateFlagAsync(target, target, targetScale, targetScale, 1f, 0f, 0.2f, false, ct);

            _flagPopup.style.display = DisplayStyle.None;
            _flagPopup.style.opacity = 0f;
        }

        /// <summary>旗ポップの中心位置（root ローカル px）・拡大率・不透明度をまとめて設定する。</summary>
        private void SetFlagTransform(Vector2 position, float scale, float opacity)
        {
            _flagPopup.style.left = position.x;
            _flagPopup.style.top = position.y;
            _flagPopup.style.scale = new Scale(new Vector2(scale, scale));
            _flagPopup.style.opacity = opacity;
        }

        /// <summary>
        /// 旗ポップを <paramref name="duration"/> 秒かけて位置・拡大率・不透明度で補間する。
        /// <paramref name="easeInOut"/> が true なら smoothstep、false なら線形。毎フレーム駆動。
        /// </summary>
        private async UniTask AnimateFlagAsync(
            Vector2 from, Vector2 to, float scaleFrom, float scaleTo,
            float opacityFrom, float opacityTo, float duration, bool easeInOut, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float e = easeInOut ? Mathf.SmoothStep(0f, 1f, t) : t;
                Vector2 position = Vector2.LerpUnclamped(from, to, e);
                SetFlagTransform(position, Mathf.LerpUnclamped(scaleFrom, scaleTo, e), Mathf.Lerp(opacityFrom, opacityTo, e));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetFlagTransform(to, scaleTo, opacityTo);
        }
    }
}
