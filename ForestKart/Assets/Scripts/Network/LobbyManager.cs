using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Unity.Services.Multiplayer;

public class LobbyManager : MonoBehaviour
{
    [Header("Relay")]
    // Maximum number of players including the host.
    public int maxPlayers = 4;

    // Last received join code after creating a Relay allocation.
    public string LastJoinCode { get; private set; } = "";

    // Event invoked to report status messages (UI can subscribe).
    public event Action<string> OnStatus;

    // Event invoked when a join code is generated (UI can display it).
    public event Action<string> OnJoinCode;

    // Event invoked once the host/client has started (used to trigger post-start logic).
    public event Action OnStarted;

    // Ensure Unity Gaming Services (UGS) are initialized and the user is signed in.
    // Uses anonymous sign-in if not already authenticated.
    async Task EnsureUGS()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    // Start hosting a Relay-backed session.
    // Allocates a Relay, obtains a join code, configures the UnityTransport and starts the host.
    public async void HostAsync()
    {
        try
        {
            OnStatus?.Invoke("Initializing services...");
            await EnsureUGS();

            // Create a Relay allocation for (maxPlayers - 1) clients (host is included in maxPlayers).
            OnStatus?.Invoke("Allocating Relay...");
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(0, maxPlayers - 1));

            // Retrieve a join code that clients can use to join this Relay allocation.
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            LastJoinCode = joinCode;
            OnJoinCode?.Invoke(joinCode);
            Debug.Log($"[NET] Join Code: {joinCode}");

            // Configure UnityTransport to use the Relay server data.
            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            var rsd = AllocationUtils.ToRelayServerData(alloc, "dtls");
            utp.SetRelayServerData(rsd);

            // If transport wasn't on the NetworkManager, try to find any UnityTransport in the scene.
            if (!utp) utp = FindAnyObjectByType<UnityTransport>();
            if (!utp) throw new Exception("UnityTransport not found on scene.");

            OnStatus?.Invoke("Starting Host...");
            NetworkManager.Singleton.StartHost();
            VoiceManager.Instance?.ConnectOrJoin();
            OnStarted?.Invoke();
            OnStatus?.Invoke("Host started. Click Start Race to begin.");
        }
        catch (Exception e)
        {
            // Report failure to UI and log exception for debugging.
            OnStatus?.Invoke("Host failed: " + e.Message);
            Debug.LogException(e);
        }
    }

    // Join an existing Relay session using a join code.
    // Joins the Relay allocation, configures the transport, and starts the client.
    public async void JoinAsync(string joinCode)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                OnStatus?.Invoke("Join code is empty.");
                return;
            }

            OnStatus?.Invoke("Initializing services...");
            await EnsureUGS();

            // Join the allocation using the provided join code.
            OnStatus?.Invoke("Joining Relay");
            JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim());

            // Configure the transport with the joined Relay data.
            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            var rsd = AllocationUtils.ToRelayServerData(join, "dtls");
            utp.SetRelayServerData(rsd);

            // Fallback to finding any UnityTransport in the scene if not set on NetworkManager.
            if (!utp) utp = FindAnyObjectByType<UnityTransport>();
            if (!utp) throw new Exception("UnityTransport not found on scene.");

            // Start the client and connect voice (if available), then notify listeners.
            OnStatus?.Invoke("Starting Client");
            NetworkManager.Singleton.StartClient();
            VoiceManager.Instance?.ConnectOrJoin();
            OnStarted?.Invoke();
            OnStatus?.Invoke("Client started.");
        }
        catch (Exception e)
        {
            // Report failure to UI and log for debugging.
            OnStatus?.Invoke("Join failed: " + e.Message);
            Debug.LogException(e);
        }
    }

    // Gracefully shutdown the NetworkManager if it's currently listening.
    public void Shutdown()
    {
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            OnStatus?.Invoke("Network shutdown.");
        }
    }
}
