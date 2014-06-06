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
    private const int memoryLimit = 2000;

    // How many log entries to show in unity3D editor. Keep it low for better performance.
    private const int showLimit = 200;
   
    // Filters
    private bool filterOnlyErrors;
    private bool filterOnlyWarnings;
    private bool filterOnlyDebugs;
    private string searchString = "";

    // Android adb logcat proccess
    private Process proccess;

    // Log entries
    private List<LogCatLog> logsList = new List<LogCatLog>();
    private List<LogCatLog> filteredList = new List<LogCatLog>(memoryLimit);

    // Filtered GUI list scroll position
    private Vector2 scrollPosition = new Vector2(0, 0);
    
    // Add menu item named "LogCat" to the Window menu
    [MenuItem("Window/LogCat - Android Logger")]
    public static void ShowWindow()
    {
        //Show existing window instance. If one doesn't exist, make one.
        EditorWindow.GetWindow(typeof(LogCatWindow), false, "Logcat");
    }
    
    void Update()
    {
        if (logsList.Count == 0)
            return;

        lock (logsList)
        {
            // Filter
            filteredList = logsList.Where(log => (searchString.Length <= 2 || log.Message.ToLower().Contains(searchString.ToLower())) &&
                ((!filterOnlyErrors && !filterOnlyWarnings && !filterOnlyDebugs) 
                || filterOnlyErrors && log.Type == 'E' 
                || filterOnlyWarnings && log.Type == 'W' 
                || filterOnlyDebugs && log.Type == 'D')).ToList();
        }
    }

    void OnGUI()
    {
        GUILayout.BeginHorizontal();

        // Enable button if proccess is not started
        GUI.enabled = proccess == null;
        if (GUILayout.Button("Start logging", GUILayout.Height(20), GUILayout.Width(100)))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = false;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.FileName = EditorPrefs.GetString("AndroidSdkRoot") + "/platform-tools/adb";
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.Arguments = @"logcat";

            proccess = Process.Start(startInfo);  
            
            proccess.ErrorDataReceived += (sender, errorLine) => { 
                if (errorLine.Data != null && errorLine.Data.Length > 2)
                    addToLogList(new LogCatLog(errorLine.Data)); 
            };
            proccess.OutputDataReceived += (sender, outputLine) => { 
                if (outputLine.Data != null && outputLine.Data.Length > 2)
                    addToLogList(new LogCatLog(outputLine.Data)); 
            };
            proccess.BeginErrorReadLine();
            proccess.BeginOutputReadLine();
        }
        
        // Disable button if proccess is already started
        GUI.enabled = proccess != null;
        if (GUILayout.Button("Stop logging", GUILayout.Height(20), GUILayout.Width(100)))
        {
            proccess.Kill();
            proccess = null;
        }

        GUI.enabled = true;
        if (GUILayout.Button("Clear", GUILayout.Height(20), GUILayout.Width(100)))
        {
            lock (logsList)
            {
                logsList.Clear();
                filteredList.Clear();
            }
        }

        GUILayout.Label("total " + filteredList.Count + " logs", GUILayout.Height(20));
        
        // Create filters
        filterOnlyErrors = GUILayout.Toggle(filterOnlyErrors, "Errors", "Button", GUILayout.Width(80));
        filterOnlyWarnings = GUILayout.Toggle(filterOnlyWarnings, "Warnings", "Button", GUILayout.Width(80));
        filterOnlyDebugs = GUILayout.Toggle(filterOnlyDebugs, "Debugs", "Button", GUILayout.Width(80));

        searchString = GUILayout.TextField(searchString, GUILayout.Height(20));

        GUILayout.EndHorizontal(); 

        GUIStyle lineStyle = new GUIStyle();
        lineStyle.normal.background = MakeTex(600, 1, new Color(1.0f, 1.0f, 1.0f, 0.1f));

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(Screen.height - 45));
        
        // Show only top `showingLimit` log entries
        int fromIndex = filteredList.Count - showLimit;
        if (fromIndex < 0)
            fromIndex = 0;

        for (int i = fromIndex; i < filteredList.Count; i++)
        {
            LogCatLog log = filteredList [i];
            GUI.backgroundColor = log.getBgColor();
            GUILayout.BeginHorizontal(lineStyle);
            GUILayout.Label(log.CreationDate + " | " + log.Message);
            GUILayout.EndHorizontal(); 
        }

        GUILayout.EndScrollView();
    }

    private class LogCatLog
    {
        public LogCatLog(string data)
        {
            Type = data [0];
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

        public Color getBgColor()
        {
            switch (Type)
            {
                case 'W':
                    return Color.yellow;

                case 'E':
                    return Color.red;

                case 'D':
                    return Color.blue;

                default:
                    return Color.grey;
            }
        }
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        
        for (int i = 0; i < pix.Length; i++)
            pix [i] = col;
        
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        
        return result;
    }

    private void addToLogList(LogCatLog log)
    {
        lock (logsList)
        {
            if (logsList.Count > memoryLimit + 1)
                logsList.RemoveRange(0, logsList.Count - memoryLimit + 1);

            logsList.Add(log);
        }
    }
}
