using RaftModLoader;
using UnityEngine;
using System.Collections;
using System.IO;
using UnityEngine.Networking;
using System.Collections.Generic;

public class MusicMod : Mod
{
    private AudioSource currentAudioSource;
    private AudioSource nextAudioSource;
    private bool isJourneyMusicPlaying = false;
    private bool isExplorationMusicPlaying = false;

    // File paths
    private string journeyMusicFolder = @"Mods/MusicMod/JourneyMusic";
    private string explorationMusicFolder = @"Mods/MusicMod/ExplorationMusic";

    // Volume control
    private float musicVolume = 0.5f;
    private const string VOLUME_PREF_KEY = "MusicModVolume";
    private const float FADE_DURATION = 2.0f;

    // References
    private GameObject raftObject;
    private const float RAFT_DISTANCE_THRESHOLD = 35f;
    private const float EXPLORATION_TRANSITION_DELAY = 20f;
    private const float JOURNEY_TRANSITION_DELAY = 20f;
    private const bool ENABLE_DETAILED_LOGGING = false; // Set to true only when debugging
    private const float LOG_INTERVAL = 15f; // Log every 15 seconds instead of 5

    // Track management
    private string currentMusicContext = "";
    private Coroutine musicPlaybackCoroutine;
    private float offRaftTimer = 0f;
    private float onRaftTimer = 0f;

    public void Start()
    {
        Debug.Log("[MusicMod] Started!");
    
        musicVolume = PlayerPrefs.GetFloat(VOLUME_PREF_KEY, 0.5f);
    
        // Create two audio sources for crossfading
        currentAudioSource = gameObject.AddComponent<AudioSource>();
        nextAudioSource = gameObject.AddComponent<AudioSource>();
        
        SetupAudioSource(currentAudioSource);
        SetupAudioSource(nextAudioSource);
    
        FindRaftObject();
        StartCoroutine(TrackPlayerState());
    }

    private void SetupAudioSource(AudioSource source)
    {
        source.loop = false;
        source.playOnAwake = false;
        source.volume = 0f;
        source.spatialBlend = 0f; // Ensure the audio is not spatial
    }

    private void FindRaftObject()
    {
        raftObject = GameObject.Find("Raft 2");
    
        if (raftObject == null)
        {
            Debug.Log("[MusicMod] Searching for alternative raft object...");
            GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (var obj in allObjects)
            {
                if (obj.name.Contains("Raft"))
                {
                    raftObject = obj;
                    Debug.Log($"[MusicMod] Found raft object: {obj.name}");
                    break;
                }
            }
        }
    }

    private IEnumerator TrackPlayerState()
    {
        while (true)
        {
            GameObject playerObject = GameObject.FindWithTag("Player");

            if (playerObject != null && raftObject != null)
            {
                float distanceFromRaft = Vector3.Distance(playerObject.transform.position, raftObject.transform.position);

                if (ENABLE_DETAILED_LOGGING)
                {
                    Debug.Log($"[MusicMod] Distance from raft: {distanceFromRaft:F1} units");
                }

                if (distanceFromRaft >= RAFT_DISTANCE_THRESHOLD)
                {
                    offRaftTimer += 5f;
                    onRaftTimer = 0f;

                    if (offRaftTimer >= EXPLORATION_TRANSITION_DELAY && currentMusicContext != "Exploration")
                    {
                        Debug.Log("[MusicMod] Player far from raft - Transitioning to Exploration music");
                        SwitchMusicContext("Exploration", explorationMusicFolder);
                    }
                }
                else
                {
                    onRaftTimer += 5f;
                    offRaftTimer = 0f;

                    if (onRaftTimer >= JOURNEY_TRANSITION_DELAY && currentMusicContext != "Journey")
                    {
                        Debug.Log("[MusicMod] Player near raft - Transitioning to Journey music");
                        SwitchMusicContext("Journey", journeyMusicFolder);
                    }
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private void SwitchMusicContext(string context, string musicFolder)
    {
        if (currentMusicContext == context)
            return;

        Debug.Log($"[MusicMod] Switching music context to: {context}");

        // Stop all existing playback coroutines
        if (musicPlaybackCoroutine != null)
        {
            StopCoroutine(musicPlaybackCoroutine);
            musicPlaybackCoroutine = null;
        }

        StartCoroutine(SwitchContextAfterFade(context, musicFolder));
    }

    private IEnumerator SwitchContextAfterFade(string context, string musicFolder)
    {
        // First, stop the current context flags to prevent new tracks from starting
        isJourneyMusicPlaying = false;
        isExplorationMusicPlaying = false;

        Debug.Log("[MusicMod] Stopping current context before switching");

        // If there's a track playing, fade it out
        if (currentAudioSource != null && currentAudioSource.isPlaying)
        {
            Debug.Log($"[MusicMod] Fading out current track: {(currentAudioSource.clip != null ? currentAudioSource.clip.name : "unknown")}");
            yield return StartCoroutine(FadeOut(currentAudioSource));
            currentAudioSource.Stop();
            currentAudioSource.clip = null;
        }

        // Wait a brief moment to ensure clean transition
        yield return new WaitForSeconds(0.5f);

        // Update context and flags
        currentMusicContext = context;
        isJourneyMusicPlaying = (context == "Journey");
        isExplorationMusicPlaying = (context == "Exploration");

        Debug.Log($"[MusicMod] Context switched to: {context}, starting new playback");

        // Start new playback
        if (musicPlaybackCoroutine != null)
        {
            StopCoroutine(musicPlaybackCoroutine);
        }
        musicPlaybackCoroutine = StartCoroutine(ContinuousTrackPlayback(musicFolder));
    }
    private void StopMusicPlayback()
    {
        Debug.Log("[MusicMod] Stopping all music playback");

        // Stop the context flags first
        isJourneyMusicPlaying = false;
        isExplorationMusicPlaying = false;

        // Stop the playback coroutine
        if (musicPlaybackCoroutine != null)
        {
            StopCoroutine(musicPlaybackCoroutine);
            musicPlaybackCoroutine = null;
        }

        // Fade out any playing audio
        if (currentAudioSource != null && currentAudioSource.isPlaying)
        {
            StartCoroutine(FadeOut(currentAudioSource));
        }
    }

    private IEnumerator ContinuousTrackPlayback(string folderPath)
    {
        List<string> playedTracks = new List<string>();
        
        while (isJourneyMusicPlaying || isExplorationMusicPlaying)
        {
            string trackToPlay = GetRandomTrack(folderPath);
            if (string.IsNullOrEmpty(trackToPlay))
            {
                Debug.LogWarning("[MusicMod] No track available to play");
                yield return new WaitForSeconds(1f);
                continue;
            }
    
            // Keep track of played songs to avoid immediate repeats
            if (!playedTracks.Contains(trackToPlay))
            {
                playedTracks.Add(trackToPlay);
                if (playedTracks.Count > 3) // Keep only last 3 tracks in memory
                {
                    playedTracks.RemoveAt(0);
                }
                
                Debug.Log($"[MusicMod] Starting new track playback: {Path.GetFileName(trackToPlay)}");
                yield return StartCoroutine(PlayTrackWithFadeIn(trackToPlay));
                
                // Add a small delay between tracks
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Debug.Log($"[MusicMod] Skipping recently played track: {Path.GetFileName(trackToPlay)}");
                yield return new WaitForSeconds(0.1f);
            }
        }
    }

    private IEnumerator PlayTrackWithFadeIn(string filePath)
    {
        Debug.Log($"[MusicMod] Loading track: {Path.GetFileName(filePath)}");

        // Load the audio clip and ensure it's ready
        yield return StartCoroutine(LoadAudioClip(filePath, currentAudioSource));

        // Verify clip loaded successfully
        if (currentAudioSource.clip == null)
        {
            Debug.LogError($"[MusicMod] Failed to load clip: {Path.GetFileName(filePath)}");
            yield break;
        }

        // Wait until the audio clip is fully loaded
        float loadingTimeout = 5f; // 5 seconds timeout
        float loadingStartTime = Time.time;

        while (currentAudioSource.clip.loadState != AudioDataLoadState.Loaded)
        {
            if (Time.time - loadingStartTime > loadingTimeout)
            {
                Debug.LogError($"[MusicMod] Timeout while loading clip: {Path.GetFileName(filePath)}");
                yield break;
            }
            yield return new WaitForSeconds(0.1f);
        }

        // Verify clip length
        if (currentAudioSource.clip.length <= 0f)
        {
            Debug.LogError($"[MusicMod] Invalid clip length: {Path.GetFileName(filePath)}");
            yield break;
        }

        // Store important values before playback
        float clipLength = currentAudioSource.clip.length;
        string clipName = Path.GetFileName(filePath);

        // Fade in and play the track
        currentAudioSource.time = 0f; // Ensure we start from the beginning
        currentAudioSource.volume = 0f;
        currentAudioSource.Play();

        Debug.Log($"[MusicMod] Starting playback of {clipName} (Length: {clipLength}s)");
        yield return StartCoroutine(FadeIn(currentAudioSource));

        // Main playback monitoring loop
        float playbackStartTime = Time.time;
        float lastPlaybackPosition = 0f;
        int stuckCounter = 0;

    while (currentAudioSource.isPlaying)
    {
        float currentPlaybackPosition = currentAudioSource.time;
        float totalPlayedTime = Time.time - playbackStartTime;

        // Only log if detailed logging is enabled and at specified intervals
        if (ENABLE_DETAILED_LOGGING && Mathf.Floor(totalPlayedTime) % LOG_INTERVAL == 0)
        {
            Debug.Log($"[MusicMod] Playback status for {clipName}: Position: {currentPlaybackPosition:F1}s / {clipLength:F1}s");
        }

        // Check if playback is stuck
        if (Mathf.Approximately(currentPlaybackPosition, lastPlaybackPosition))
        {
            stuckCounter++;
            if (stuckCounter > 50) // 5 seconds of being stuck (checking every 0.1s)
            {
                Debug.LogWarning($"[MusicMod] Playback appears stuck for {clipName}, restarting track");
                break;
            }
        }
        else
        {
            stuckCounter = 0;
        }

        // Check if we're near the end of the track
        if (currentPlaybackPosition >= clipLength - FADE_DURATION)
        {
            if (ENABLE_DETAILED_LOGGING)
            {
                Debug.Log($"[MusicMod] Track {clipName} reaching end, starting fade out");
            }
            break;
        }

        // Check for invalid playback position
        if (currentPlaybackPosition > clipLength)
        {
            Debug.LogError($"[MusicMod] Invalid playback position detected for {clipName}");
            break;
        }

        lastPlaybackPosition = currentPlaybackPosition;
        yield return new WaitForSeconds(0.1f);
    }

        // Always ensure we fade out properly
        Debug.Log($"[MusicMod] Starting fade out for {clipName}");
        yield return StartCoroutine(FadeOut(currentAudioSource));

        // Cleanup
        currentAudioSource.Stop();
        currentAudioSource.clip = null;
        Resources.UnloadUnusedAssets();

        Debug.Log($"[MusicMod] Finished playing {clipName}");
    }

    private string GetRandomTrack(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Debug.LogError($"[MusicMod] Folder not found: {folderPath}");
            return null;
        }

        var files = Directory.GetFiles(folderPath, "*.wav");
        if (files.Length == 0)
        {
            Debug.LogWarning($"[MusicMod] No WAV files found in {folderPath}");
            return null;
        }

        string selectedTrack = files[Random.Range(0, files.Length)];
        Debug.Log($"[MusicMod] Selected track: {Path.GetFileName(selectedTrack)}");
        return selectedTrack;
    }

    private IEnumerator LoadAudioClip(string filePath, AudioSource targetSource)
    {
        string absolutePath = Path.GetFullPath(filePath);
        string fileURL = "file:///" + absolutePath.Replace("\\", "/");
        
        Debug.Log($"[MusicMod] Loading audio from: {Path.GetFileName(filePath)}");
        
        UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(fileURL, AudioType.WAV);
        yield return request.SendWebRequest();
    
        if (request.error != null)
        {
            Debug.LogError($"[MusicMod] Error loading audio: {request.error}");
            yield break;
        }
    
        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null)
        {
            Debug.LogError("[MusicMod] Failed to load audio clip");
            yield break;
        }

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            Debug.LogError($"[MusicMod] Clip failed to load properly: {Path.GetFileName(filePath)}");
            yield break;
        }
    
        targetSource.clip = clip;
        Debug.Log($"[MusicMod] Successfully loaded {Path.GetFileName(filePath)}");
    }

    private IEnumerator FadeIn(AudioSource source)
    {
        float startTime = Time.time;
        source.volume = 0f;
        
        while (Time.time - startTime < FADE_DURATION)
        {
            float elapsed = Time.time - startTime;
            source.volume = Mathf.Lerp(0f, musicVolume, elapsed / FADE_DURATION);
            yield return null;
        }
        
        source.volume = musicVolume;
        Debug.Log("[MusicMod] Fade in complete");
    }

    private IEnumerator FadeOut(AudioSource source)
    {
        float startTime = Time.time;
        float startVolume = source.volume;
        
        while (Time.time - startTime < FADE_DURATION)
        {
            float elapsed = Time.time - startTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / FADE_DURATION);
            yield return null;
        }
        
        source.volume = 0f;
        Debug.Log("[MusicMod] Fade out complete");
    }

private bool isMenuOpen = false; // A flag to toggle the menu open/closed
private Rect menuRect = new Rect(0f, 0f, 400f, 400f); // Initial size of the menu
private float menuWidth = 400f;
private float menuHeight = 400f;

public void OnGUI()
{
    // Colors for the Jager Bros theme
    Color purple = new Color(0.4f, 0.1f, 0.6f); // Deep purple
    Color pink = new Color(0.9f, 0.1f, 0.5f);   // Vibrant pink
    Color backgroundColor = new Color(0.15f, 0.05f, 0.2f); // Dark purple for the menu
    Color textColor = Color.white;

    // Gradient background for buttons
    Texture2D buttonBackground = CreateGradientTexture(purple, pink);

    // Styling for buttons
    GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
    {
        fontSize = 16,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = textColor, background = buttonBackground },
        hover = { textColor = textColor, background = buttonBackground },
        active = { textColor = Color.gray, background = buttonBackground }
    };

    // Styling for the menu box
    GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
    {
        fontSize = 18,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        padding = new RectOffset(20, 20, 20, 20),
        normal = { background = MakeBackgroundTexture(backgroundColor), textColor = textColor }
    };

    // Styling for labels
    GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
    {
        fontSize = 16,
        fontStyle = FontStyle.Bold,
        normal = { textColor = textColor }
    };

    // Open/Close Button to toggle the menu
    if (GUI.Button(new Rect(Screen.width - 120, 10, 100, 40), isMenuOpen ? "Close Menu" : "Open Menu", buttonStyle))
    {
        isMenuOpen = !isMenuOpen; // Toggle the menu open/closed
    }

    // If the menu is open, render the menu content
    if (isMenuOpen)
    {
        // Define the menu position on the right side of the screen
        menuRect.x = Screen.width - menuWidth - 10f; // Adjust the position to the right edge
        menuRect.y = (Screen.height - menuHeight) / 2f; // Center the menu vertically

        // Background for the menu (to make it stand out)
        GUI.Box(menuRect, "Music Mod Settings", boxStyle);

        // Inside the menu
        GUILayout.BeginArea(menuRect);

        GUILayout.Space(40); // Add space to avoid overlap
        // Remove the redundant declaration of newVolume here
        GUILayout.Label($"Volume: {musicVolume * 100:F0}%", labelStyle);
        GUILayout.Space(10); // Add some space before the slider
        float newVolume = GUILayout.HorizontalSlider(musicVolume, 0f, 1f, GUILayout.Width(360));

        if (newVolume != musicVolume)
        {
            musicVolume = newVolume;
            PlayerPrefs.SetFloat(VOLUME_PREF_KEY, musicVolume);
            PlayerPrefs.Save();

            if (currentAudioSource != null && currentAudioSource.isPlaying)
            {
                currentAudioSource.volume = musicVolume;
            }
            if (nextAudioSource != null && nextAudioSource.isPlaying)
            {
                nextAudioSource.volume = musicVolume;
            }

            Debug.Log($"[MusicMod] Volume changed to: {musicVolume}");
        }

        GUILayout.Space(20);

        // Add the clickable links
        if (GUILayout.Button("YOUTUBE", buttonStyle, GUILayout.Height(40)))
        {
            Application.OpenURL("https://youtube.com/@JagerBros?sub_confirmation=1");
        }

        GUILayout.Space(10);
        if (GUILayout.Button("SHOP", buttonStyle, GUILayout.Height(40)))
        {
            Application.OpenURL("https://shop.jagerbros.com");
        }

        GUILayout.Space(10);
        if (GUILayout.Button("WEBSITE", buttonStyle, GUILayout.Height(40)))
        {
            Application.OpenURL("https://jagerbros.com");
        }

        GUILayout.EndArea();
    }
}


// Utility method to create a solid color background texture
private Texture2D MakeBackgroundTexture(Color color)
{
    Texture2D texture = new Texture2D(1, 1);
    texture.SetPixel(0, 0, color);
    texture.Apply();
    return texture;
}

// Utility method to create a gradient texture
private Texture2D CreateGradientTexture(Color color1, Color color2)
{
    Texture2D texture = new Texture2D(1, 2);
    texture.SetPixel(0, 0, color1);
    texture.SetPixel(0, 1, color2);
    texture.Apply();
    return texture;
}




}