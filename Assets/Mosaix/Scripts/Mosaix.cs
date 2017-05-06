using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Apply a mosaic to objects in a display layer.
//
// This renders the object to a texture, and then replaces the material on the object with
// one that samples the prerendered texture.  This works better with MSAA, since the object
// is being drawn normally (just with a different texture sampler) instead of comping the
// mosaic on top.
//
// This could also be made to replace a specific material on the objects, to apply a mosaic
// to a specific material but leave the rest of the mesh alone.
[AddComponentMenu("Effects/Mosaix")]
public class Mosaix: MonoBehaviour
{
    // The render layer for objects to mosaic.
    public int MosaicLayer;

    // The minimum number of mosaic blocks we want.  This will be adjusted upwards to keep the
    // blocks square.
    public float MosaicBlocks = 16;
    
    // If true, we'll render the mosaic texture with other objects casting shadows.  Turning this off may
    // be faster, but will result in the mosaic having no shadowing, which can cause it to be too bright
    // depending on the scene.
    public bool ShadowsCastOnMosaic = true;

    // If true, we'll render the mosaiced objects at full resolution, and then downscale that
    // texture to the low resolution mosaic texture.  This has a couple benefits:
    // 
    // - Lighting matches much better at high resolution.  Lowering the resolution loses shadow
    // details, which can make the mosaic texture not mesh as well.
    // - We have a full resolution texture, which allows us to mask the mosaic.
    //
    // If false, we'll render directly at the lower resolution, which is faster.
    public bool HighResolutionRender = true;

    public float Alpha = 1;

    // The number of ExpandEdges passes to perform.  This is normally only changed for debugging.
    // One pass is usually enough, but if we have RenderScale enabled we may need two passes.
    [System.NonSerialized]
    public int ExpandPasses = 2;

    // Sphere masking:
    public bool SphereMasking;

    // A sphere to define where to mosaic.  This sphere can be scaled, stretched and rotated
    // to adjust the shape of the mosaic.
    public GameObject MaskingSphere;

    // How far to fade the mosaic out around MaskingSphere.  At 0, the mosaic cuts off sharply.
    // At 1, we fade for the same size as MaskingSphere: if it's 10 world units, the fade ends
    // 20 world units away.
    public float MaskFade = 0.1f;

    // Texture masking:
    public bool TextureMasking;

    public Texture MaskingTexture;

    // Anchoring
    //
    // If this is set, we'll shift the mosaic so this transform always lies on the intersection of two
    // mosaic blocks.  This allows the mosaic to appear to follow an object as it moves and turns.
    public GameObject AnchorTransform;

    // If true, we'll scale the mosaic so it gets bigger when the anchor is closer to the camera
    // and smaller as it gets further away.
    public bool ScaleMosaicToAnchorDistance;

    // This shader copies the outermost edge of opaque pixels outwards.
    public Shader ExpandEdgesShader;
    private Material ExpandEdgesMaterial;

    // This material calls MosaicShader, which samples our low-resolution mosaic texture in screen space.
    // It can also be another material that calls the mosaic pass.
    public Material MosaicMaterial;

    // A simple shader just used to copy from one texture to another:
    public Shader BlitShader;
    private Material BlitMaterial;

    // The number of pixels to offset the mosaic.  OffsetPixelsX 5 will shift the center of the mosaic
    // right by 5 pixels in screen space.  OffsetPixelsY 5 will shift the mosaic down by 5 pixels.
    private float OffsetPixelsX, OffsetPixelsY;

    // The amount of extra screen space to render in the texture.  If this is 1, the texture is drawn at
    // exactly the size of the screen.  If 2, the texture is expanded by 2x.  The screen will still be
    // drawn at exactly the same size in the center of the texture, but we'll have more pixels outwards.
    //
    // Setting this to a slightly higher value gives us extra pixels to sample for the mosaic at the edges
    // of the screen.  This can reduce flicker due to bright parts of the object coming in and out of view,
    // by keeping them "in view" of the mosaic even though they're actually offscreen.
    //
    // This also helps avoid artifacts from OffsetPixelsX/OffsetPixelsY.  If we're shifting the mosaic
    // right by 10 pixels, then we want to have 10 pixels extra at the left to sample the left row of
    // mosaic.  If the object fills the camera, those 10 pixels can be offscreen and we may have no pixels
    // for that row.  ExpandEdges fixes this by expanding the next row outwards, but it can be conspicuous
    // when a whole row is missing.
    //
    // This shouldn't be set too high.  If this is 2 and the user is at 1080p, this will create a 2160p
    // texture.  Values around 1.1 are best, to only sample a little bit near the edge.
    public float RenderScale = 1.1f;

    // The RenderScale that we're actually applying.
    private float ActualRenderScaleX = 1, ActualRenderScaleY = 1;

    // If we're rendering a non-integer number of mosaic blocks, the mosaic is scaled down slightly in the
    // texture.  If we're rendering 3.75 mosaic blocks, we render into a 4-pixel texture as if it's 3.75
    // pixels wide, and this is the ratio we need to scale it.
    private float HorizontalMosaicRatio = 1, VerticalMosaicRatio = 1;

    // The real camera on the object this script is attached to.
    private Camera ThisCamera;

    // The camera we use to draw the isolated texture.  This is done with a separate camera that matches the
    // target camera, so we don't accidentally change settings on the main camera without putting them back.
    // A matching camera to this one:
    private Camera MosaicCamera;

    // This is an array of textures, starting at full size and progressively getting smaller towards the
    // mosaic size.
    public enum PassType
    {
        Render,
        Downscale,
        Expand
    };
    public class ImagePass
    {
        public ImagePass(RenderTexture tex, PassType type)
        {
            Texture = tex;
            Type = type;
        }
        public RenderTexture Texture;
        public PassType Type;
    };
    private List<ImagePass> Passes = new List<ImagePass>();

    private Dictionary<Renderer,Material[]> SavedMaterials = new Dictionary<Renderer,Material[]>();

#if UNITY_EDITOR
    // These are settings only used by MosaixEditor.
    [System.NonSerialized]
    public MosaixEditor.EditorSettings EditorSettings = new MosaixEditor.EditorSettings();

    void Reset()
    {
        // When the script is added in the editor, set the default shaders and mosaic material.
        ExpandEdgesShader = Shader.Find("Hidden/Mosaix/ExpandEdges");
        BlitShader = Shader.Find("Hidden/Mosaix/Blit");
        MosaicMaterial = (Material) AssetDatabase.LoadAssetAtPath("Assets/Mosaix/Shaders/Mosaic.mat", typeof(Material));
    }

    public List<RenderTexture> GetTexturePasses()
    {
        List<RenderTexture> TexturePasses = new List<RenderTexture>();

        for(int i = 0; i < Passes.Count; ++i)
            TexturePasses.Add(Passes[i].Texture);

        return TexturePasses;
    }
#endif

    static HashSet<Mosaix> EnabledMosaixScripts = new HashSet<Mosaix>();

    // This stores the configuration we used to set up textures.  If this changes, we know we need
    // to recreate our image passes.
    struct Setup
    {
        public int RenderWidth, RenderHeight;
        public float HorizontalMosaicBlocks;
        public float VerticalMosaicBlocks;
        public int ExpandPasses;
        public int AntiAliasing;
    };
    Setup CurrentSetup;

    void OnEnable()
    {
        EnabledMosaixScripts.Add(this);

        // If more than one of these scripts is attached to the same layer on the same camera, our
        // material replacement in OnPreRender and OnPostRender won't behave correctly.  This is
        // because if multiple scripts have PreRender and PostRenders, Unity calls them in linear
        // order instead of as a stack.  That is, instead of
        //
        // Script1.PreRender Script2.PreRender Script2.PostRender Script1.PostRender
        //
        // it'll call 
        // Script1.PreRender Script2.PreRender Script1.PostRender Script2.PostRender
        //
        // This makes it hard to push and pop render state properly.
        //
        // This probably isn't too useful and if it's happened it's probably unintentional.  Warn about
        // it, since it can cause confusing results.
        foreach(Mosaix script in EnabledMosaixScripts)
        {
            if(script == this || MosaicLayer != script.MosaicLayer || ThisCamera != script.ThisCamera ||
                MosaicLayer != script.MosaicLayer)
                continue;

            Debug.Log("Warning: Multiple Mosaix scripts have been applied to the same display layer " +
                    LayerMask.LayerToName(MosaicLayer) + " on camera " + ThisCamera + ".  This may not work.");
        }
    }

    void OnDisable()
    {
        EnabledMosaixScripts.Remove(this);
    }

    void Start()
    {
        ThisCamera = gameObject.GetComponent<Camera>();
        if(ThisCamera == null)
        {
            Debug.LogError("This script must be attached to a camera.");
            return;
        }

        // Create materials for our shaders.
        ExpandEdgesMaterial = new Material(ExpandEdgesShader);
        BlitMaterial = new Material(BlitShader);

        // Make a copy of the mosaic material.  We may be connected to a material used by multiple
        // instances of this script, and we need the properties we set to not affect the others.
        MosaicMaterial = new Material(MosaicMaterial);

        // Dynamically create a camera to render with.  We'll make this a child of this camera to keep
        // it from cluttering the scene, but this doesn't really matter.
        GameObject MosaicCameraGameObject = new GameObject("Mosaic camera (" + LayerMask.LayerToName(MosaicLayer) + ")");
        MosaicCameraGameObject.transform.SetParent(this.transform);
        MosaicCamera = MosaicCameraGameObject.AddComponent<Camera>();

        // Disable the helper camera, so we can render it manually, and disable its GameObject so it doesn't
        // show up in the editor viewport.
        MosaicCamera.enabled = false;
        MosaicCameraGameObject.SetActive(false);

        SetupTextures();
    }

    void OnDestroy()
    {
        // Make sure we destroy our textures immediately.
        ReleaseTextures();

        // Destroy the camera we created.
        if(MosaicCamera != null)
        {
            Destroy(MosaicCamera);
            MosaicCamera = null;
        }
    }    

    static float RoundToNearest(float val, float interval)
    {
	return Mathf.Floor((val + interval/2.0f)/interval) * interval;
    }

    private void SetupTextures()
    {
        int Width = ThisCamera.pixelWidth;
        int Height = ThisCamera.pixelHeight;

        // RenderScale is how much larger we should draw the scene than the viewport.  If this is 2, then
        // we'll draw a texture twice as wide and high as the viewport (usually much closer to 1).  We want
        // to be an integer number of pixels larger than the viewport, so the screen is an exact subset of
        // the texture we draw, so antialiasing stays the same.  If the screen is 100x100 and RenderScale
        // tells us to render at 103.5x103.5, round to the nearest even multiple of 2, 104x104x.
        {
            float ExtraPixelsX = RoundToNearest(Width * (RenderScale-1), 2);
            float ExtraPixelsY = RoundToNearest(Height * (RenderScale-1), 2);

            ActualRenderScaleX = (Width+ExtraPixelsX) / Width;
            ActualRenderScaleY = (Height+ExtraPixelsY) / Height;

            Width += (int) ExtraPixelsX;
            Height += (int) ExtraPixelsY;
        }

        // The number of actual horizontal and vertical blocks.  Scale this by RenderScale, so if the scale
        // is 2 (we're drawing a texture twice as big), we draw twice as many blocks and keep the blocks the
        // same size.
        float HorizontalMosaicBlocks = MosaicBlocks * ActualRenderScaleX;

        if(AnchorTransform != null && ScaleMosaicToAnchorDistance)
        {
            // Get the distance from the camera to the anchor, and scale the mosaic size by it.
            // Note that the mosaic alignment to AnchorTransform helps reduce flicker caused by
            // the mosaic resolution changing.
            Vector3 TransformPos = AnchorTransform.transform.position;
            Vector3 CameraPos = ThisCamera.transform.position;
            float Distance = Vector3.Distance(TransformPos, CameraPos);
            HorizontalMosaicBlocks = HorizontalMosaicBlocks * Distance;
        }

        float VerticalMosaicBlocks = HorizontalMosaicBlocks * ActualRenderScaleY;

        // Make sure neither dimension is zero, and avoid 1 since it avoids some visual issues that
        // I haven't tracked down.
        HorizontalMosaicBlocks = Math.Max(2, HorizontalMosaicBlocks);
        VerticalMosaicBlocks = Math.Max(2, VerticalMosaicBlocks);

        // MosaicBlocks is the number of mosaic blocks we want to display.  However, the screen is probably not
        // square and we want the mosaic blocks to be square.  Adjust the number of blocks to fix this.
        // Use the larger of the two sizes, so the blocks are square.
        float AspectRatio = ((float) Width) / Height;
        if(Width < Height)
        {
            // The screen is taller than it is wide.  Decrease the number of blocks horizontally.
            HorizontalMosaicBlocks = VerticalMosaicBlocks * AspectRatio;
        }
        else
        {
            // The screen is wider than it is tall.  Decrease the number of blocks vertically.
            VerticalMosaicBlocks = HorizontalMosaicBlocks / AspectRatio;
        }

        // There's no point to these being higher than the display resolution.
        HorizontalMosaicBlocks = Math.Min(HorizontalMosaicBlocks, Width);
        VerticalMosaicBlocks = Math.Min(VerticalMosaicBlocks, Height);

        HorizontalMosaicBlocks = Math.Max(HorizontalMosaicBlocks, 1);
        VerticalMosaicBlocks = Math.Max(VerticalMosaicBlocks, 1);

        int CurrentWidth = Width, CurrentHeight = Height;

        // If we're doing a low-resolution render, render at the block size, and we won't have
        // any rescaling passes below.
        if(!HighResolutionRender)
        {
            CurrentWidth = (int) HorizontalMosaicBlocks;
            CurrentHeight = (int) VerticalMosaicBlocks;
        }

        // Check if we actually need to recreate textures.
        Setup NewSetup;
        NewSetup.RenderWidth = CurrentWidth;
        NewSetup.RenderHeight = CurrentHeight;
        NewSetup.HorizontalMosaicBlocks = HorizontalMosaicBlocks;
        NewSetup.VerticalMosaicBlocks = VerticalMosaicBlocks;
        NewSetup.ExpandPasses = ExpandPasses;
        NewSetup.AntiAliasing = ThisCamera.allowMSAA? QualitySettings.antiAliasing:0;
        if(NewSetup.AntiAliasing == 0)
            NewSetup.AntiAliasing = 1; // work around Unity inconsistency

        if(CurrentSetup.Equals(NewSetup))
            return;
        CurrentSetup = NewSetup;

        // Release the temporary textures we previously allocated.  Note that most of the time we come back here
        // to recreate textures it's because the mosaic block size is changing (eg. because the anchor has moved),
        // in which case most of the textures will be the same size, especially the large main render texture.
        // Usually, the only thing that will change is how many low-resolution textures we allocate at the end
        // of the pass list.
        ReleaseTextures();

        // If Unity is rendering into an HDR texture for postprocessing, we want to render HDR too to pass it
        // through.  Do this if we're in linear color space and the camera's allowHDR flag is enabled.
        //
        // If there are no image effects enabled and Camera.forceIntoRenderTexture is false Unity will actually
        // just render sRGB and it'd be better for us to too, but there's no obvious way to ask Unity whether
        // it's rendering a camera into an HDR texture in OnPreRender.
        RenderTextureFormat format = QualitySettings.activeColorSpace == ColorSpace.Linear && ThisCamera.allowHDR?
            RenderTextureFormat.DefaultHDR:
            RenderTextureFormat.Default;

        // We'll render to the first texture, then blit each texture to the next to progressively
        // downscale it.
        // The first texture is what we render into.  This is also the only texture that needs a depth buffer.
        Passes.Add(new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 24, format), PassType.Render));

        // Match the scene antialiasing level.
        Passes[Passes.Count-1].Texture.antiAliasing = NewSetup.AntiAliasing;

        // Create a texture for each downscale step.
        int IntegerHorizontalMosaicBlocks = (int) Math.Ceiling(HorizontalMosaicBlocks);
        int IntegerVerticalMosaicBlocks = (int) Math.Ceiling(VerticalMosaicBlocks);

        // If we want 3.5 blocks and we're drawing into a 4x4 texture, we're drawing at 0.875 scale.
        HorizontalMosaicRatio = HorizontalMosaicBlocks / IntegerHorizontalMosaicBlocks;
        VerticalMosaicRatio = VerticalMosaicBlocks / IntegerVerticalMosaicBlocks;
        while(true)
        {
            // Each pass halves the resolution, except for the last pass which snaps to the
            // final resolution.
            CurrentWidth /= 2;
            CurrentHeight /= 2;
            CurrentWidth = (int) Math.Max(CurrentWidth, IntegerHorizontalMosaicBlocks);
            CurrentHeight = (int) Math.Max(CurrentHeight, IntegerVerticalMosaicBlocks);

            // If we've already reached the target resolution, we're done.
            if(Passes[Passes.Count-1].Texture.width == CurrentWidth &&
               Passes[Passes.Count-1].Texture.height == CurrentHeight)
                break;

            Passes.Add(new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 0, format), PassType.Downscale));
        }

        // Add the expand pass.
        for(int pass = 0; pass < ExpandPasses; ++pass)
            Passes.Add(new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 0, format), PassType.Expand));
    }

    private void ReleaseTextures()
    {
        if(Passes != null)
        {
            foreach(ImagePass pass in Passes)
                RenderTexture.ReleaseTemporary(pass.Texture);
            Passes.Clear();
        }
    }

    List<GameObject> FindGameObjectsInLayer(int layer, bool excluded=false)
    {
        GameObject[] goArray = FindObjectsOfType(typeof(GameObject)) as GameObject[];
        List<GameObject> results = new List<GameObject>();
        foreach(GameObject go in goArray)
        {
            if((!excluded && go.layer == layer) || (excluded && go.layer != layer))
                results.Add(go);
        }
        return results;
    }

    static Matrix4x4 TranslationMatrix(Vector3 translate)
    {
        return Matrix4x4.TRS(translate, Quaternion.identity, new Vector3(1,1,1));
    }

    static Matrix4x4 ScaleMatrix(Vector3 scale)
    {
        return Matrix4x4.TRS(Vector3.zero, Quaternion.identity, scale);
    }

    static float scale(float x, float l1, float h1, float l2, float h2)
    {
        return (x - l1) * (h2 - l2) / (h1 - l1) + l2;
    }

    void OnPreRender()
    {
        // Update the render targets if the window has been resized.
        SetupTextures();

        // Match the helper camera to the main camera.
        MosaicCamera.CopyFrom(ThisCamera);

        // Set the background color to black.
        MosaicCamera.backgroundColor = new Color(0, 0, 0, 0);

        //
        // Render the mosaic texture.
        //

        // Hiding other layers with the layer mask prevents them from casting shadows too.  If
        // ShadowsCastOnMosaic is enabled, hide the objects that aren't being mosaiced
        // by setting them to ShadowsOnly, so we draw only our mosaiced object but it still has shadows
        // cast by other objects.  If it's false, be less intrusive and faster by just setting the
        // cullingMask.  This can look wrong if the whole scene is in shadow, because the mosaiced
        // object will be brighter.
        Dictionary<Renderer,ShadowCastingMode> DisabledRenderers = new Dictionary<Renderer,ShadowCastingMode>();
        if(ShadowsCastOnMosaic)
        {
            List<GameObject> NonMosaicObjects = FindGameObjectsInLayer(MosaicLayer, true);
            foreach(GameObject go in NonMosaicObjects)
            {
                Renderer r = go.GetComponent<Renderer>();
                if(r == null)
                    continue;

                DisabledRenderers[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        } else {
            MosaicCamera.cullingMask =  (1 << MosaicLayer);
        }

        // Match the projection matrix to the main camera, so we render the same thing even if
        // the aspect ratio of the RenderTexture isn't exactly the same as the screen.
        MosaicCamera.projectionMatrix = ThisCamera.projectionMatrix;

        // Scale the projection matrix by ActualRenderScale.  If RenderScale is 2 then we're rendering
        // into a texture twice as large as the screen, and we need to scale everything to 50% size.
        MosaicCamera.projectionMatrix *= ScaleMatrix(new Vector3(1 / ActualRenderScaleX,1 / ActualRenderScaleY, 1));

        MosaicCamera.renderingPath = ThisCamera.renderingPath;
        MosaicCamera.clearFlags = CameraClearFlags.SolidColor;
        MosaicCamera.targetTexture = Passes[0].Texture;

        // Render the layers into our main temporary texture.  This will be HighResTex.
        MosaicCamera.Render();

        // Now that we're done rendering the mosaic texture, undo any changes we just made to shadowCastingMode.
        foreach(KeyValuePair<Renderer,ShadowCastingMode> SavedShadowMode in DisabledRenderers)
            SavedShadowMode.Key.shadowCastingMode = SavedShadowMode.Value;

        // If we have an AnchorTransform, see how many screen space pixels we should shift the resized texture
        // to keep the mosaic aligned with it.  We do this now so we can use MosaicCamera.projectionMatrix.
        // We use that instead of ThisCamera.projectionMatrix so it takes the render scale into account.
        if(AnchorTransform != null)
        {
            // Get the screen coordinates of the anchor.
            // see where the transform is in screen space, and set the offset so that lies on a mosaic intersection
            Vector3 ScreenPos = MosaicCamera.WorldToScreenPoint(AnchorTransform.transform.position);

            // The size of the mosaic, in screen pixels:
            float HorizontalMosaicSize = MosaicCamera.targetTexture.width / CurrentSetup.HorizontalMosaicBlocks;
            float VerticalMosaicSize = MosaicCamera.targetTexture.height / CurrentSetup.VerticalMosaicBlocks;

            // Figure out the offset needed to align the mosaic to the anchor.
            float HorizontalOffset = RoundToNearest(ScreenPos.x, HorizontalMosaicSize) - ScreenPos.x;
            float VerticalOffset = RoundToNearest(ScreenPos.y, VerticalMosaicSize) - ScreenPos.y;

            // Our offsets are relative to the bottom-left of the screen.
            OffsetPixelsX = -HorizontalOffset;
            OffsetPixelsY = +VerticalOffset;
//            Debug.Log(ScreenPos.x + ", " + HorizontalOffset + ", " + HorizontalMosaicSize + ", " + OffsetPixelsX);
//            Debug.Log(ScreenPos.y + ", " + VerticalOffset + ", " + VerticalMosaicSize + ", " + OffsetPixelsY);
        }
        else
        {
            OffsetPixelsX = 0;
            OffsetPixelsY = 0;
        }

        // Run each postprocessing pass.
        for(int i = 1; i < Passes.Count; ++i)
        {
            RenderTexture src = Passes[i-1].Texture;
            RenderTexture dst = Passes[i].Texture;

            // This doesn't happen automatically.  We have to enable sRGBWrite manually.
            GL.sRGBWrite = dst.sRGB;

            switch(Passes[i].Type)
            {
            case PassType.Downscale:
            {
                src.filterMode = FilterMode.Bilinear;

                // Set up BlitMaterial.  This is a simple texture that we only use for blitting.  Graphics.Blit
                // doesn't give us any control, so we have to do the copies ourself.
                BlitMaterial.SetTexture("_MainTex", src);

                RenderTexture SavedActiveRenderTexture = RenderTexture.active;
                RenderTexture.active = dst;

                BlitMaterial.SetPass(0);

                GL.PushMatrix();
                GL.LoadOrtho();

                Vector2 BottomLeftUV = new Vector2(0,0);
                Vector2 TopRightUV = new Vector2(1,1);

                // Apply scaling and offsets on the first downscale pass.
                if(i == 1)
                {
                    // MosaicRatio is the scale of the mosaic texture.  If this is 0.5, the mosaic will be
                    // half the size of the texture (anchored bottom-left).  Apply this in the first downscale.
                    TopRightUV.x *= 1/HorizontalMosaicRatio;
                    TopRightUV.y *= 1/VerticalMosaicRatio;

                    // Apply the mosaic offset in the first downscale by shifting UVs when sampling the render
                    // buffer.  This will be reversed by TextureMatrix.  The render buffer always has pixels
                    // 1:1 to the screen (even if we're expanding it at the edges), so this is always in pixels.
                    float OffsetU = OffsetPixelsX / src.width;
                    float OffsetV = -OffsetPixelsY / src.height;
                    BottomLeftUV += new Vector2(OffsetU, OffsetV);
                    TopRightUV += new Vector2(OffsetU, OffsetV);
                }

                GL.Begin(GL.QUADS);
                GL.TexCoord2(TopRightUV.x,   BottomLeftUV.y); GL.Vertex3(1.0f, 0.0f, 0.1f);
                GL.TexCoord2(BottomLeftUV.x, BottomLeftUV.y); GL.Vertex3(0.0f, 0.0f, 0.1f);
                GL.TexCoord2(BottomLeftUV.x, TopRightUV.y);   GL.Vertex3(0.0f, 1.0f, 0.1f);
                GL.TexCoord2(TopRightUV.x,   TopRightUV.y);   GL.Vertex3(1.0f, 1.0f, 0.1f);
                GL.End();
                GL.PopMatrix();

                RenderTexture.active = SavedActiveRenderTexture;
                break;
            }

            case PassType.Expand:
                ExpandEdgesMaterial.SetVector("PixelUVStep", new Vector4(1.0f / src.width, 1.0f / src.height, 0, 0));
                src.filterMode = FilterMode.Point;
                Graphics.Blit(src, dst, ExpandEdgesMaterial);
                break;
            }
        }

        // Draw the low-resolution texture with nearest neighbor sampling.
        RenderTexture MosaicTex = Passes[Passes.Count-1].Texture;
        MosaicTex.filterMode = FilterMode.Point;
        MosaicMaterial.SetTexture("MosaicTex", MosaicTex);

        // Disable material keywords.  We'll set the correct ones below.
        foreach(string keyword in MosaicMaterial.shaderKeywords)
            MosaicMaterial.DisableKeyword(keyword);

        // HighResTex is the texture to sample where the mosaic is masked out.  If we're not rendering
        // in high resolution, this will be the same texture as the mosaic, so masking and alpha won't
        // do anything.
        MosaicMaterial.SetTexture("HighResTex", Passes[0].Texture);
        MosaicMaterial.SetFloat("Alpha", Alpha);

        {
            Matrix4x4 FullTextureMatrix = Matrix4x4.identity;

            // If we rendered the full frame at 2x resolution, use the texture matrix to scale UVs
            // down by 0.5 to compensate.  Scale around the center of the texture, not the origin.
            FullTextureMatrix *= TranslationMatrix(new Vector3(0.5f, 0.5f, 0));
            FullTextureMatrix *= ScaleMatrix(new Vector3(1/ActualRenderScaleX,1/ActualRenderScaleY,1));
            FullTextureMatrix *= TranslationMatrix(new Vector3(-0.5f, -0.5f, 0));
            MosaicMaterial.SetMatrix("FullTextureMatrix", FullTextureMatrix);

            // OffsetPixelsX/OffsetPixelsY shifted the texture in order to move where the center of
            // the mosaic blocks are.  Undo the shifting here, so it isn't actually shifted on screen.
            float OffsetPixelsU = OffsetPixelsX / Passes[0].Texture.width;
            float OffsetPixelsV = -OffsetPixelsY / Passes[0].Texture.height;
            //Matrix4x4 MosaicTextureMatrix = TranslationMatrix(new Vector3(-OffsetPixelsU, -OffsetPixelsV, 0)) * FullTextureMatrix;
            Matrix4x4 MosaicTextureMatrix = Matrix4x4.identity;
            MosaicTextureMatrix *= ScaleMatrix(new Vector3(HorizontalMosaicRatio, VerticalMosaicRatio, 0));
            MosaicTextureMatrix *= TranslationMatrix(new Vector3(-OffsetPixelsU, -OffsetPixelsV, 0));

            // If we scaled the full resolution image, the mosaic is affected as well.
            MosaicTextureMatrix *= FullTextureMatrix;

            MosaicMaterial.SetMatrix("MosaicTextureMatrix", MosaicTextureMatrix);
        }

        // Select whether we're using the texture masking shader, sphere masking, or no masking.
        if(TextureMasking && MaskingTexture != null)
        {
            MosaicMaterial.EnableKeyword("TEXTURE_MASKING");
            MosaicMaterial.SetTexture("MaskTex", MaskingTexture);
        }

        if(SphereMasking && MaskingSphere != null)
        {
            MosaicMaterial.EnableKeyword("SPHERE_MASKING");

            // MaskSizeInner is how big the mosaic circle should be around MaskingSphere.  Within this
            // distance, the mosaic is 100%.  MaskSizeOuter is the size of the fade-out.  At 0, the
            // mosaic cuts out abruptly.  At 1, it fades out over one world space unit.
            //
            // The transparency of the mosaic scales distance so MaskSizeInner is 1 (100%) and MaskSizeOuter
            // is 0 (0%).  If the distance is less than MaskSizeInner it'll be above 1 and clamped.  This
            // is simply:
            //
            // f = (dist - MaskSizeOuter) / (MaskSizeInner - MaskSizeOuter);
            //
            // To remove the division from the fragment shader, we pass in MaskScaleFactor,
            // which is 1 / (MaskSizeInner - MaskSizeOuter).
            //
            // If the fade is zero, nudge it up slightly to avoid division by zero.
            float MaskSizeInner = 1;
            float MaskSizeOuter = MaskSizeInner + MaskFade;
            if(MaskFade == 0)
                MaskSizeOuter += 0.0001f;

            MosaicMaterial.SetFloat("MaskSizeOuter", MaskSizeOuter);
            float MaskSizeFactor = 1.0f / (MaskSizeInner - MaskSizeOuter);
            MosaicMaterial.SetFloat("MaskSizeFactor", MaskSizeFactor);
            Matrix4x4 mat = MaskingSphere.transform.worldToLocalMatrix;

            // Halve the size of the mask, since the distance from the center to the edge of
            // the mask sphere is 0.5, not 1:
            mat = Matrix4x4.TRS(
                    Vector3.zero,
                    Quaternion.AngleAxis(0, new Vector3(1,0,0)),
                    new Vector3(2,2,2)) * mat;

            MosaicMaterial.SetMatrix("MaskMatrix", mat);
        }

        // Find the objects that we're mosaicing, and switch them to the mosaic shader, which
        // will sample the prerendered texture we just made.  This will happen during regular
        // rendering.
        List<GameObject> MosaicObjects = FindGameObjectsInLayer(MosaicLayer);
      
        foreach(GameObject go in MosaicObjects)
        {
            // Find objects in our layer that have a renderer.
            Renderer renderer = go.GetComponent<Renderer>();
            if(renderer == null)
                continue;
            
            // Save the original materials so we can restore them in OnPostRender.
            SavedMaterials[renderer] = renderer.materials;

            // Replace all materials on this object with ours.
            Material[] ReplacementMaterials = new Material[renderer.materials.Length];
            for(int i = 0; i < renderer.materials.Length; ++i)
                ReplacementMaterials[i] = MosaicMaterial;
            renderer.materials = ReplacementMaterials;
        }
    }

    void OnPostRender()
    {
        // Restore the original materials.
        foreach(KeyValuePair<Renderer,Material[]> SavedMat in SavedMaterials)
            SavedMat.Key.materials = SavedMat.Value;
        SavedMaterials.Clear();

        // Discard the textures we rendered, since we don't need them anymore.
        foreach(ImagePass pass in Passes)
            pass.Texture.DiscardContents();
    }
};

