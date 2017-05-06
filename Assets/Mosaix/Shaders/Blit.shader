// This is a simple blit texture, like Graphics.Blit.
Shader "Hidden/Mosaix/Blit" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
}

SubShader {
    ZWrite Off
    ZTest Off
    Blend One Zero

    Pass {  
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 2.0
        
        #include "UnityCG.cginc"

        struct appdata_t {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        sampler2D _MainTex;
        float4 _MainTex_ST;

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.texcoord = v.texcoord;
            return o;
        }
        
        half4 frag(v2f i) : SV_Target
        {
            return tex2D(_MainTex, i.texcoord);
        }
        ENDCG
    }
}

}
