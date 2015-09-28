using UnityEngine;
using System.Collections;
using UnityEditor;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

public class LogCatWindow : EditorWindow
{
    // How many log entries to store in memory. Keep it low for better performance.
    private const int memoryLimit = 1000;

    // How many log entries to show in unity3D editor. Keep it low for better performance.
    private const int showLimit = 150;


    //COLORS Cache
    private static readonly Color color_error = new Color(0.75f, 0.5f, 0.5f, 1f);
    private static readonly Color color_info = new Color(0.5f, 0.75f, 0.5f, 1f);
    private static readonly Color color_warning =  new Color(0.95f, 0.95f, 0.3f, 1f);
    private static readonly Color color_debug = new Color(0.5f, 0.5f, 0.75f, 1f);
    private static readonly Color color_background = new Color(1.0f, 1.0f, 1.0f, 0.1f);
    

    // Filters
    private bool prefilterOnlyUnity = true;
    private bool filterOnlyError = false;
    private bool filterOnlyWarning = false;
    private bool filterOnlyDebug = false;
    private bool filterOnlyInfo = false;
    private bool filterOnlyVerbose = false;
    private string filterByString = String.Empty;

    // Android adb logcat process
    private Process logCatProcess;

    //Mode
    private bool UseInPlayMode= false;
    private bool UseInNotPlayMode= false;

    // Log entries
    private List<LogCatLog> logsList = new List<LogCatLog>();
    private List<LogCatLog> filteredList = new List<LogCatLog>(memoryLimit);

    // Filtered GUI list scroll position
    private Vector2 scrollPosition = new Vector2(0, 0);

    // Add menu item named "LogCat" to the Window menu
    [MenuItem("Window/LogCat - Android Logger")]
    public static void ShowWindow()
    {
        // Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(LogCatWindow), false, "Logcat");
    }

    void OnEnable()
    {
        EditorApplication.playmodeStateChanged += StateChange;
    }

    void Update()
    {
        if (logsList.Count == 0)
            return;

        lock (logsList)
        {
            // Filter
            filteredList = logsList.Where(log => (filterByString.Length <= 2 || log.Message.ToLower().Contains(filterByString.ToLower())) &&
                                          ((!filterOnlyError && !filterOnlyWarning && !filterOnlyDebug && !filterOnlyInfo && !filterOnlyVerbose)
             || filterOnlyError && log.Type == 'E'
             || filterOnlyWarning && log.Type == 'W'
             || filterOnlyDebug && log.Type == 'D'
             || filterOnlyInfo && log.Type == 'I'
             || filterOnlyVerbose && log.Type == 'V')).ToList();
        }
    }

    void OnGUI()
    {
        GUILayout.BeginHorizontal();

        // Enable pre-filter if process is not started
        GUI.enabled = logCatProcess == null;
        prefilterOnlyUnity = GUILayout.Toggle(prefilterOnlyUnity, "Only Unity", "Button", GUILayout.Width(75f));

        // Enable button if process is not started
        GUI.color = UseInNotPlayMode ? color_info : color_error;
        bool tmp_UseInNotPlayMode = GUILayout.Toggle(UseInNotPlayMode, "NotPlay", "Button", GUILayout.Width(70f));
        GUI.color = UseInPlayMode ? color_info : color_error;
        bool tmp_UseInPlayMode = GUILayout.Toggle(UseInPlayMode, "PlayMode", "Button", GUILayout.Width(70f));

        GUI.color = Color.white;
        //Check differences
        if (tmp_UseInNotPlayMode != UseInNotPlayMode)
        {
            if (tmp_UseInNotPlayMode && !EditorApplication.isPlaying && logCatProcess == null)
            {
                StartLogCat();
            }
            else if (!tmp_UseInNotPlayMode && logCatProcess != null)
            {
                StopLogCat();
            }
            UseInNotPlayMode = tmp_UseInNotPlayMode;
        }
        if (tmp_UseInPlayMode != UseInPlayMode)
        {
            if (tmp_UseInPlayMode && EditorApplication.isPlaying && logCatProcess == null)
            {
                StartLogCat();
            }
            else if (!tmp_UseInPlayMode && logCatProcess != null)
            {
                StopLogCat();
            }
            UseInPlayMode = tmp_UseInPlayMode;
        }

        // Disable button if process is already started
        GUI.enabled = logCatProcess != null;
        if (GUILayout.Button("Stop", GUILayout.Width(55f)))
        {
            UseInNotPlayMode = false;
            UseInPlayMode = false;
            StopLogCat();
        }

        GUI.enabled = true;
        if (GUILayout.Button("Clear", GUILayout.Width(55f)))
        {
            lock (logsList)
            {
                logsList.Clear();
                filteredList.Clear();
            }
        }

        GUILayout.Label(filteredList.Count + " matching logs", GUILayout.Height(20f), GUILayout.Width(100f));

        // Create filters
        filterByString = GUILayout.TextField(filterByString, GUILayout.Height(20f));
        GUI.color = color_error;
        filterOnlyError = GUILayout.Toggle(filterOnlyError, "Error", "Button", GUILayout.Width(60f));
        GUI.color = color_warning;
        filterOnlyWarning = GUILayout.Toggle(filterOnlyWarning, "Warning", "Button", GUILayout.Width(60f));
        GUI.color = color_debug;
        filterOnlyDebug = GUILayout.Toggle(filterOnlyDebug, "Debug", "Button", GUILayout.Width(60f));
        GUI.color = color_info;
        filterOnlyInfo = GUILayout.Toggle(filterOnlyInfo, "Info", "Button", GUILayout.Width(60f));
        GUI.color = Color.white;
        filterOnlyVerbose = GUILayout.Toggle(filterOnlyVerbose, "Verbose", "Button", GUILayout.Width(60f));

        GUILayout.EndHorizontal();

        GUIStyle lineStyle = new GUIStyle();
        lineStyle.normal.background = MakeTexture(600, 1, color_background);

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(Screen.height - 45));

        // Show only top `showingLimit` log entries
        int fromIndex = filteredList.Count - showLimit;
        if (fromIndex < 0)
            fromIndex = 0;

        for (int i = fromIndex; i < filteredList.Count; i++)
        {
            LogCatLog log = filteredList[i];
            GUI.backgroundColor = log.GetBgColor();
            GUILayout.BeginHorizontal(lineStyle);
            GUILayout.Label(log.CreationDate + " | " + log.Message);
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    void StateChange()
    {
        if (logCatProcess == null)
        {
            if (EditorApplication.isPlaying && UseInPlayMode)
            {
                StartLogCat();
            }
            else if (!EditorApplication.isPlaying && UseInNotPlayMode)
            {
                StartLogCat();
            }
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            StopLogCat();
        }
    }

    private void StartLogCat()
    {
        // Start `adb logcat` (with additional optional arguments) process for filtering
                ProcessStartInfo logProcessInfo = new ProcessStartInfo();
                logProcessInfo.CreateNoWindow = true;
                logProcessInfo.UseShellExecute = false;
                logProcessInfo.RedirectStandardOutput = true;
                logProcessInfo.RedirectStandardError = true;
                logProcessInfo.FileName = EditorPrefs.GetString("AndroidSdkRoot") + "/platform-tools/adb";
                logProcessInfo.WindowStyle = ProcessWindowStyle.Hidden;

                // Add additional -s argument for filtering by Unity tag.
                logProcessInfo.Arguments = "logcat" + (prefilterOnlyUnity ? " -s  \"Unity\"" : "");

                logCatProcess = Process.Start(logProcessInfo);

                logCatProcess.ErrorDataReceived += (sender, errorLine) =>
                {
                    if (errorLine.Data != null && errorLine.Data.Length > 2)
                        AddLog(new LogCatLog(errorLine.Data));
                };
                logCatProcess.OutputDataReceived += (sender, outputLine) =>
                {
                    if (outputLine.Data != null && outputLine.Data.Length > 2)
                        AddLog(new LogCatLog(outputLine.Data));
                };
                logCatProcess.BeginErrorReadLine();
                logCatProcess.BeginOutputReadLine();
    }

    private void StopLogCat()
    {
        try
        {
            logCatProcess.Kill();
        }
#pragma warning disable 0168
        catch (InvalidOperationException ex)
#pragma warning restore 0168
        {
            // Just ignore it.
        }
        finally
        {
            logCatProcess = null;

        }
    }

    private Texture2D MakeTexture(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];

        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }

    private void AddLog(LogCatLog log)
    {
        lock (logsList)
        {
            if (logsList.Count > memoryLimit + 1)
                logsList.RemoveRange(0, logsList.Count - memoryLimit + 1);

            logsList.Add(log);
        }
    }

    private class LogCatLog
    {
        public LogCatLog(string data)
        {
            // First char indicates error type:
            // W - warning
            // E - error
            // D - debug
            // I - info
            // V - verbose
            Type = data[0];

            Message = data.Substring(2);
            CreationDate = DateTime.Now.ToString("H:mm:ss");
        }

        public string CreationDate
        {
            get;
            set;
        }

        public char Type
        {
            get;
            set;
        }

        public string Message
        {
            get;
            set;
        }

        public Color GetBgColor()
        {
            switch (Type)
            {
                case 'W':
                    return Color.yellow;

                case 'I':
                    return Color.green;

                case 'E':
                    return Color.red;

                case 'D':
                    return Color.blue;

                case 'V':
                default:
                    return Color.grey;
            }
        }
    }
}