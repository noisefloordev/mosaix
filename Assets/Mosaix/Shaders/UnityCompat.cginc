// Disable Unity's horrifying automatic upgrade thing that modifies your
// source code without asking: UNITY_SHADER_NO_UPGRADE

// Work around breaking changes in Unity 5.4 and up:
#if UNITY_VERSION < 540
#define UnityObjectToClipPos(v) mul(UNITY_MATRIX_MVP, v)
#define unity_ObjectToWorld _Object2World
#endif

