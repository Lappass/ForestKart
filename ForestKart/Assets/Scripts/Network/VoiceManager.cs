using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;

/// <summary>
/// Manages Vivox voice chat lifecycle:
/// - Initializes Unity Gaming Services and Vivox
/// - Logs in (anonymous)
/// - Joins a group channel
/// - Provides simple mute controls
/// This component is a persistent singleton.
/// </summary>
public class VoiceManager : MonoBehaviour
{
    // Name of the group channel to join by default
    [SerializeField] private string channelName = "TestRoom";
    public string ChannelName => channelName;
    private bool isIn3DChannel = false;
    public bool IsIn3DChannel => isIn3DChannel;

    private string _localVivoxAccountId = string.Empty;
    
    // Singleton instance (accessible globally)
    public static VoiceManager Instance { get; private set; }

    void Awake()
    {
        // Standard Unity singleton pattern:
        // If an instance already exists, destroy the duplicate.
        if (Instance != null) { Destroy(gameObject); return; }

        Instance = this;
        // Keep this object alive across scene loads
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Public entry point to connect, login and join the default group channel.
    /// Uses async calls and logs errors to the Unity console.
    /// Note: async void is used here because this is an external entry method
    /// (e.g. called from UI). Prefer async Task for internal call chains.
    /// </summary>
    public async void ConnectOrJoin()
    {
        Debug.Log("[Vivox] ConnectOrJoin started for channel: " + channelName);
        try
        {
            // Ensure Unity Services are initialized and we are authenticated
            await EnsureUgsAsync();

            // Ensure Vivox SDK is initialized
            await EnsureVivoxInitializedAsync();

            // Ensure we're logged into Vivox (will login if necessary)
            await EnsureVivoxLoggedInAsync();

            // Join the configured group channel (audio only)
            await JoinGroupChannelAsync(channelName);

            Debug.Log("[Vivox] Ready & Joined: " + channelName);
        }
        catch (Exception e)
        {
            // Surface any unexpected errors to the Unity console
            Debug.LogError("[Vivox] ERROR: " + e);
        }
    }

    /// <summary>
    /// Initialize Unity Services if needed and sign in anonymously if not signed in.
    /// </summary>
    async Task EnsureUgsAsync()
    {
        // Initialize Unity Services only if they're not already initialized
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            print(1);
            await UnityServices.InitializeAsync();
        }

        // Sign in anonymously if we don't have an authenticated user yet
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            print(2);
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    /// <summary>
    /// Initialize the Vivox service (SDK). This prepares Vivox for login/join operations.
    /// </summary>
    async Task EnsureVivoxInitializedAsync()
    {
        await VivoxService.Instance.InitializeAsync();
    }

    /// <summary>
    /// Log into Vivox if not already logged in.
    /// A random display name is generated for the session.
    /// </summary>
    async Task EnsureVivoxLoggedInAsync()
    {
        if (!VivoxService.Instance.IsLoggedIn)
        {
            var opts = new LoginOptions { DisplayName = "P" + UnityEngine.Random.Range(1000, 9999) };
            await VivoxService.Instance.LoginAsync(opts);
        }
    }

    /// <summary>
    /// Join a Vivox group channel with audio-only chat capability.
    /// </summary>
    async Task JoinGroupChannelAsync(string thisChannelName)
    {
        var options = new ChannelOptions { MakeActiveChannelUponJoining = true };
        //await VivoxService.Instance.JoinGroupChannelAsync(thisChannelName, ChatCapability.AudioOnly, options);
        await VivoxService.Instance.JoinPositionalChannelAsync(thisChannelName, ChatCapability.AudioOnly, new Channel3DProperties(32, 1, 1.0f, AudioFadeModel.InverseByDistance), options);
        isIn3DChannel = true;
    }

    /// <summary>
    /// Mute or unmute the output (speakers).
    /// </summary>
    public void SetOutputMuted(bool muted)
    {
        if (muted) VivoxService.Instance.MuteOutputDevice();
        else VivoxService.Instance.UnmuteOutputDevice();
    }

    /// <summary>
    /// Mute or unmute the input (microphone).
    /// </summary>
    public void SetInputMuted(bool muted)
    {
        if (muted) VivoxService.Instance.MuteInputDevice();
        else VivoxService.Instance.UnmuteInputDevice();
    }

    /// <summary>
    /// When this component is disabled/destroyed, attempt to leave all channels.
    /// Silent-catch any exceptions to avoid noisy shutdown errors.
    /// </summary>
    private async void OnDisable()
    {
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); } catch { }
    }
    
    /// <summary>
    /// Returns the local Vivox account id if available.
    /// Tries cached value, then attempts to reflectively read common properties from VivoxService,
    /// then falls back to AuthenticationService.PlayerId, otherwise returns empty string.
    /// </summary>
    public string GetLocalVivoxAccountId()
    {
        if (!string.IsNullOrEmpty(_localVivoxAccountId)) return _localVivoxAccountId;

        try
        {
            var vs = VivoxService.Instance;
            if (vs != null)
            {
                var t = vs.GetType();

                // Try common session-like property names
                var sessionProp = t.GetProperty("LoginSession") ?? t.GetProperty("Session") ?? t.GetProperty("CurrentSession");
                if (sessionProp != null)
                {
                    var session = sessionProp.GetValue(vs);
                    if (session != null)
                    {
                        var idProp = session.GetType().GetProperty("AccountId") ?? session.GetType().GetProperty("Id") ?? session.GetType().GetProperty("Name");
                        if (idProp != null)
                        {
                            var idVal = idProp.GetValue(session)?.ToString();
                            if (!string.IsNullOrEmpty(idVal))
                            {
                                _localVivoxAccountId = idVal;
                                return _localVivoxAccountId;
                            }
                        }
                    }
                }

                // Try direct properties on VivoxService
                var directProp = t.GetProperty("LocalUser") ?? t.GetProperty("LoggedInUser") ?? t.GetProperty("AccountId");
                if (directProp != null)
                {
                    var val = directProp.GetValue(vs)?.ToString();
                    if (!string.IsNullOrEmpty(val))
                    {
                        _localVivoxAccountId = val;
                        return _localVivoxAccountId;
                    }
                }
            }
        }
        catch { }

        try
        {
            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                return AuthenticationService.Instance.PlayerId;
            }
        }
        catch { }

        return string.Empty;
    } 
}