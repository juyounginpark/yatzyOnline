Shader "Custom/RadialFill"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Fill Amount", Range(0,1)) = 1
        _SpriteRect ("Sprite Rect", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Fill;
            float4 _SpriteRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;

                // 스프라이트 rect 기준 정규화 UV (아틀라스 대응)
                float2 normUV = (i.uv - _SpriteRect.xy) / _SpriteRect.zw;
                float2 c = normUV - 0.5;

                // 상단 12시 기준, 반시계 방향 각도 (오른쪽부터 사라짐)
                float angle = atan2(-c.x, c.y);
                if (angle < 0.0) angle += 6.28318530718;
                float fill01 = angle / 6.28318530718;

                if (fill01 > _Fill) discard;

                return col;
            }
            ENDCG
        }
    }
}
