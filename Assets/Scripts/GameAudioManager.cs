using UnityEngine;

public sealed class GameAudioManager : MonoBehaviour
{
    private const string AudioEnabledKey = "AudioEnabled";

    [Header("Music")]
    [SerializeField] private AudioClip mainMenuMusic;
    [SerializeField] private AudioClip gameplayMusic;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.6f;

    [Header("Sound Effects")]
    [SerializeField] private AudioClip buttonSound;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip carCrashSound;
    [SerializeField] private AudioClip waterFallSound;
    [SerializeField] private AudioClip gameOverSound;
    [SerializeField] private AudioClip trainPassSound;
    [SerializeField, Range(0f, 1f)] private float soundEffectVolume = 1f;

    private AudioSource musicSource;
    private AudioSource soundEffectSource;
    private bool trainPassSoundEnabled;

    public static GameAudioManager Instance { get; private set; }
    public bool IsAudioEnabled { get; private set; } = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = CreateAudioSource(loop: true, musicVolume);
        soundEffectSource = CreateAudioSource(loop: false, soundEffectVolume);

        IsAudioEnabled = PlayerPrefs.GetInt(AudioEnabledKey, 1) != 0;
        ApplyAudioEnabled();
    }

    public void SetAudioEnabled(bool isEnabled)
    {
        IsAudioEnabled = isEnabled;
        ApplyAudioEnabled();

        PlayerPrefs.SetInt(AudioEnabledKey, isEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void PlayMainMenuMusic()
    {
        PlayMusic(mainMenuMusic);
    }

    public void PlayGameplayMusic()
    {
        PlayMusic(gameplayMusic);
    }

    public void StopMusic()
    {
        musicSource?.Stop();
    }

    public void PauseMusic()
    {
        musicSource?.Pause();
    }

    public void ResumeMusic()
    {
        musicSource?.UnPause();
    }

    public void PlayButtonSound()
    {
        PlaySoundEffect(buttonSound);
    }

    public void PlayJumpSound()
    {
        PlaySoundEffect(jumpSound);
    }

    public void PlayTrainPassSound()
    {
        if (!trainPassSoundEnabled)
        {
            return;
        }

        PlaySoundEffect(trainPassSound);
    }

    public void SetTrainPassSoundEnabled(bool isEnabled)
    {
        trainPassSoundEnabled = isEnabled;
    }

    public void PlayDeathSounds(PlayerDeathCause cause)
    {
        PlaySoundEffect(
            cause == PlayerDeathCause.Water
                ? waterFallSound
                : carCrashSound);
        PlaySoundEffect(gameOverSound);
    }

    private AudioSource CreateAudioSource(bool loop, float volume)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.volume = volume;
        source.spatialBlend = 0f;
        return source;
    }

    private void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null)
        {
            return;
        }

        if (musicSource.clip == clip && musicSource.isPlaying)
        {
            return;
        }

        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.Play();
    }

    private void PlaySoundEffect(AudioClip clip)
    {
        if (soundEffectSource != null && clip != null)
        {
            soundEffectSource.PlayOneShot(clip);
        }
    }

    private void ApplyAudioEnabled()
    {
        AudioListener.volume = IsAudioEnabled ? 1f : 0f;
    }
}
