using UnityEngine;

public class SceneController: MonoBehaviour 
{
    public Animator animator;
    public void Start()
    {
        animator.Play("WALK00_F");
    }
}

