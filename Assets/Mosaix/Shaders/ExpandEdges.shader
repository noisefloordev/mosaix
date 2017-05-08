// Disable Unity's horrifying automatic upgrade thing that modifies your
// source code without asking: UNITY_SHADER_NO_UPGRADE
Shader "Hidden/Mosaix/ExpandEdges" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
    PixelUVStep ("UV Step", Vector) = (1,1,1,1)
}

/*
 * After we downscale the image, we have some transparent pixels around the edge, where the sampling
 * didn't pick up much or any color while downscaling but where we'll still be sampling the mosaic.
 * Fill those in with color from neighboring pixels.
 *
 * Some of these pixels may be very slightly opaque, with a very small but nonzero alpha value.  We
 * should also replace these colors, since premultiplied color with very small alpha values is poorly
 * defined.  For example, #FC0000 premultiplied with an alpha of 0.01 and then un-premultiplied won't
 * come back at #FC0000 since there isn't enough precision in the texture.
 *
 * We work around both of these with the same process, expanding the edge of the image outwards.  We
 * treat alpha as a confidence.  If a pixel's alpha is already 1 then we don't need to change it,
 * if it's 0.05 we should fill it in with neighboring colors, and if it's 0 then it should be replaced
 * entirely.
 */
SubShader {
    ZWrite Off
    ZTest Off

    // This shader doesn't blend with anything.  We just search for a color in the source texture
    // and copy it to the target.
    Blend One Zero

    Pass {  
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
        float4 PixelUVStep;
        float4 _MainTex_ST;

        v2f vert(appdata_t v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.texcoord = v.texcoord;
            return o;
        }
        
#define SAMPLE(X,Y) tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * X, PixelUVStep.y * Y))

        half4 GetPixel(v2f i) : SV_Target
        {
            // Sample the four neighboring pixels around this one and add them.  Note that
            // these values are premultiplied.
            half4 neighbors =
                SAMPLE(-1, 0) +
                SAMPLE(+1, 0) + 
                SAMPLE( 0,+1) +
                SAMPLE( 0,-1);

            // If we sampled two red pixels (1,0.0,1) and (0.5,0,0,0.5), that means the first pixel is
            // a 100% confidence red and the second pixel is a 50% confidence red.  (They're both #FF0000:
            // the second is 0.5 only because it's premultiplied by its alpha value.)  Premultiplied
            // alpha already weighted each sample by its alpha for us, giving us (1.5,0,0,1.5).
            //
            // If the resulting alpha is greater than 1, then the color is superbright (greater than 1)
            // and we need to divide by alpha to bring it down to 1, giving us a color in the regular
            // range.  If alpha is less than 1 don't do this: the color is already in range, and if we divide
            // by alpha we'll snap the confidence up to 1.
            neighbors /= max(1, neighbors.a);

            // Sample the main pixel.
            half4 col = SAMPLE(0,0);

            // Mix the neighboring pixels with the pixel itself.  If the color already has an alpha of 1
            // then this will discard the neighbor samples.  If it has an alpha of 0 then it'll use the
            // neighbor samples exclusively.
            //
            // This is just regular color mixing.  We don't use lerp() since col is already multiplied
            // by col.a.
            return neighbors*(1-col.a) + col;
        }

        half4 frag(v2f i) : SV_Target
        {
            half4 col = GetPixel(i);
            return col;
        }
        ENDCG
    }
}

}
