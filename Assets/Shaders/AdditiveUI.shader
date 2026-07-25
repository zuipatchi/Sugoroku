// 勝利エフェクトの合成用シェーダー。エフェクト専用カメラで RenderTexture に描いたパーティクルを、
// uGUI の Screen Space Overlay Canvas 上の RawImage で UI Toolkit の前面に加算合成するために使う。
// Blend One One（加算）なので RenderTexture の黒背景（0,0,0）は画面に何も足さず（透明に見え）、
// 明るいパーティクルだけが盤面の上に光として乗る（docs/effects.md「2. 加算ブレンド」）。
Shader "Sugoroku/AdditiveUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                return tex2D(_MainTex, IN.texcoord) * IN.color;
            }
            ENDCG
        }
    }
}
