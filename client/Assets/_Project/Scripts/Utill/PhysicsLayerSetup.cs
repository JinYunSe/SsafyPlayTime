using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class PhysicsLayerSetup
{
    const int CharacterLayer = 8;
    const int RagdollLayer = 9;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ApplyCollisionRules()
    {
        Physics.IgnoreLayerCollision(CharacterLayer, RagdollLayer, true);
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void RegisterEditorHooks()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            Physics.IgnoreLayerCollision(CharacterLayer, RagdollLayer, true);
    }
#endif
}
