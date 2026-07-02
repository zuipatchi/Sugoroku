using System.Collections;
using System.Reflection;
using Common.SceneManagement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Tests.PlayMode
{
    public class TitleSceneTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Title シーンは TitleVideoPresenter が動画を再生する。テスト環境で動画を再生できないと
            // Unity の VideoPlayer がネイティブに Error ログを吐き、それだけでテストが失敗扱いになる。
            // ここでは GameStartButton の挙動しか検証しないため、想定外ログでの失敗を無効化する
            // （ボタンが存在しない等の本当の不具合は各テストのアサーションで検出される）。
            LogAssert.ignoreFailingMessages = true;

            yield return SceneManager.LoadSceneAsync("Title", LoadSceneMode.Single);
            // Common シーンのロード完了を待つ
            yield return new WaitUntil(() => SceneManager.GetSceneByName("Common").isLoaded);
            // VContainer スコープビルド + DI 注入完了を待つ
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // static フィールドをリセットして次のテストで Common シーンが再ロードされるようにする
            typeof(CommonSceneLoader)
                .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ゲームスタートボタンがシーンに存在する()
        {
            Button button = FindGameStartButton();
            Assert.IsNotNull(button, "GameStartButton が見つかりません");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ゲームスタートボタンが初期状態で有効()
        {
            Button button = FindGameStartButton();
            Assert.IsNotNull(button, "GameStartButton が見つかりません");
            Assert.IsTrue(button.enabledSelf, "初期状態でボタンが無効になっています");
            yield return null;
        }

        [UnityTest]
        public IEnumerator クリック後にゲームスタートボタンが無効化される()
        {
            Button button = FindGameStartButton();
            Assert.IsNotNull(button, "GameStartButton が見つかりません");

            // NavigationSubmitEvent（Enter/Space キー相当）で Clickable を発火させる
            button.Focus();
            using var submitEvent = NavigationSubmitEvent.GetPooled();
            button.SendEvent(submitEvent);

            yield return null;

            Assert.IsFalse(button.enabledSelf, "クリック後にボタンが disabled になっていません");
        }

        // Title シーン内の UIDocument から GameStartButton を探す
        private static Button FindGameStartButton()
        {
            Scene titleScene = SceneManager.GetSceneByName("Title");
            foreach (GameObject root in titleScene.GetRootGameObjects())
            {
                foreach (UIDocument doc in root.GetComponentsInChildren<UIDocument>())
                {
                    Button button = doc.rootVisualElement?.Q<Button>("GameStartButton");
                    if (button != null)
                    {
                        return button;
                    }
                }
            }
            return null;
        }
    }
}
