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

#pragma warning disable 0162 // Unreachable code detected
#pragma warning disable 0429 // Unreachable expression code detected

[AddComponentMenu("Effects/Mosaix")]
#if UNITY_5_1_0
[HelpURL("https://github.com/unity-effects/mosaix/blob/master/readme.md")]
#endif
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

    // If true, we'll render the mask rather than the real texture.
    [System.NonSerialized]
    public bool ShowMask = false;

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
    // The anchor used by FollowAnchor and ScaleMosaicToAnchorDistance.  If both of those are
    // disabled, this will have no effect.
    public GameObject AnchorTransform;

    // If true, we'll scale the mosaic so it gets bigger when the anchor is closer to the camera
    // and smaller as it gets further away.
    public bool ScaleMosaicToAnchorDistance;

    // If true, the mosaic alignment will follow the anchor around on screen.  This gives a "solid"
    // looking mosaic.
    public bool FollowAnchor;

    // The most recent offset of AnchorTransform on screen relative to the mosaic.
    private Vector2 PreviousAnchorOffset = new Vector2(0,0);

    // The offset of the mosaic.  At 0x0, the mosaic is aligned to the bottom-left of the texture.  At 0.5x0.5,
    // the mosaic grid is shifted right and up by one half of a mosaic block.  This is tracked as a fraction of
    // a block instead of pixels, so the offset remains constant if the block size is changed.
    private Vector2 MosaicOffset = new Vector2(0,0);

    // Convert MosaicOffset to screen pixels.
    private Vector2 MosaicOffsetPixels {
        get {
            Vector2 MosaicSize = GetMosaicSizeInPixels();
            return new Vector2(MosaicOffset.x * MosaicSize.x, MosaicOffset.y * MosaicSize.y);
        }
    }

    public Shader ResizeShader;
    private Material ResizeMaterial;

    // This shader copies the outermost edge of opaque pixels outwards.
    public Shader ExpandEdgesShader;
    private Material ExpandEdgesMaterial;

    // This material calls MosaicShader, which samples our low-resolution mosaic texture in screen space.
    // It can also be another material that calls the mosaic pass.
    public Material MosaicMaterial;

    // The amount of extra screen space to render in the texture.  If this is 1, the texture is drawn at
    // exactly the size of the screen.  If 2, the texture is expanded by 2x.  The screen will still be
    // drawn at exactly the same size in the center of the texture, but we'll have more pixels outwards.
    //
    // Setting this to a slightly higher value gives us extra pixels to sample for the mosaic at the edges
    // of the screen.  This can reduce flicker due to bright parts of the object coming in and out of view,
    // by keeping them "in view" of the mosaic even though they're actually offscreen.
    //
    // This also helps avoid artifacts from OffsetPixels.  If we're shifting the mosaic right by 10 pixels,
    // then we want to have 10 pixels extra at the left to sample the left row of mosaic.  If the object
    // fills the camera, those 10 pixels can be offscreen and we may have no pixels for that row.  ExpandEdges
    // fixes this by expanding the next row outwards, but it can be conspicuous when a whole row is missing.
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

        // For PassType.Downscale, true if we're filtering on X, false if we're filtering on Y.
        public bool FilterOnX;
    };
    private List<ImagePass> Passes = new List<ImagePass>();

    private Dictionary<Renderer,Material[]> SavedMaterials = new Dictionary<Renderer,Material[]>();

#if UNITY_EDITOR
    // These are settings only used by MosaixEditor.
    [System.NonSerialized]
    public MosaixEditor.EditorSettings EditorSettings = new MosaixEditor.EditorSettings();

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

    // Throw a fatal error, and disable the script so we don't spam errors if something isn't set up right.
    void FatalError(string s)
    {
        enabled = false;
        throw new Exception(this + ": " + s);
    }

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
        if(ThisCamera == null) FatalError("This script must be attached to a camera.");
        if(ExpandEdgesShader == null) FatalError("No ExpandEdgesShader is assigned.");
        if(ResizeShader == null) FatalError("No ResizeShader is assigned.");
        if(MosaicMaterial == null) FatalError("No MosaicMaterial is assigned.");

        // Create materials for our shaders.
        ResizeMaterial = new Material(ResizeShader);
        ExpandEdgesMaterial = new Material(ExpandEdgesShader);

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

        // Make sure neither dimension is zero.
        HorizontalMosaicBlocks = Math.Max(1, HorizontalMosaicBlocks);
        VerticalMosaicBlocks = Math.Max(1, VerticalMosaicBlocks);

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

        // Don't allow these to go too low.  If we're only drawing one block, it's easy for MosaicOffset
        // to put too much offscreen, so we'd need a very high RenderScale to compensate.  This probably
        // would only happen from unexpected anchor scaling, so put a sanity limit here.
        HorizontalMosaicBlocks = Math.Max(HorizontalMosaicBlocks, 4);
        VerticalMosaicBlocks = Math.Max(VerticalMosaicBlocks, 4);

        int CurrentWidth = Width, CurrentHeight = Height;

        // The final number of mosaic blocks (resolution of the mosaic texture):
        int IntegerHorizontalMosaicBlocks = (int) Math.Ceiling(HorizontalMosaicBlocks);
        int IntegerVerticalMosaicBlocks = (int) Math.Ceiling(VerticalMosaicBlocks);

        // If we're doing a low-resolution render, render at the block size, and we won't have
        // any rescaling passes below.  Snap HorizontalMosaicBlocks/VerticalMosaicBlocks as well
        // in this mode (we won't support fractional mosaic sizes here).
        if(!HighResolutionRender)
        {
            HorizontalMosaicBlocks = CurrentWidth = IntegerHorizontalMosaicBlocks;
            VerticalMosaicBlocks = CurrentHeight = IntegerVerticalMosaicBlocks;
        }

        // Check if we actually need to recreate textures.
        Setup NewSetup;
        NewSetup.RenderWidth = CurrentWidth;
        NewSetup.RenderHeight = CurrentHeight;
        NewSetup.HorizontalMosaicBlocks = HorizontalMosaicBlocks;
        NewSetup.VerticalMosaicBlocks = VerticalMosaicBlocks;
        NewSetup.ExpandPasses = ExpandPasses;

        // Match the scene antialiasing level.
#if UNITY_5_6_OR_NEVER
        bool allowMSAA = ThisCamera.allowMSAA;
#else
        bool allowMSAA = false;
#endif

        NewSetup.AntiAliasing = allowMSAA? QualitySettings.antiAliasing:0;
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
#if UNITY_5_6_OR_NEWER
        bool allowHDR = ThisCamera.allowHDR;
#else
        bool allowHDR = ThisCamera.hdr;
#endif
        RenderTextureFormat format = QualitySettings.activeColorSpace == ColorSpace.Linear && allowHDR?
            RenderTextureFormat.DefaultHDR:
            RenderTextureFormat.Default;

        // We'll render to the first texture, then blit each texture to the next to progressively
        // downscale it.
        //
        // The first texture is what we render into.  This is also the only texture that needs a depth buffer, and the
        // only one that has antialiasing enabled.
        Passes.Add(new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 24, format,
                        RenderTextureReadWrite.Default, NewSetup.AntiAliasing),
                   PassType.Render));


        // If we want 3.5 blocks and we're drawing into a 4x4 texture, we're drawing at 0.875 scale.
        HorizontalMosaicRatio = HorizontalMosaicBlocks / IntegerHorizontalMosaicBlocks;
        VerticalMosaicRatio = VerticalMosaicBlocks / IntegerVerticalMosaicBlocks;

        // If we're in normal (high-resolution) mode, downscale to the mosaic.  If we're in low-res mode, we're
        // already at mosaic resolution and can skip these passes.
        if(HighResolutionRender)
        {
            // First resize on X.
            CurrentWidth = IntegerHorizontalMosaicBlocks;

            // This resize step is doing a filter over X and will rescale all the way to the mosaic resolution on
            // X.  While we're doing this, also downscale by up to 50% on Y, since we can do this for free.
            CurrentHeight = Math.Max(CurrentHeight / 2, IntegerVerticalMosaicBlocks);
            ImagePass HorizResizePass = new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 24, format), PassType.Downscale);
            HorizResizePass.FilterOnX = true; // box filter on X axis
            Passes.Add(HorizResizePass);

            // Next, resize on Y.
            CurrentHeight = IntegerVerticalMosaicBlocks;
            ImagePass VertResizePass = new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 24, format), PassType.Downscale);
            VertResizePass.FilterOnX = false; // box filter on Y axis
            Passes.Add(VertResizePass);
        }

        // Add the expand pass.
        for(int pass = 0; pass < ExpandPasses; ++pass)
            Passes.Add(new ImagePass(RenderTexture.GetTemporary(CurrentWidth, CurrentHeight, 24, format), PassType.Expand));
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

    // Return the size of the mosaic, in screen pixels.
    Vector2 GetMosaicSizeInPixels()
    {
        // Return 1x1 if we're not initialized yet, which happens in SetupTextures().
        if(MosaicCamera.targetTexture == null || CurrentSetup.HorizontalMosaicBlocks == 0)
            return new Vector2(1,1);

        return new Vector2(
                MosaicCamera.targetTexture.width / CurrentSetup.HorizontalMosaicBlocks,
                MosaicCamera.targetTexture.height / CurrentSetup.VerticalMosaicBlocks);
    }

    // Return the offset from the mosaic grid at the given screen coordinates.  If ScreenPos lies exactly
    // on a mosaic grid intersection, return 0x0.  If ScreenPos is two pixels to the right and three
    // pixels above a grid intersection, return (2x3).
    Vector2 GetOffsetAtScreenPos(Vector3 ScreenPos)
    {
        // The size of the mosaic, in screen pixels:
        Vector2 MosaicSize = GetMosaicSizeInPixels();

        // If MosaicOffsetPixels is (1,0), the mosaic is shifted right by one pixel.  Offset ScreenPos too so we
        // return the offset relative to the current mosaic.
        Vector3 AdjustedScreenPos = ScreenPos - new Vector3(MosaicOffsetPixels.x, MosaicOffsetPixels.y, 0);

        // Figure out the offset needed to align the mosaic to the anchor.
        Vector2 OffsetFromOrigin = new Vector2(
                RoundToNearest(ScreenPos.x, MosaicSize.x) - AdjustedScreenPos.x,
                RoundToNearest(ScreenPos.y, MosaicSize.y) - AdjustedScreenPos.y);

        OffsetFromOrigin *= -1;

        // Scale the offset from pixels to fraction of a block.
        OffsetFromOrigin.x /= MosaicSize.x;
        OffsetFromOrigin.y /= MosaicSize.y;

        // Debug.Log(MosaicSize.x.ToString("N2") + "x" + MosaicSize.y.ToString("N2") + ", " + ScreenPos.x.ToString("N2") + "x" + ScreenPos.y.ToString("N2") + ", " + OffsetFromOrigin.x.ToString("N2") + "x" + OffsetFromOrigin.y.ToString("N2"));
        return OffsetFromOrigin;
    }

    void SetMosaicOffset(Vector2 Offset)
    {
        MosaicOffset = Offset;

        // Wrap MosaicOffset to [-0.5,0.5].
        MosaicOffset += new Vector2(0.5f, 0.5f);
        MosaicOffset.x %= 1.0f;
        MosaicOffset.y %= 1.0f;
        if(MosaicOffset.x < 0) MosaicOffset.x += 1;
        if(MosaicOffset.y < 0) MosaicOffset.y += 1;
        MosaicOffset -= new Vector2(0.5f, 0.5f);
    }

    // Store the current anchor alignment.
    //
    // Call this after moving the anchor if you don't want the mosaic to shift as a side-effect.  This won't
    // prevent distance scaling from changing.
    public void ResetAnchorAlignment()
    {
        if(AnchorTransform != null && MosaicCamera != null)
        {
            Vector3 OldAnchorScreenPos = MosaicCamera.WorldToScreenPoint(AnchorTransform.transform.position);
            PreviousAnchorOffset = GetOffsetAtScreenPos(OldAnchorScreenPos);
        }
    }

    void OnPreRender()
    {
        // If we're not aligning the mosaic to the transform (but we may be aligning it for scale), save
        // the mosaic alignment now.  This way, when we update the alignment later, we do it relative to now
        // rather than to the last frame.  That makes it so we only adjust for changes that we make to the
        // mosaic below (scaling) and not for other changes to the scene, like the anchor or camera moving.
        if(!FollowAnchor)
            ResetAnchorAlignment();

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

        // The camera might have a modified viewport to set where it appears on screen.  Ignore this for
        // the MosaicCamera.
        MosaicCamera.rect = new Rect(0, 0, 1, 1);

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

        // If either transform or scale anchoring are enabled, update the offset.
        //
        // This is done for scaling and not just alignment.  If we scale the mosaic without also aligning
        // it, the mosaic changing scale is ugly since the mosaic is effectively anchored to the bottom-left.
        if((FollowAnchor || ScaleMosaicToAnchorDistance) && AnchorTransform != null)
        {
            // See how far the anchor has moved in screen space since the last time ResetAnchorAlignment was
            // called, and shift the mosaic to compensate.
            Vector3 NewScreenPos = MosaicCamera.WorldToScreenPoint(AnchorTransform.transform.position);
            Vector2 NewOffset = GetOffsetAtScreenPos(NewScreenPos);
            Vector2 OffsetDelta = NewOffset - PreviousAnchorOffset;
            SetMosaicOffset(MosaicOffset + OffsetDelta);
        }

        // Remember where the anchor is on screen now that we've adjusted it, so we update relative to this
        // next frame.
        if(AnchorTransform != null)
            ResetAnchorAlignment();

        Vector2 OffsetPixels = MosaicOffsetPixels;
        Vector2 OffsetUV = new Vector2(OffsetPixels.x / Passes[0].Texture.width, OffsetPixels.y / Passes[0].Texture.height);

        // Run each postprocessing pass.
        for(int i = 1; i < Passes.Count; ++i)
        {
            RenderTexture src = Passes[i-1].Texture;
            RenderTexture dst = Passes[i].Texture;

            // This doesn't happen automatically.  We have to enable sRGBWrite manually.
            GL.sRGBWrite = dst.sRGB;

            ImagePass pass = Passes[i];
            switch(pass.Type)
            {
            case PassType.Downscale:
            {
                RenderTexture SavedActiveRenderTexture = RenderTexture.active;
                RenderTexture.active = dst;

                /*
                 * This pass implements a box filter resize.  This resize is ideal for a mosaic, since
                 * it can resize with very high ratios, where most other resizes only work up to a 50%
                 * reduction and need to be iterated.
                 *
                 * This doesn't do edge weighting.  Each sample in the box has the same weight, even if
                 * it doesn't overlap the box completely.  This would make the filter slower and more
                 * complex, and in our case where we're usually downscaling a lot it doesn't matter.
                 * However, if you're downscaling by a small ratio you may be better with a regular bilinear
                 * blit.
                 * 
                 * This only averages on one axis at a time: we resize horizontally and then vertically.
                 * This parallelizes better on the GPU and simplifies the shader, since it only needs one
                 * loop.
                 *
                 * If we're downsampling from 4 pixels to 1:
                 *
                 * ABCD -> E
                 *
                 * The destination pixel is at the center (between B and C), and we want to sample each
                 * source pixel once.  UVStep is be the distance from one pixel to another (from A to B).
                 * UVStart is the distance from the center of the destination pixel (the intersection
                 * between B and C) to the first pixel to sample (the center of A).  We'll sample 4 times,
                 * add them together and divide by the number of samples (4).
                 *
                 * * Optimization: bilinear filtering
                 *
                 * If we're doing box filtering on the X axis, we can also let regular bilinear scaling happen
                 * on the Y axis.  For example, we can resize from 1000x1000 to 100x500 in one pass.  The box
                 * filter will be set to the X axis for the large 10:1 ratio, and bilinear filtering will work
                 * normally on the Y axis.  This lets us halve the amount of texture data we need to process: to
                 * go from 1000x1000 to 100x100, we'll first go to 100x500 (box filter on X) and then 100x100
                 * (box filter on Y).  We don't need to do anything special for this to work, we just set the
                 * texture resolutions accordingly.
                 *
                 * * Optimization: partial bilinear scaling
                 *
                 * If we're downsampling 4 pixels:
                 *
                 * ABCD -> E
                 *
                 * Instead of sampling the center of each pixel A B C D with point sampling, we sample the
                 * intersection of AB and CD with bilinear filtering.  This gives the same result with half
                 * the number of samples.  (The result can vary slightly since we're sampling an integer number
                 * of pixels: if we were sampling 9, we'll be sampling 4 or 5, not 4.5.)
                 */

                src.filterMode = FilterMode.Bilinear;

                // Set up ResizeMaterial.  This is a simple texture that we only use for blitting.  Graphics.Blit
                // doesn't give us any control, so we have to do the copies ourself.
                ResizeMaterial.SetTexture("_MainTex", src);

                // See which axis we're resizing on.  We only resize on X or Y in a given pass.
                int AxisIndex = pass.FilterOnX? 0:1;

                Vector2 SrcSize = new Vector2(src.width, src.height);
                Vector2 DstSize = new Vector2(dst.width, dst.height);
                //Vector2 SrcSize = new Vector2(4, 4);
                //Vector2 DstSize = new Vector2(1, 1);

                float SrcToDstRatio = (float) SrcSize[AxisIndex] / DstSize[AxisIndex];
                const bool BilinearResizeOptimization = true;

                // If we're resampling ABCD -> E, by default the UV will be at the center, between BC.
                Vector2 UVStart = new Vector2(0,0);
                UVStart[AxisIndex] = -SrcToDstRatio/2; // left edge of A, in source pixels
                if(BilinearResizeOptimization)
                    UVStart[AxisIndex] += 1.0f; // between AB, in source pixels
                else
                    UVStart[AxisIndex] += 0.5f; // center of A, in source pixels
                UVStart[AxisIndex] /= SrcSize[AxisIndex]; // convert to UVs

                // Create (x,0) start/steps if we're resizing on X, otherwise (0,x).
                ResizeMaterial.SetVector("UVStart", UVStart);

                // If we step 1 / SrcSize we'll move by one pixel per sample.  Step by 2 / SrcSize to
                // step by two pixels.
                Vector2 UVStep = new Vector2(0,0);
                UVStep[AxisIndex] = BilinearResizeOptimization? 2.0f:1.0f;
                UVStep[AxisIndex] /= SrcSize[AxisIndex];
                ResizeMaterial.SetVector("UVStep", UVStep);

                int Samples = (int) Math.Round(SrcToDstRatio);
                if(BilinearResizeOptimization)
                    Samples /= 2;
                Samples = Math.Max(Samples, 1);

                ResizeMaterial.SetInt("Samples", (int) Samples);
                ResizeMaterial.SetFloat("SampleFactor", 1.0f / Samples);
                
                // Debug.Log(src + " -> " + dst + ", " + SrcToDstRatio + ", " + "start: " + UVStart + ", " + "step: " + UVStep + ", samples " + Samples);

                ResizeMaterial.SetPass(0);

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
                    BottomLeftUV += new Vector2(OffsetUV.x, OffsetUV.y);
                    TopRightUV += new Vector2(OffsetUV.x, OffsetUV.y);
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

            // OffsetPixels shifted the texture in order to move where the center of the mosaic blocks are.
            // Undo the shifting here, so it isn't actually shifted on screen.
            Matrix4x4 MosaicTextureMatrix = Matrix4x4.identity;
            MosaicTextureMatrix *= ScaleMatrix(new Vector3(HorizontalMosaicRatio, VerticalMosaicRatio, 0));
            MosaicTextureMatrix *= TranslationMatrix(new Vector3(-OffsetUV.x, -OffsetUV.y, 0));

            // If we scaled the full resolution image, the mosaic is affected as well.
            MosaicTextureMatrix *= FullTextureMatrix;

            MosaicMaterial.SetMatrix("MosaicTextureMatrix", MosaicTextureMatrix);
        }

        if(ShowMask)
            MosaicMaterial.EnableKeyword("SHOW_MASK");
        
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
            if(MaskFade < 0.0001f)
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

