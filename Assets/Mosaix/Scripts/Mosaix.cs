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
    public int MosaicBlocks = 16;
    
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

    // The number of ExpandEdges passes to perform.  This is normally always 1 and we only
    // expose it for debugging.
    [System.NonSerialized]
    public int ExpandPasses = 1;

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

    // This shader copies the outermost edge of opaque pixels outwards.
    public Shader ExpandEdgesShader;

    // This material calls MosaicShader, which samples our low-resolution mosaic texture in screen space.
    // It can also be another material that calls the mosaic pass.
    public Material MosaicMaterial;

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
    public List<ImagePass> Passes = new List<ImagePass>();

    private Material ExpandEdgesMaterial;

    private Dictionary<Renderer,Material[]> SavedMaterials = new Dictionary<Renderer,Material[]>();

#if UNITY_EDITOR
    // These are settings only used by MosaixEditor.
    [System.NonSerialized]
    public MosaixEditor.EditorSettings EditorSettings = new MosaixEditor.EditorSettings();

    void Reset()
    {
        // When the script is added in the editor, set the default shaders and mosaic material.
        ExpandEdgesShader = Shader.Find("Hidden/Mosaix/ExpandEdges");
        MosaicMaterial = (Material) AssetDatabase.LoadAssetAtPath("Assets/Mosaix/Shaders/Mosaic.mat", typeof(Material));
    }
#endif

    static HashSet<Mosaix> EnabledMosaixScripts = new HashSet<Mosaix>();

    // This stores the configuration we used to set up textures.  If this changes, we know we need
    // to recreate our image passes.
    struct Setup
    {
        public int RenderWidth, RenderHeight;
        public int HorizontalMosaicBlocks;
        public int VerticalMosaicBlocks;
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

    private void SetupTextures()
    {
        int Width = ThisCamera.pixelWidth;
        int Height = ThisCamera.pixelHeight;

        // int Factor = 16;
        // int HorizontalMosaicBlocks = Width/Factor;
        // int VerticalMosaicBlocks = Height/Factor;
        int HorizontalMosaicBlocks = (int) Math.Max(1, MosaicBlocks);
        int VerticalMosaicBlocks = HorizontalMosaicBlocks;

        // MosaicBlocks is the number of mosaic blocks we want to display.  However, the screen is probably not
        // square and we want the mosaic blocks to be square.  Adjust the number of blocks to fix this.
        // Use the larger of the two sizes, so the blocks are square.
        float AspectRatio = ((float) Width) / Height;
        if(Width < Height)
        {
            // The screen is taller than it is wide.  Decrease the number of blocks horizontally.
            HorizontalMosaicBlocks = (int) Math.Round(VerticalMosaicBlocks * AspectRatio);
        }
        else
        {
            // The screen is wider than it is tall.  Decrease the number of blocks vertically.
            VerticalMosaicBlocks = (int) Math.Round(HorizontalMosaicBlocks / AspectRatio);
        }

        // There's no point to these being higher than the display resolution.
        HorizontalMosaicBlocks = Math.Min(HorizontalMosaicBlocks, ThisCamera.pixelWidth);
        VerticalMosaicBlocks = Math.Min(VerticalMosaicBlocks, ThisCamera.pixelHeight);

        HorizontalMosaicBlocks = Math.Max(HorizontalMosaicBlocks, 1);
        VerticalMosaicBlocks = Math.Max(VerticalMosaicBlocks, 1);

        int CurrentWidth = Width, CurrentHeight = Height;

        // If we're doing a low-resolution render, render at the block size, and we won't have
        // any rescaling passes below.
        if(!HighResolutionRender)
        {
            CurrentWidth = HorizontalMosaicBlocks;
            CurrentHeight = VerticalMosaicBlocks;
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

        ReleaseTextures();

        // Use HDR textures if we're in linear color, otherwise use regular textures.  This way,
        // we preserve HDR color through to the framebuffer.  Note that this assumes you're using
        // linear color and HDR together.  You can use linear color without HDR, but that seems
        // to be broken with RenderTexture.
        RenderTextureFormat format = QualitySettings.activeColorSpace == ColorSpace.Linear? RenderTextureFormat.DefaultHDR:RenderTextureFormat.Default;

        // We'll render to the first texture, then blit each texture to the next to progressively
        // downscale it.
        // The first texture is what we render into.  This is also the only texture that needs a depth buffer.
        Passes.Add(new ImagePass(new RenderTexture(CurrentWidth, CurrentHeight, 24, format), PassType.Render));

        // Match the scene antialiasing level.
        Passes[Passes.Count-1].Texture.antiAliasing = NewSetup.AntiAliasing;

        // Create a texture for each downscale step.
        while(true)
        {
            // Each pass halves the resolution, except for the last pass which snaps to the
            // final resolution.
            CurrentWidth /= 2;
            CurrentHeight /= 2;
            CurrentWidth = Math.Max(CurrentWidth, HorizontalMosaicBlocks);
            CurrentHeight = Math.Max(CurrentHeight, VerticalMosaicBlocks);

            // If we've already reached the target resolution, we're done.
            if(Passes[Passes.Count-1].Texture.width == CurrentWidth &&
               Passes[Passes.Count-1].Texture.height == CurrentHeight)
                break;

            Passes.Add(new ImagePass(new RenderTexture(CurrentWidth, CurrentHeight, 0, format), PassType.Downscale));
        }

        // Add the expand pass.
        for(int pass = 0; pass < ExpandPasses; ++pass)
            Passes.Add(new ImagePass(new RenderTexture(CurrentWidth, CurrentHeight, 0, format), PassType.Expand));
    }

    private void ReleaseTextures()
    {
        if(Passes != null)
        {
            foreach(ImagePass pass in Passes)
                pass.Texture.Release();
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

        MosaicCamera.renderingPath = ThisCamera.renderingPath;
        MosaicCamera.clearFlags = CameraClearFlags.SolidColor;
        MosaicCamera.targetTexture = Passes[0].Texture;

        MosaicCamera.Render();

        // Now that we're done rendering the mosaic texture, undo any changes we just made to shadowCastingMode.
        foreach(KeyValuePair<Renderer,ShadowCastingMode> SavedShadowMode in DisabledRenderers)
            SavedShadowMode.Key.shadowCastingMode = SavedShadowMode.Value;

        // Run each postprocessing pass.
        for(int i = 1; i < Passes.Count; ++i)
        {
            RenderTexture src = Passes[i-1].Texture;
            RenderTexture dst = Passes[i].Texture;

            switch(Passes[i].Type)
            {
            case PassType.Downscale:
                src.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, dst);
                break;

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

