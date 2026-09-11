using System;
using UnityEngine;

namespace Kaleido
{
    public enum SceneKind { Dome, Object }

    /// <summary>One playlist item: which renderer class to use plus its configuration.</summary>
    public class SceneDefinition
    {
        public string Id;
        public string Title;
        public SceneKind Kind;
        public Func<GameObject, KaleidoSceneBase> Factory;
        /// <summary>Free-form config consumed by the scene class (e.g. mirror mode, colours).</summary>
        public object Config;

        public SceneDefinition(string id, string title, SceneKind kind, Func<GameObject, KaleidoSceneBase> factory, object config = null)
        {
            Id = id; Title = title; Kind = kind; Factory = factory; Config = config;
        }
    }
}
