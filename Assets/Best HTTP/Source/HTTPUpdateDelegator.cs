using System;
using System.Threading;

using UnityEngine;

#if NETFX_CORE
    using System.Threading.Tasks;
#endif

namespace BestHTTP
{
    /// <summary>
    /// Will route some U3D calls to the HTTPManager.
    /// </summary>
    [ExecuteInEditMode]
    [BestHTTP.PlatformSupport.IL2CPP.Il2CppEagerStaticClassConstructionAttribute]
    public sealed class HTTPUpdateDelegator : MonoBehaviour
    {
        #region Public Properties

        /// <summary>
        /// The singleton instance of the HTTPUpdateDelegator
        /// </summary>
        public static HTTPUpdateDelegator Instance { get; private set; }

        /// <summary>
        /// True, if the Instance property should hold a valid value.
        /// </summary>
        public static bool IsCreated { get; private set; }

        /// <summary>
        /// Set it true before any CheckInstance() call, or before any request sent to dispatch callbacks on another thread.
        /// </summary>
        public static bool IsThreaded { get; set; }

        /// <summary>
        /// It's true if the dispatch thread running.
        /// </summary>
        public static bool IsThreadRunning { get; private set; }

        /// <summary>
        /// How much time the plugin should wait between two update call. Its default value 100 ms.
        /// </summary>
        public static int ThreadFrequencyInMS { get; set; }

        /// <summary>
        /// Called in the OnApplicationQuit function. If this function returns False, the plugin will not start to
        /// shut down itself.
        /// </summary>
        public static System.Func<bool> OnBeforeApplicationQuit;

        public static System.Action<bool> OnApplicationForegroundStateChanged;

        #endregion

        private static bool IsSetupCalled;
        private int isHTTPManagerOnUpdateRunning;

#if UNITY_EDITOR
        /// <summary>
        /// Called after scene loaded to support Configurable Enter Play Mode (https://docs.unity3d.com/2019.3/Documentation/Manual/ConfigurableEnterPlayMode.html)
        /// </summary>
#if UNITY_2019_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        static void ResetSetup()
        {
            IsSetupCalled = false;
            HTTPManager.Logger.Information("HTTPUpdateDelegator", "Reset called!");
        }
#endif

        static HTTPUpdateDelegator()
        {
            ThreadFrequencyInMS = 100;
        }

        /// <summary>
        /// Will create the HTTPUpdateDelegator instance and set it up.
        /// </summary>
        public static void CheckInstance()
        {
            try
            {
                if (!IsCreated)
                {
                    GameObject go = GameObject.Find("HTTP Update Delegator");

                    if (go != null)
                        Instance = go.GetComponent<HTTPUpdateDelegator>();

                    if (Instance == null)
                    {
                        go = new GameObject("HTTP Update Delegator");
                        go.hideFlags = HideFlags.HideAndDontSave;
                        
                        Instance = go.AddComponent<HTTPUpdateDelegator>();
                    }
                    IsCreated = true;

#if UNITY_EDITOR
                    if (!UnityEditor.EditorApplication.isPlaying)
                    {
                        UnityEditor.EditorApplication.update -= Instance.Update;
                        UnityEditor.EditorApplication.update += Instance.Update;
                    }

#if UNITY_2017_2_OR_NEWER
                    UnityEditor.EditorApplication.playModeStateChanged -= Instance.OnPlayModeStateChanged;
                    UnityEditor.EditorApplication.playModeStateChanged += Instance.OnPlayModeStateChanged;
#else
                    UnityEditor.EditorApplication.playmodeStateChanged -= Instance.OnPlayModeStateChanged;
                    UnityEditor.EditorApplication.playmodeStateChanged += Instance.OnPlayModeStateChanged;
#endif
#endif

                    // https://docs.unity3d.com/ScriptReference/Application-wantsToQuit.html
                    Application.wantsToQuit -= UnityApplication_WantsToQuit;
                    Application.wantsToQuit += UnityApplication_WantsToQuit;

                    HTTPManager.Logger.Information("HTTPUpdateDelegator", "Instance Created!");
                }
            }
            catch
            {
                HTTPManager.Logger.Error("HTTPUpdateDelegator", "Please call the BestHTTP.HTTPManager.Setup() from one of Unity's event(eg. awake, start) before you send any request!");
            }
        }

        public void SwapThreadingMode()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (IsThreaded)
            {
                IsThreadRunning = false;
                IsThreaded = false;
            }
            else
            {
                IsThreaded = true;
                PlatformSupport.Threading.ThreadedRunner.RunLongLiving(ThreadFunc);
            }
#endif
        }

        private void Setup()
        {
            if (IsSetupCalled)
                return;
            IsSetupCalled = true;

            HTTPManager.Setup();

#if UNITY_WEBGL && !UNITY_EDITOR
            // Threads are not implemented in WEBGL builds, disable it for now.
            IsThreaded = false;
#endif
            if (IsThreaded)
                PlatformSupport.Threading.ThreadedRunner.RunLongLiving(ThreadFunc);

            // Unity doesn't tolerate well if the DontDestroyOnLoad called when purely in editor mode. So, we will set the flag
            //  only when we are playing, or not in the editor.
            if (!Application.isEditor || Application.isPlaying)
                GameObject.DontDestroyOnLoad(this.gameObject);

            HTTPManager.Logger.Information("HTTPUpdateDelegator", "Setup done!");
        }

        void ThreadFunc()
        {
            HTTPManager.Logger.Information ("HTTPUpdateDelegator", "Update Thread Started");

            try
            {
                IsThreadRunning = true;
                while (IsThreadRunning)
                {
                    CallOnUpdate();

#if NETFX_CORE
	                await Task.Delay(ThreadFrequencyInMS);
#else
                    System.Threading.Thread.Sleep(ThreadFrequencyInMS);
#endif
                }
            }
            finally
            {
                HTTPManager.Logger.Information("HTTPUpdateDelegator", "Update Thread Ended");
            }
        }

        void Update()
        {
            if (!IsSetupCalled)
                Setup();

            if (!IsThreaded)
                CallOnUpdate();
        }

        private void CallOnUpdate()
        {
            // Prevent overlapping call of OnUpdate from unity's main thread and a separate thread
            if (Interlocked.CompareExchange(ref isHTTPManagerOnUpdateRunning, 1, 0) == 0)
            {
                try
                {
                    HTTPManager.OnUpdate();
                }
                finally
                {
                    Interlocked.Exchange(ref isHTTPManagerOnUpdateRunning, 0);
                }
            }
        }

#if UNITY_EDITOR
#if UNITY_2017_2_OR_NEWER
        void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange playMode)
        {
            if (playMode == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                UnityEditor.EditorApplication.update -= Update;
            }
            else if (playMode == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                UnityEditor.EditorApplication.update -= Update;
                UnityEditor.EditorApplication.update += Update;

                HTTPUpdateDelegator.ResetSetup();
                HTTPManager.ResetSetup();
            }
        }
#else
        void OnPlayModeStateChanged()
        {
            if (UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.update -= Update;
            else if (!UnityEditor.EditorApplication.isPlaying)
                UnityEditor.EditorApplication.update += Update;
        }

#endif
#endif

        void OnDisable()
        {
            HTTPManager.Logger.Information("HTTPUpdateDelegator", "OnDisable Called!");

#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
#endif
                UnityApplication_WantsToQuit();
        }

        void OnApplicationPause(bool isPaused)
        {
            HTTPManager.Logger.Information("HTTPUpdateDelegator", "OnApplicationPause isPaused: " + isPaused);

            if (HTTPUpdateDelegator.OnApplicationForegroundStateChanged != null)
                HTTPUpdateDelegator.OnApplicationForegroundStateChanged(isPaused);
        }

        private static bool UnityApplication_WantsToQuit()
        {
            HTTPManager.Logger.Information("HTTPUpdateDelegator", "UnityApplication_WantsToQuit Called!");

            if (OnBeforeApplicationQuit != null)
            {
                try
                {
                    if (!OnBeforeApplicationQuit())
                    {
                        HTTPManager.Logger.Information("HTTPUpdateDelegator", "OnBeforeApplicationQuit call returned false, postponing plugin and application shutdown.");
                        return false;
                    }
                }
                catch (System.Exception ex)
                {
                    HTTPManager.Logger.Exception("HTTPUpdateDelegator", string.Empty, ex);
                }
            }

            IsThreadRunning = false;

            if (!IsCreated)
                return true;

            IsCreated = false;

            HTTPManager.OnQuit();

            return true;
        }
    }
}
