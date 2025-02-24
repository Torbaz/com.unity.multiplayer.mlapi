using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MLAPI;
using MLAPI.Transports.UNET;


/// <summary>
/// Provides basic command line handling capabilities
/// Commands:
/// -s    | Scene to load.
/// -ip   | IP address of the host-server.
/// -p    | The connection listening port.
/// -fr   | Set the target frame rate.
/// -m (?)| Start MLAPI in one of 3 modes: client, host, server
/// </summary>
public class CommandLineProcessor
{
    private static CommandLineProcessor s_Singleton;
    private static uint s_LoadingIterations;

    private string m_SceneToLoad;
    private Dictionary<string, string> m_CommandLineArguments = new Dictionary<string, string>();
    private ConnectionModeScript m_ConnectionModeScript;
    public CommandLineProcessor(string[] args)
    {
        try
        {        
            if(s_Singleton != null)
            {
                Debug.LogError($"More than one {nameof(CommandLineProcessor)} has been instantiated!");
                throw new Exception();
            }
        }
        catch(Exception ex)
        {
            Debug.LogError($"Stack Trace: {ex.StackTrace}");
        }

        s_Singleton = this;
        m_CommandLineArguments = new Dictionary<string, string>();

        for (int i = 0; i < args.Length; ++i)
        {
            var arg = args[i].ToLower();
            if (arg.StartsWith("-"))
            {
                var value = i < args.Length - 1 ? args[i + 1].ToLower() : null;
                value = (value?.StartsWith("-") ?? false) ? null : value;

                m_CommandLineArguments.Add(arg, value);
            }
        }

        ProcessCommandLine();
    }

    public bool AutoConnectEnabled()
    {
        if (m_CommandLineArguments.TryGetValue("-m", out string mlapiValue))
        {
            switch (mlapiValue)
            {
                case "server":                   
                case "host":
                case "client":
                {

                    return true;
                }
            }
        }
        return false;
    }

    public void ProcessCommandLine()
    {
        if (m_CommandLineArguments.Count > 0)
        {
            if (m_CommandLineArguments.TryGetValue("-s", out string sceneToLoad))
            {
                if (m_SceneToLoad != sceneToLoad)
                {
                    m_SceneToLoad = sceneToLoad;
                    StartSceneSwitch();
                    return;
                }
            }

            if (m_CommandLineArguments.TryGetValue("-ip", out string ipValue))
            {
                SetTransportAddress(ipValue);
                m_CommandLineArguments.Remove("-ip");
            }

            if (m_CommandLineArguments.TryGetValue("-p", out string port))
            {
                SetPort(ushort.Parse(port));
                m_CommandLineArguments.Remove("-p");
            }

            if (m_CommandLineArguments.TryGetValue("-fr", out string frameRate))
            {
                Application.targetFrameRate = int.Parse(frameRate);
                m_CommandLineArguments.Remove("-fr");
            }

            if (m_CommandLineArguments.TryGetValue("-m", out string mlapiValue))
            {
                switch (mlapiValue)
                {
                    case "server":
                        StartServer();
                        break;
                    case "host":
                        StartHost();
                        break;
                    case "client":
                        StartClient();
                        break;
                    default:
                        Debug.LogWarning($"Invalid MLAPI argument: {mlapiValue}");
                        break;
                }
            }
        }
    }

    private void StartSceneSwitch()
    {
        if(m_SceneToLoad != string.Empty)
        {
            SceneManager.sceneLoaded += SceneManager_sceneLoaded;
            SceneManager.LoadSceneAsync(m_SceneToLoad, LoadSceneMode.Single);
            m_CommandLineArguments.Remove("-s");
        }
    }

    private void SceneManager_sceneLoaded(Scene sceneLoaded, LoadSceneMode sceneLoadingMode)
    {
        if (sceneLoaded.name.ToLower() == m_SceneToLoad)
        {
            SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
            var connectionModeButtons = GameObject.Find("ConnectionModeButtons");
            if (connectionModeButtons)
            {
                m_ConnectionModeScript = connectionModeButtons.GetComponent<ConnectionModeScript>();
                if (m_ConnectionModeScript)
                {
                    m_ConnectionModeScript.SetCommandLineHandler(this);
         
                    return;
                }
            }
            ProcessCommandLine();

        }
    }

    private void StartServer()
    {
        m_CommandLineArguments.Remove("-m");
        if (m_ConnectionModeScript)
        {
            m_ConnectionModeScript.OnStartServer();
        }
        else
        {
            NetworkManager.Singleton.StartServer();
        }
    }

    private void StartHost()
    {
        if (m_ConnectionModeScript)
        {
            m_ConnectionModeScript.OnStartHost();
        }
        else
        {
            NetworkManager.Singleton.StartHost();
        }
    }

    private void StartClient()
    {
        if (m_ConnectionModeScript)
        {
            m_ConnectionModeScript.OnStartClient();
        }
        else
        {
            NetworkManager.Singleton.StartClient();
        }       
    }

    private void SetTransportAddress(string address)
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        switch (transport)
        {
            case UNetTransport unetTransport:
                unetTransport.ConnectAddress = address;
                break;
        }
    }

    private void SetPort(ushort port)
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        switch (transport)
        {
            case UNetTransport unetTransport:
                unetTransport.ConnectPort = port;
                unetTransport.ServerListenPort = port;
                break;
        }
    }
}

/// <summary>
/// Command line handler component to attach to a GameObject in the
/// Build Settings index 0 slot.
/// </summary>
public class CommandLineHandler : MonoBehaviour
{
    private static CommandLineProcessor s_CommandLineProcessorInstance;
    private void Start()
    {
        if(s_CommandLineProcessorInstance == null)
        {
            s_CommandLineProcessorInstance = new CommandLineProcessor(Environment.GetCommandLineArgs());
        }
        
    }
}
