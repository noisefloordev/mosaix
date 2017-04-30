// This is used by MosaixEditor.cs to display textures.
Shader "Hidden/Mosaix/EditorTextureDisplay" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
}

SubShader {
    Blend One Zero

    Pass {  
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 2.0
        
        #include "UnityCG.cginc"
        #pragma multi_compile DISP_NORMAL DISP_UNPREMULTIPLY DISP_ALPHA_ONLY DISP_WITHOUT_ALPHA

        struct appdata_t {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        sampler2D _MainTex;

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.texcoord = v.texcoord;
            return o;
        }
        
        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 col = tex2D(_MainTex, i.texcoord);
#if DISP_NORMAL
            col.rgb *= col.a;
#elif DISP_ALPHA_ONLY
            col = fixed4(col.a,col.a,col.a,1);
#elif DISP_WITHOUT_ALPHA
            col.a = 1;
#elif DISP_UNPREMULTIPLY
            if(col.a > 0.01)
                col.rgb /= col.a;
            col.a = 1;
#endif
            return col;
        }
        ENDCG
    }
}

}
