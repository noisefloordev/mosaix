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
        #pragma multi_compile __ TEXTURE_MASKING
        #pragma multi_compile __ SPHERE_MASKING

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
#endif

#if SPHERE_MASKING
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
            float f = Alpha;

#if TEXTURE_MASKING
            f *= tex2D(MaskTex, i.uv);
#endif

#if SPHERE_MASKING
            {
                // If MASKING is enabled, use the mask matrix.
                float3 TransformedWorld = mul(MaskMatrix, float4(i.world, 1));

                // The distance between this fragment and the center of the mask control:
                float dist = distance(TransformedWorld, float3(0,0,0));

                float sphere = (dist - MaskSizeOuter) * MaskSizeFactor;
                f *= clamp(sphere, 0, 1);
            }
#endif

            // Sample the mosaic.
            fixed4 color1 = tex2D(MosaicTex, uv);

            // Sample the high-res texture to fade/mask to it.
            fixed4 color2 = tex2D(HighResTex, uv);

            // Blend between the mosaic and full texture.
            fixed4 color = color1*f + color2*(1-f);

            // Ignore transparency at the edges due to antialiasing.  In the high-res texture this happens from
            // MSAA, and since we're rendering with MSAA this will double-apply the antialiasing.  In the low-res
            // texture this can happen from downsampling, and we don't want the mosaic to be transparent because
            // it filtered in alpha.
            if(color.a > 0.001)
            {
                color.rgb /= color.a;
                color.a = 1;
            }
            return color;
        }
        ENDCG
    }
}

}
