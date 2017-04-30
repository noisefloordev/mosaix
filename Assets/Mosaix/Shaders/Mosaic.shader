Shader "FX/Mosaix" {

Properties {
}

SubShader {
    Tags {"Queue"="Transparent"}

    // The output is premultiplied, so the source factor is One rather than SrcAlpha.
    Blend One OneMinusSrcAlpha

    Pass {  
        Name "MOSAIC"
    
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #pragma multi_compile __ TEXTURE_MASKING SPHERE_MASKING

        #include "UnityCG.cginc"

        struct appdata_t {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f {
            float3 world : TEXCOORD;
            float2 uv : TEXCOORD1;
            // float4 screenPos : SCREENPOS;
        };

        sampler2D MosaicTex;

        sampler2D HighResTex;
        float Alpha;

#if TEXTURE_MASKING
        sampler2D MaskTex;
#elif SPHERE_MASKING
        float4x4 MaskMatrix;
        float MaskSizeOuter;
        float MaskSizeFactor;
#endif

        v2f vert(appdata_t v, out float4 vertex : SV_POSITION)
        {
            v2f o;
            vertex = UnityObjectToClipPos(v.vertex);
            o.world = mul(unity_ObjectToWorld, v.vertex);
            o.uv = v.uv;

            // We can calculate the screen position with ComputeScreenPos(vertex), but this causes odd
            // rounding error at the boundary between mosaic pixels.  Instead, use VPOS.
            // o.screenPos = ComputeScreenPos(vertex);
            return o;
        }

        fixed4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
        {
            // Sample the mosaic texture in screen space.
            float2 uv = screenPos.xy / _ScreenParams.xy;
            fixed4 color1 = tex2D(MosaicTex, uv);

            // The low-resolution mosaic texture has transparency from being filtered down, which we need
            // to ignore or else the output will look transparent near edges.
            // XXX: Should we do this in the ExpandEdges pass, so we only have to do it at the lower resolution?
            if(color1.a > 0.001)
            {
                color1.rgb /= color1.a;
                color1.a = 1;
            }

            // If FADING is set, we'll sample the high-resolution texture as well, which allows us to fade
            // between mosaiced and non-mosaiced.
            float f = 1;

#if TEXTURE_MASKING
            f = tex2D(MaskTex, i.uv);
#elif SPHERE_MASKING
            {
                // If MASKING is enabled, use the mask matrix.
                float3 TransformedWorld = mul(MaskMatrix, float4(i.world, 1));

                // The distance between this fragment and the center of the mask control:
                float dist = distance(TransformedWorld, float3(0,0,0));

                f = (dist - MaskSizeOuter) * MaskSizeFactor;
                f = clamp(f, 0, 1);
            }
#endif

            f *= Alpha;

            fixed4 color2 = tex2D(HighResTex, uv);
            fixed4 color = color1*f + color2*(1-f);
            return color;
        }
        ENDCG
    }
}

}
