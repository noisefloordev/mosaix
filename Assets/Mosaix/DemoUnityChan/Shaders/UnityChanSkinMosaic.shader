// This is a demo shader to mosaic Unity-chan's face, but not the toon outline around her
// face.
//
// * On the face materials, set Outline Thickness -> 0.  The disables the outline from being
// drawn into the mosaic.  That way, it won't be blurred into the mosaic, which would cause
// the mosaic blocks to darken near the outline, which we don't want.
// * This material is assigned to Mosaix's MosaicMaterial instead of the default one.  This
// draws the mosaic created by Mosaix by calling FX/Mosaix/MOSAIC, then draws the outline
// around it.
//
// This mosaics just the color of the face and not the outlines, with nice clean outlines that
// don't bleed into the mosaic.
Shader "UnityChanSkinWithMosaic"
{
	Properties
	{
		_Color ("Main Color", Color) = (1, 1, 1, 1)
		_EdgeThickness ("Outline Thickness", Float) = 1
				
		_MainTex ("Diffuse", 2D) = "white" {}
	}

	SubShader
	{
		Tags
		{
			"RenderType"="Opaque"
			"Queue"="Geometry"
			"LightMode"="ForwardBase"
		}

                // Call the outline pass in UnityChanShader.
		Pass
		{
			Cull Front
			ZTest Less
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
#include "../../../UnityChan/Models/UnityChanShader/Shader/CharaOutline.cg"
ENDCG
		}

                // This calls MosaicShader to draw the mosaic part.
		UsePass "FX/Mosaix/MOSAIC"
        }
}

