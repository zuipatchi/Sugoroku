using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Main.Board
{
    /// <summary>
    /// 着地演出のビュー。画面中央のマス画像ポップアップ（表示・フェードアウト）とその下に出すマスの文言、
    /// 浮遊テキスト（お金マスの増減額・進む／戻るマスの移動マス数）、
    /// 陣地マスの旗トゥイーン（中央表示→マスへ縮小移動）を担う。
    /// UI 要素（CellPopup / CellMessage / FlagPopup / MoneyFloat＝浮遊テキストはお金と移動で共用）の
    /// 表示制御だけを持ち、ゲームロジック（お金加算・占拠確定・勝者判定）と「どの演出をいつ呼ぶか」の統括は
    /// <see cref="BoardPresenter"/> 側に残す。
    /// </summary>
    public sealed class BoardLandingPresentation
    {
        private const string CellPopupVisibleClass = "cell-popup--visible";
        private const string CellMessageVisibleClass = "cell-message--visible";
        // 「画像を出さない着地では画面中央へ寄せる」は位置決めなので、ピルではなくそれを載せる行に付ける。
        private const string CellMessageAloneClass = "cell-message-row--alone";

        private readonly VisualElement _cellPopup;
        private readonly VisualElement _flagPopup;
        private readonly Label _moneyFloat;
        private readonly Label _cellMessage;
        // 文言のピルを載せる全幅の行（位置決めを持つ親）。ピルの折り返しを含めた高さを正しく測るために挟んである。
        private readonly VisualElement _cellMessageRow;

        // 中央ポップアップの表示状態。画像と文言は片方だけ出ることがある（画像未配置・文言だけ見せるマス）ので
        // 別々に覚え、消すときに出していたものだけを対象にする。
        private bool _popupShown;
        private bool _messageShown;

        public BoardLandingPresentation(
            VisualElement cellPopup,
            VisualElement flagPopup,
            Label moneyFloat,
            Label cellMessage,
            VisualElement cellMessageRow)
        {
            _cellPopup = cellPopup;
            _flagPopup = flagPopup;
            _moneyFloat = moneyFloat;
            _cellMessage = cellMessage;
            _cellMessageRow = cellMessageRow;
        }

        /// <summary>
        /// 画像 <paramref name="sprite"/> を画面中央のポップアップに拡大表示する（消さない）。
        /// マス画像とアイテム絵で共用する。表示できたら true。画像が null なら false を返し何もしない。
        /// 消すのは呼び出し側（<see cref="HideCellPopupAsync"/> / <see cref="ShowMoneyFloatAsync"/>）。
        /// </summary>
        public UniTask<bool> ShowCellPopupAsync(Sprite sprite, CancellationToken ct)
        {
            return ShowCellPopupAsync(sprite, null, ct);
        }

        /// <summary>
        /// マスの文言 <paramref name="message"/> だけを画面中央に拡大表示する（画像は出さない・消さない）。
        /// アイテム・ミニゲームのように、このあと専用の演出へ分岐して画像を出さない着地で使う。
        /// 表示できたら true。消すのは呼び出し側（<see cref="HideCellPopupAsync"/>）。
        /// </summary>
        public UniTask<bool> ShowCellMessageAsync(string message, CancellationToken ct)
        {
            return ShowCellMessageAsync(message, true, ct);
        }

        /// <summary>
        /// マスの文言 <paramref name="message"/> だけを拡大表示する（画像は出さない・消さない）。
        /// <paramref name="centered"/> が false なら画面中央ではなくマス画像と同じ「中央の少し下」に置く。
        /// 陣地マスのように、中央を別の演出（旗ポップ）が使っていて同時に見せたいときに使う。
        /// </summary>
        public UniTask<bool> ShowCellMessageAsync(string message, bool centered, CancellationToken ct)
        {
            return ShowCellPopupAsync(null, message, centered, ct);
        }

        /// <summary>
        /// 画像 <paramref name="sprite"/> と、その下に出すマスの文言 <paramref name="message"/> を
        /// 画面中央のポップアップに拡大表示する（消さない）。どちらか片方だけでも表示でき
        /// （画像が未配置のマス・文言だけ見せるマス）、どちらも出せなければ false を返す。
        /// 消すのは呼び出し側（<see cref="HideCellPopupAsync"/> / <see cref="ShowMoneyFloatAsync"/>）で、
        /// 画像と文言は常に足並みをそろえて出入りする。
        /// </summary>
        public UniTask<bool> ShowCellPopupAsync(Sprite sprite, string message, CancellationToken ct)
        {
            return ShowCellPopupAsync(sprite, message, true, ct);
        }

        /// <summary>
        /// <see cref="ShowCellPopupAsync(Sprite, string, CancellationToken)"/> の本体。
        /// <paramref name="centerWhenAlone"/> は「画像が出ず文言だけになったときに画面中央へ寄せるか」。
        /// 画像と一緒に出すときは常に画像の下なのでこの指定は効かない。
        /// </summary>
        private async UniTask<bool> ShowCellPopupAsync(
            Sprite sprite, string message, bool centerWhenAlone, CancellationToken ct)
        {
            bool showPopup = _cellPopup != null && sprite != null;
            bool showMessage = _cellMessage != null && !string.IsNullOrEmpty(message);
            if (!showPopup && !showMessage)
            {
                return false;
            }

            if (showPopup)
            {
                _cellPopup.style.backgroundImage = new StyleBackground(sprite);
                _cellPopup.RemoveFromClassList(CellPopupVisibleClass);
                _cellPopup.style.display = DisplayStyle.Flex;
                _popupShown = true;
            }
            if (showMessage)
            {
                _cellMessage.text = message;
                _cellMessage.RemoveFromClassList(CellMessageVisibleClass);
                // 画像を出さない着地（アイテム・ミニゲーム）は文言だけが宙に浮くので、
                // 画像の下ではなく画面中央へ寄せる（位置決めは親の行が持つ）。
                // 陣地マスは中央を旗ポップが使うので、寄せずに画像と同じ「中央の少し下」に置く。
                _cellMessageRow?.EnableInClassList(CellMessageAloneClass, centerWhenAlone && !showPopup);
                _cellMessage.style.display = DisplayStyle.Flex;
                _messageShown = true;
            }

            // 次フレームまで待ってから --visible を付け、縮小→等倍の transition を効かせる。
            await UniTask.NextFrame(ct);
            if (showPopup)
            {
                _cellPopup.AddToClassList(CellPopupVisibleClass);
            }
            if (showMessage)
            {
                _cellMessage.AddToClassList(CellMessageVisibleClass);
            }
            return true;
        }

        /// <summary>
        /// 表示中の中央ポップアップ（マス画像・文言）をフェードアウトして非表示にする。
        /// 既に非表示なら何もしない。
        /// </summary>
        public async UniTask HideCellPopupAsync(CancellationToken ct)
        {
            if (!_popupShown && !_messageShown)
            {
                return;
            }
            // 等倍→縮小のフェードアウト（USS transition）ぶん待ってから非表示にする。
            BeginHideCellPopup();
            await UniTask.Delay(TimeSpan.FromSeconds(0.2f), cancellationToken: ct);
            FinishHideCellPopup();
        }

        /// <summary>中央ポップアップ（マス画像・文言）のフェードアウトを始める（消えるのは transition のぶん後）。</summary>
        private void BeginHideCellPopup()
        {
            if (_popupShown)
            {
                _cellPopup.RemoveFromClassList(CellPopupVisibleClass);
            }
            if (_messageShown)
            {
                _cellMessage.RemoveFromClassList(CellMessageVisibleClass);
            }
        }

        /// <summary>フェードアウトし終えた中央ポップアップ（マス画像・文言）を非表示にする。</summary>
        private void FinishHideCellPopup()
        {
            if (_popupShown)
            {
                _cellPopup.style.display = DisplayStyle.None;
                _popupShown = false;
            }
            if (_messageShown)
            {
                _cellMessage.style.display = DisplayStyle.None;
                _messageShown = false;
            }
        }

        /// <summary>
        /// お金マスの増減額を浮遊テキストで見せる演出。
        /// <paramref name="delta"/> が正なら「+ $n」を緑、負なら「- $n」を赤で表示する。0 なら何もしない。
        /// </summary>
        public UniTask ShowMoneyFloatAsync(int delta, bool hidePopup, float duration, CancellationToken ct)
        {
            string text = delta > 0 ? $"+ ${delta}" : $"- ${-delta}";
            return ShowFloatTextAsync(text, delta, hidePopup, duration, ct);
        }

        /// <summary>
        /// 進む／戻るマスの移動マス数を、お金と同じ浮遊テキストで見せる演出。
        /// <paramref name="steps"/> が正なら「+ n マス」を緑、負なら「- n マス」を赤で表示する。0 なら何もしない。
        /// </summary>
        public UniTask ShowMoveFloatAsync(int steps, bool hidePopup, float duration, CancellationToken ct)
        {
            string text = steps > 0 ? $"+ {steps}マス" : $"- {-steps}マス";
            return ShowFloatTextAsync(text, steps, hidePopup, duration, ct);
        }

        /// <summary>
        /// 浮遊テキストの共通演出。<paramref name="text"/> を画面中央から上へ浮かび上がらせながらフェードアウトさせる。
        /// <paramref name="amount"/> は色分け（正＝緑／負＝赤）と「0 なら何もしない」の判定にだけ使う。
        /// <paramref name="hidePopup"/> が true なら、表示中の中央ポップアップ（マス画像・文言）を
        /// 浮遊テキストが消えるのと同じタイミングでフェードアウトさせる。
        /// </summary>
        private async UniTask ShowFloatTextAsync(
            string text, int amount, bool hidePopup, float duration, CancellationToken ct)
        {
            if (_moneyFloat == null || amount == 0)
            {
                // 浮遊テキストが出せない場合でも、保持していた画像・文言は消す。
                if (hidePopup)
                {
                    await HideCellPopupAsync(ct);
                }
                return;
            }

            bool up = amount > 0;
            _moneyFloat.text = text;
            _moneyFloat.EnableInClassList("money-float--up", up);
            _moneyFloat.EnableInClassList("money-float--down", !up);
            _moneyFloat.style.display = DisplayStyle.Flex;

            // 開始位置はポップ画像の中央やや下（中央 top:50% + 画像高さの一部）。画像が無ければ中央から。
            float startY = _popupShown ? _cellPopup.resolvedStyle.height * 0.1f : 0f;
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

                // 浮遊テキストが消えきるのに合わせて画像・文言もフェードアウトを開始する。
                if (hidePopup && !popupHideStarted && elapsed >= duration - PopupFadeLead)
                {
                    popupHideStarted = true;
                    BeginHideCellPopup();
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            _moneyFloat.style.display = DisplayStyle.None;
            _moneyFloat.style.opacity = 0f;
            _moneyFloat.style.translate = new Translate(0f, 0f);

            // フェードが終わった画像・文言を非表示にする。
            if (hidePopup)
            {
                FinishHideCellPopup();
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
