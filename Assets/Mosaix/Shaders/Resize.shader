// Disable Unity's horrifying automatic upgrade thing that modifies your
// source code without asking: UNITY_SHADER_NO_UPGRADE

Shader "Hidden/Mosaix/Resize" {

Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
}

SubShader {
    ZWrite Off
    ZTest Off

    Pass {  
        Blend One Zero

        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 2.0
        
        #include "UnityCG.cginc"
        #include "UnityCompat.cginc"

        struct appdata_t {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        sampler2D _MainTex;
        float2 UVStart;
        float2 UVStep;
        int Samples;
        float SampleFactor;
        

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.texcoord = v.texcoord;
            return o;
        }
        
        half4 frag(v2f i) : SV_Target
        {
            // Accumulate into a float, since a half doesn't have enough precision if we're sampling
            // a lot of pixels.
            float4 result = float4(0,0,0,0);
            for(int x = 0; x < Samples; ++x)
            {
                float2 uv = i.texcoord + UVStart + UVStep*x;
                result += tex2D(_MainTex, uv);
            }
            result *= SampleFactor;
            return half4(result);
        }    
                
        ENDCG
    }
}

}

