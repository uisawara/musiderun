using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    [InitializeOnLoad]
    internal static class EditorMainThreadDispatcher
    {
        private static readonly Queue<Action> Queue = new();
        private static bool _registered;

        static EditorMainThreadDispatcher()
        {
            EditorApplication.delayCall += EnsureRegistered;
        }

        public static void Enqueue(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (Queue)
            {
                Queue.Enqueue(action);
                EnsureRegistered();
            }
        }

        public static void EnsureRegistered()
        {
            if (_registered)
            {
                return;
            }

            EditorApplication.update += Drain;
            _registered = true;
        }

        private static void Drain()
        {
            while (true)
            {
                Action action;
                lock (Queue)
                {
                    if (Queue.Count == 0)
                    {
                        return;
                    }

                    action = Queue.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
