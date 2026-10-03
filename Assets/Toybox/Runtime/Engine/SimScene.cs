using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toybox.Engine
{
    /// <summary>
    /// The Unity scene the simulation lives in, with a PhysX scene of its own.
    ///
    /// PhysX only replays identically from a freshly created scene: after bodies have been added to and
    /// removed from a scene, its internal ordering differs and the same inputs give (slightly, then
    /// wildly) different results. So every level load gets a brand-new physics scene, which makes a
    /// level behave the same in its test, after a restart, and after any other level.
    ///
    /// In Play Mode and in the build this is a runtime scene with local physics. Outside Play Mode Unity
    /// does not allow creating those, so an editor preview scene is used; that is the one kind of scene
    /// that has its own physics there.
    /// </summary>
    public sealed class SimScene
    {
        static int created;

        readonly bool owned;
        bool closed;

        public Scene Scene { get; }
        public PhysicsScene Physics { get; }
        /// <summary>False when the simulation shares the open scene and the default physics scene.</summary>
        public bool Isolated => owned;

        SimScene(Scene scene, PhysicsScene physics, bool owned)
        {
            Scene = scene;
            Physics = physics;
            this.owned = owned;
        }

        public static SimScene Create(bool isolated)
        {
            if (!isolated)
                return new SimScene(SceneManager.GetActiveScene(), UnityEngine.Physics.defaultPhysicsScene, false);

            Scene scene;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            else
#endif
                scene = SceneManager.CreateScene("Toybox Sim " + (++created), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            return new SimScene(scene, scene.GetPhysicsScene(), true);
        }

        /// <summary>Moves a root GameObject (and with it its whole hierarchy) into this scene.</summary>
        public void Adopt(GameObject root)
        {
            if (owned) SceneManager.MoveGameObjectToScene(root, Scene);
        }

        /// <summary>Closes the scene, destroying whatever is still in it.</summary>
        public void Close()
        {
            if (closed || !owned) return;
            closed = true;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(Scene);
                return;
            }
#endif
            if (Scene.IsValid() && Scene.isLoaded) SceneManager.UnloadSceneAsync(Scene);
        }
    }
}
