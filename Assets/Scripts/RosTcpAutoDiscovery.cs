using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Finds a reachable ROS TCP endpoint on the local network and connects Unity to it.
/// This avoids hardcoding the ROS host IP when the host changes between Wi-Fi/Ethernet networks.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class RosTcpAutoDiscovery : MonoBehaviour
{
    private const int ConnectTimeoutMs = 250;
    private const int MaxConcurrency = 32;
    private const int MaxHostsPerSubnet = 1024;
    private const float RetryDelaySeconds = 5f;
    private const string LastSuccessfulIpKey = "ROS_TCP_AUTODISCOVERY_LAST_IP";

    private static RosTcpAutoDiscovery s_instance;

    private ROSConnection rosConnection;
    private Coroutine discoveryCoroutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;

        GameObject bootstrapObject = new GameObject(nameof(RosTcpAutoDiscovery));
        bootstrapObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(bootstrapObject);
        s_instance = bootstrapObject.AddComponent<RosTcpAutoDiscovery>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryBindToRosConnection();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryBindToRosConnection();
    }

    private void Update()
    {
        if (rosConnection == null)
        {
            TryBindToRosConnection();
        }
    }

    private void TryBindToRosConnection()
    {
        ROSConnection foundConnection = FindFirstObjectByType<ROSConnection>();
        if (foundConnection == null) return;
        if (ReferenceEquals(rosConnection, foundConnection)) return;

        rosConnection = foundConnection;
        rosConnection.ConnectOnStart = false;

        StartDiscoveryLoop();
    }

    private void StartDiscoveryLoop()
    {
        if (discoveryCoroutine != null)
        {
            StopCoroutine(discoveryCoroutine);
        }

        discoveryCoroutine = StartCoroutine(DiscoveryLoop());
    }

    private IEnumerator DiscoveryLoop()
    {
        while (rosConnection != null)
        {
            if (rosConnection.HasConnectionThread)
            {
                yield return new WaitForSecondsRealtime(RetryDelaySeconds);
                continue;
            }

            int port = rosConnection.RosPort;
            List<string> preferredIps = BuildPreferredCandidateList(rosConnection.RosIPAddress);

            Task<string> discoveryTask = DiscoverEndpointAsync(port, preferredIps);
            while (!discoveryTask.IsCompleted)
            {
                yield return null;
            }

            if (discoveryTask.IsFaulted)
            {
                Debug.LogWarning($"RosTcpAutoDiscovery: discovery failed: {discoveryTask.Exception?.GetBaseException().Message}", this);
                yield return new WaitForSecondsRealtime(RetryDelaySeconds);
                continue;
            }

            string discoveredIp = discoveryTask.Result;
            if (string.IsNullOrEmpty(discoveredIp))
            {
                Debug.LogWarning($"RosTcpAutoDiscovery: no ROS TCP server found on local IPv4 subnets for port {port}. Retrying in {RetryDelaySeconds:0}s.", this);
                yield return new WaitForSecondsRealtime(RetryDelaySeconds);
                continue;
            }

            if (!string.Equals(rosConnection.RosIPAddress, discoveredIp, StringComparison.Ordinal))
            {
                Debug.Log($"RosTcpAutoDiscovery: found ROS TCP server at {discoveredIp}:{port}.", this);
            }

            PlayerPrefs.SetString(LastSuccessfulIpKey, discoveredIp);
            PlayerPrefs.SetString(ROSConnection.PlayerPrefsKey_ROS_IP, discoveredIp);
            PlayerPrefs.SetInt(ROSConnection.PlayerPrefsKey_ROS_TCP_PORT, port);
            PlayerPrefs.Save();

            rosConnection.Disconnect();
            rosConnection.Connect(discoveredIp, port);

            yield return new WaitForSecondsRealtime(RetryDelaySeconds);
        }
    }

    private static async Task<string> DiscoverEndpointAsync(int port, IReadOnlyList<string> preferredIps)
    {
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            foreach (string preferredIp in preferredIps)
            {
                if (await CanConnectAsync(preferredIp, port, ConnectTimeoutMs, cts.Token))
                {
                    return preferredIp;
                }
            }

            List<string> subnetCandidates = EnumerateSubnetCandidates(preferredIps).ToList();
            if (subnetCandidates.Count == 0)
            {
                return null;
            }

            using (SemaphoreSlim gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency))
            {
                List<Task<string>> tasks = new List<Task<string>>(subnetCandidates.Count);

                foreach (string candidate in subnetCandidates)
                {
                    tasks.Add(CheckCandidateAsync(candidate, port, gate, cts.Token));
                }

                while (tasks.Count > 0)
                {
                    Task<string> finishedTask = await Task.WhenAny(tasks);
                    tasks.Remove(finishedTask);

                    string successfulIp = await finishedTask;
                    if (!string.IsNullOrEmpty(successfulIp))
                    {
                        cts.Cancel();
                        return successfulIp;
                    }
                }
            }

            return null;
        }
    }

    private static async Task<string> CheckCandidateAsync(string ip, int port, SemaphoreSlim gate, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            bool success = await CanConnectAsync(ip, port, ConnectTimeoutMs, token);
            return success ? ip : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<bool> CanConnectAsync(string ip, int port, int timeoutMs, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        using (TcpClient client = new TcpClient())
        {
            try
            {
                Task connectTask = client.ConnectAsync(ip, port);
                Task timeoutTask = Task.Delay(timeoutMs, token);
                Task completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask != connectTask)
                {
                    return false;
                }

                await connectTask;
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }

    private static List<string> BuildPreferredCandidateList(string configuredIp)
    {
        HashSet<string> ordered = new HashSet<string>();
        List<string> preferred = new List<string>();

        AddPreferredCandidate(configuredIp, ordered, preferred);
        AddPreferredCandidate(PlayerPrefs.GetString(ROSConnection.PlayerPrefsKey_ROS_IP, string.Empty), ordered, preferred);
        AddPreferredCandidate(PlayerPrefs.GetString(LastSuccessfulIpKey, string.Empty), ordered, preferred);

        return preferred;
    }

    private static void AddPreferredCandidate(string candidate, HashSet<string> ordered, List<string> preferred)
    {
        IPAddress parsedIp;
        if (!IPAddress.TryParse(candidate, out parsedIp)) return;
        if (parsedIp.AddressFamily != AddressFamily.InterNetwork) return;
        if (!ordered.Add(candidate)) return;
        preferred.Add(candidate);
    }

    private static IEnumerable<string> EnumerateSubnetCandidates(IEnumerable<string> excludedIps)
    {
        HashSet<string> excluded = new HashSet<string>(excludedIps.Where(ip => !string.IsNullOrWhiteSpace(ip)));
        HashSet<string> yielded = new HashSet<string>();

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation unicastAddress in properties.UnicastAddresses)
            {
                if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (unicastAddress.IPv4Mask == null) continue;

                foreach (string candidate in ExpandSubnet(unicastAddress.Address, unicastAddress.IPv4Mask))
                {
                    if (excluded.Contains(candidate)) continue;
                    if (!yielded.Add(candidate)) continue;
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> ExpandSubnet(IPAddress address, IPAddress mask)
    {
        uint ip = ToUInt32(address);
        uint subnetMask = ToUInt32(mask);
        uint network = ip & subnetMask;
        uint broadcast = network | ~subnetMask;

        ulong hostCount = broadcast > network ? (ulong)(broadcast - network - 1) : 0;

        uint start;
        uint end;

        if (hostCount == 0)
        {
            yield break;
        }

        if (hostCount > MaxHostsPerSubnet)
        {
            uint classCNetwork = ip & 0xFFFFFF00u;
            start = classCNetwork + 1u;
            end = classCNetwork + 254u;
        }
        else
        {
            start = network + 1u;
            end = broadcast - 1u;
        }

        for (uint candidate = start; candidate <= end; candidate++)
        {
            if (candidate == ip) continue;
            yield return FromUInt32(candidate).ToString();
        }
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static IPAddress FromUInt32(uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return new IPAddress(bytes);
    }
}
