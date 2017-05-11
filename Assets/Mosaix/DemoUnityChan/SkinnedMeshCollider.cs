// MayaCamera wants meshes to collide to find out what the user clicked on, but MeshColliders
// don't support skinned meshes.  Put on the same object as a SkinnedMeshRenderer, and
// SkinnedMeshCollider.UpdateAllColliders will copy the mesh into the collider for all objects
// that have this script on them.  This may be slow for heavy meshes, so we only call it when
// the user clicks to tumble the screen and not every frame.
//
// This is only used by the DemoUnityChan scene.

using UnityEngine;
using System.Collections.Generic;

public class SkinnedMeshCollider: MonoBehaviour
{
    static HashSet<SkinnedMeshCollider> SkinnedMeshColliders = new HashSet<SkinnedMeshCollider>();
   
    static public void UpdateAllColliders()
    {
        foreach(SkinnedMeshCollider SkinnedCollider in SkinnedMeshColliders)
            SkinnedCollider.UpdateCollider();
    }
    
    public void UpdateCollider()
    {
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if(meshCollider != null)
            meshCollider.sharedMesh = null;

        SkinnedMeshRenderer meshRenderer = GetComponent<SkinnedMeshRenderer>();
        if(meshCollider == null || meshRenderer == null)
            return;

        Mesh colliderMesh = new Mesh();
        meshRenderer.BakeMesh(colliderMesh);
        meshCollider.sharedMesh = colliderMesh;
    }

    void OnEnable()
    {
        SkinnedMeshColliders.Add(this);
        UpdateCollider();
    }

    void OnDisable()
    {
        SkinnedMeshColliders.Remove(this);
    }
};

