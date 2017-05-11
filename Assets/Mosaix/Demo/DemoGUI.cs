using UnityEngine;

public class DemoGUI: MonoBehaviour 
{
    public bool ShowSphere = true;
    public float MosaicBlocks = 20;
    bool Open = false;
    float SpherePosition = 0;

    // This is true in the simple demo where we show a scale control, and false in the character
    // demo where scaling interpolates between the two control endpoints.
    public bool ScaleControl;

    public Mosaix MosaixComponent;
    public GameObject SphereMask;
    public Transform SphereMaskStart, SphereMaskEnd;

    GUIStyle MakePaddedBoxStyle(int Horiz, int Vert)
    {
        GUIStyle PaddedBox = new GUIStyle(GUI.skin.box); 
        PaddedBox.padding = new RectOffset(Horiz,Horiz,Vert,Vert);
        return PaddedBox;
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
            GUILayout.BeginHorizontal();
            GUILayout.Space(20); // left padding
                GUILayout.BeginVertical();
                    GUILayout.FlexibleSpace(); // bottom align
                    GUILayout.Label("Hold ALT to navigate with the mouse", MakePaddedBoxStyle(15,5));
                    GUILayout.Space(20); // bottom padding
                GUILayout.EndVertical();
            GUILayout.FlexibleSpace(); // right padding
            GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(0, 0, Screen.width, Screen.height));
            GUILayout.Space(10); // top padding
            GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("Open on GitHub"))
                    Application.OpenURL("https://github.com/unity-effects/mosaix");
            GUILayout.Space(10); // right padding
            GUILayout.EndHorizontal();
        GUILayout.EndArea();

        GUILayout.BeginArea(new Rect(10, 10, Screen.width-20, Screen.height-20));
            GUILayout.BeginVertical(MakePaddedBoxStyle(10,10), GUILayout.MinWidth(Open? 250:10));
            Open = GUILayout.Toggle(Open, "Mosaic Controls");
            if(Open)
                ControlWindow();
            GUILayout.EndVertical();
        GUILayout.EndArea();

        Refresh();
    }

    void ControlWindow()
    {
        MosaixComponent.enabled = GUILayout.Toggle(MosaixComponent.enabled, "Enable mosaic");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Blocks");
        MosaicBlocks = GUILayout.HorizontalSlider(MosaicBlocks, 2, 100);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Alpha");
        MosaixComponent.Alpha = GUILayout.HorizontalSlider(MosaixComponent.Alpha, 0, 1);
        GUILayout.EndHorizontal();

        MosaixComponent.FollowAnchor = GUILayout.Toggle(MosaixComponent.FollowAnchor, "Anchor transform");
        MosaixComponent.ScaleMosaicToAnchorDistance = GUILayout.Toggle(MosaixComponent.ScaleMosaicToAnchorDistance, "Scale mosaic");

        MosaixComponent.SphereMasking = GUILayout.Toggle(MosaixComponent.SphereMasking, "Sphere masking");
        if(MosaixComponent.SphereMasking)
        {
            BeginGroup();

            ShowSphere = GUILayout.Toggle(ShowSphere, "Show sphere");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sphere fade");
            MosaixComponent.MaskFade = GUILayout.HorizontalSlider(MosaixComponent.MaskFade, 0, 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position");
            SpherePosition = GUILayout.HorizontalSlider(SpherePosition, 0, 1);
            GUILayout.EndHorizontal();

            SphereMask.transform.position = Vector3.Lerp(SphereMaskStart.position, SphereMaskEnd.position, SpherePosition);
            SphereMask.transform.rotation = Quaternion.Lerp(SphereMaskStart.rotation, SphereMaskEnd.rotation, SpherePosition);

            if(ScaleControl)
            {
                Vector3 SphereScale = SphereMask.transform.localScale;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scale X");
                SphereScale.x = GUILayout.HorizontalSlider(SphereScale.x, 0.25f, 2);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scale Y");
                SphereScale.y = GUILayout.HorizontalSlider(SphereScale.y, 0.25f, 2);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Scale Z");
                SphereScale.z = GUILayout.HorizontalSlider(SphereScale.z, 0.25f, 2);
                GUILayout.EndHorizontal();
                SphereMask.transform.localScale = SphereScale;
            } else {
                SphereMask.transform.localScale = Vector3.Lerp(SphereMaskStart.localScale, SphereMaskEnd.localScale, SpherePosition);
            }

            EndGroup();
        }

        if(MosaixComponent.MaskingTexture != null)
            MosaixComponent.TextureMasking = GUILayout.Toggle(MosaixComponent.TextureMasking, "Texture masking");
        MosaixComponent.ShowMask = GUILayout.Toggle(MosaixComponent.ShowMask, "Show mask");
    }

    int level = 0;
    void BeginGroup()
    {
        ++level;
        GUILayout.BeginHorizontal();
        GUILayout.Space(level*15); // left padding
        GUILayout.BeginVertical();
    }

    void EndGroup()
    {
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        --level;
    }

    void Refresh()
    {
        MosaixComponent.MosaicBlocks = MosaicBlocks;
        Renderer SphereMaskRenderer = SphereMask.GetComponent<Renderer>();
        SphereMaskRenderer.enabled = MosaixComponent.SphereMasking && ShowSphere;
        
        if(MosaixComponent.ScaleMosaicToAnchorDistance)
            MosaixComponent.MosaicBlocks *= 5;
    }
}
