Shader "Hidden/Mosaix/ExpandEdges" {
Properties {
    _MainTex ("Base (RGB)", 2D) = "white" {}
    PixelUVStep ("UV Step", Vector) = (1,1,1,1)
}

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
        
        // We're blitting from the full-resolution isolated layer to a low-resolution mosaic
        // layer.  The alpha of our output won't be used.
        //
        // If we just sample a single value from the source texture, we'll sample values outside
        // of any object in the layer, which will be completely transparent and have a meaningless
        // color value.  Since we're generating a lower-resolution mosaic texture, these values will
        // creep in at the boundaries of objects, which looks ugly.
        //
        // Fix this by searching nearby pixels for a non-transparent pixel.  If the pixel we'd like
        // to sample is completely transparent, sample the eight neighboring pixels to try to find
        // one that isn't transparent.  If they're all transparent then we're more than one (mosaic-
        // resolution) pixel away from anything, so this pixel shouldn't be drawn.
        fixed4 GetPixel(v2f i) : SV_Target
        {
        #define CHECK if(col.a > 0.25f) return col;
            fixed4 col;

            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x *  0, PixelUVStep.x * 0));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * -1, PixelUVStep.x * 0));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * +1, PixelUVStep.x * 0));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x *  0, PixelUVStep.x * -1));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x *  0, PixelUVStep.x * +1));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * -1, PixelUVStep.x * -1));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * +1, PixelUVStep.x * -1));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * -1, PixelUVStep.x * +1));
            CHECK;
            col = tex2D(_MainTex, i.texcoord + float2(PixelUVStep.x * +1, PixelUVStep.x * +1));

            // If we don't find any non-transparent pixels, return an arbitrary transparent pixel.
            return col;
        }

        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 col = GetPixel(i);
            return col;
        }
        ENDCG
    }
}

}
