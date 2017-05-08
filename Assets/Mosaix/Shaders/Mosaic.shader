// Disable Unity's horrifying automatic upgrade thing that modifies your
// source code without asking: UNITY_SHADER_NO_UPGRADE

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
        #pragma multi_compile __ SHOW_MASK

        #include "UnityCG.cginc"
        #include "UnityCompat.cginc"

        // This receives textures of the full resolution screen, and a downscaled version to sample
        // the mosaic.
        //
        // There are two ways to get the screen coordinates of a fragment: VPOS in the fragment
        // shader, and ComputeScreenPos from the vertex coordinates.  During earlier testing, ComputeScreenPos
        // gave rounding artifacts that were worked around in VPOS, but this isn't happening now.  VPOS
        // is broken in all versions of Unity between 5.0 and 5.5 (it doesn't take the viewport into account),
        // so ComputeScreenPos is preferred.  If the rounding issues crop up again, USE_VPOS can be defined
        // to turn it back on.
// #if UNITY_VERSION >= 560
// #define USE_VPOS
// #endif

        struct appdata_t {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f {
            float3 world : TEXCOORD;
            float2 uv : TEXCOORD1;
#ifndef USE_VPOS
            float4 screenPos : TEXCOORD2;
#endif
        };

        sampler2D MosaicTex;

        sampler2D HighResTex;
        float Alpha;

        float4x4 FullTextureMatrix;
        float4x4 MosaicTextureMatrix;

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

#ifndef USE_VPOS
            // We can calculate the screen position with ComputeScreenPos(vertex), but this causes odd
            // rounding error at the boundary between mosaic pixels.  Instead, use VPOS.
            o.screenPos = ComputeScreenPos(vertex);
#endif

            return o;
        }

#ifdef USE_VPOS
        half4 frag(v2f i, UNITY_VPOS_TYPE screenPos : VPOS) : SV_Target
        {
            float2 ScreenSpaceUV = screenPos / _ScreenParams.xy;
#else
        half4 frag(v2f i) : SV_Target
        {
            float2 ScreenSpaceUV = i.screenPos / i.screenPos.w;
#endif
            // Sample the mosaic texture in screen space.  FullUV is the texture coordinates in
            // the full resolution texture (not shifted by the offset), and MosaicUV is the texture
            // coordinates in the mosaic (shifted by the offset).  If OffsetX/OffsetY are 0,
            // these are the same.
            float2 FullUV = mul(FullTextureMatrix, float4(ScreenSpaceUV, 0, 1));
            float2 MosaicUV = mul(MosaicTextureMatrix, float4(ScreenSpaceUV, 0, 1));

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

#if SHOW_MASK
            return half4(f, f, f, 1);
#endif

            // Sample the mosaic.
            half4 color1 = tex2D(MosaicTex, MosaicUV);

            // Sample the high-res texture to fade/mask to it.
            half4 color2 = tex2D(HighResTex, FullUV);

            // Blend between the mosaic and full texture.
            half4 color = color1*f + color2*(1-f);

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
