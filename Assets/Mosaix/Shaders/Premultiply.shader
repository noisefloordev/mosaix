Shader "Hidden/Mosaix/Premultiply" {
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
        bool PremultipliedInput;

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.texcoord = v.texcoord;
            return o;
        }
        
        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 color = tex2D(_MainTex, i.texcoord);
            return fixed4(color.rgb * color.a, color.a);
        }
        ENDCG
    }
}

}
