using System.Collections.Generic;
using System.Reflection;
using Main.Board;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public class BoardCatalogTests
    {
        private BoardCatalog _catalog;
        private BoardDefinition _alpha;
        private BoardDefinition _beta;

        [SetUp]
        public void SetUp()
        {
            _alpha = ScriptableObject.CreateInstance<BoardDefinition>();
            _alpha.name = "Alpha";
            _beta = ScriptableObject.CreateInstance<BoardDefinition>();
            _beta.name = "Beta";

            _catalog = ScriptableObject.CreateInstance<BoardCatalog>();
            SetBoards(_catalog, new List<BoardDefinition> { _alpha, _beta });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_alpha);
            Object.DestroyImmediate(_beta);
        }

        // private な _boards（SerializeField）をリフレクションで差し替える。
        private static void SetBoards(BoardCatalog catalog, List<BoardDefinition> boards)
        {
            FieldInfo field = typeof(BoardCatalog).GetField("_boards", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(catalog, boards);
        }

        [Test]
        public void Defaultは先頭のマップを返す()
        {
            Assert.AreSame(_alpha, _catalog.Default);
        }

        [Test]
        public void Findは識別子_資産名_に一致するマップを返す()
        {
            Assert.AreSame(_beta, _catalog.Find("Beta"));
        }

        [Test]
        public void Findは未登録の識別子なら先頭にフォールバックする()
        {
            Assert.AreSame(_alpha, _catalog.Find("Unknown"));
        }

        [Test]
        public void Findは空文字なら先頭にフォールバックする()
        {
            Assert.AreSame(_alpha, _catalog.Find(string.Empty));
        }

        [Test]
        public void 登録があればIsEmptyはfalse()
        {
            Assert.IsFalse(_catalog.IsEmpty);
        }

        [Test]
        public void 空カタログのDefaultはnullでIsEmptyはtrue()
        {
            BoardCatalog empty = ScriptableObject.CreateInstance<BoardCatalog>();
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsNull(empty.Default);
            Assert.IsNull(empty.Find("anything"));
            Object.DestroyImmediate(empty);
        }
    }
}
