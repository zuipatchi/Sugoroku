using System;
using UnityEngine;

namespace Main.Online
{
    /// <summary>
    /// <see cref="GameAction"/> と JSON 文字列の相互変換。ネットワーク層（<see cref="NgoMessenger"/>）は
    /// 文字列しか扱わないため、アクションの表現をここに閉じ込める。純粋関数なので EditMode テストの対象。
    /// </summary>
    public static class GameActionCodec
    {
        /// <summary>アクションを JSON 文字列にする。</summary>
        public static string Encode(GameAction action)
        {
            GameActionDto dto = new()
            {
                type = (int)action.Type,
                seat = action.Seat,
                args = action.ArgsCopy(),
                seq = action.Seq,
            };
            return JsonUtility.ToJson(dto);
        }

        /// <summary>
        /// JSON 文字列をアクションに戻す。壊れた JSON や未知の種別は false を返す
        /// （バージョン違いのクライアントから届いても落ちないようにする）。
        /// </summary>
        public static bool TryDecode(string json, out GameAction action)
        {
            action = default;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            GameActionDto dto;
            try
            {
                dto = JsonUtility.FromJson<GameActionDto>(json);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (dto == null || !Enum.IsDefined(typeof(GameActionType), dto.type))
            {
                return false;
            }

            action = new GameAction((GameActionType)dto.type, dto.seat, dto.args, dto.seq);
            return true;
        }

        // JsonUtility 用のシリアライズ DTO（JsonUtility は struct のトップレベル・可変長の型引数を扱えないため）。
        // seq は古い形式（seq 無し）の JSON でも 0＝GameAction.NoSeq に落ちるので後方互換で読める。
        [Serializable]
        private sealed class GameActionDto
        {
            public int type;
            public int seat;
            public int[] args;
            public int seq;
        }
    }
}
