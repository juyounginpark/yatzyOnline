using UnityEngine;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("─ Audio Sources ─")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource clockSource;  // 시계 소리 전용 (루프)

    [Header("─ UI & Base SFX ─")]
    public AudioClip uiClick;
    public AudioClip cardHover;
    public AudioClip cardDraw;
    public AudioClip cardPlace;
    public AudioClip cardFlip;
    public AudioClip turnEnd;
    public AudioClip clockTick;
    public AudioClip rouletteSpin;
    public AudioClip rouletteSelect;

    [Header("─ Combat SFX ─")]
    public AudioClip cameraPan;
    public AudioClip tensionShake;
    public AudioClip gatherPower;
    public AudioClip combatHit;
    public AudioClip combatBlock;
    public AudioClip shatter;
    public AudioClip damageTaken;

    [Header("─ Outcome SFX ─")]
    public AudioClip heal;
    public AudioClip victory;
    public AudioClip defeat;

    [Header("─ Background Music ─")]
    public AudioClip mainBGM;
    public AudioClip combatBGM;
    [Range(0f, 1f)] public float bgmVolume = 0.5f;
    [Tooltip("시작 시 mainBGM 자동 재생")]
    public bool playMainBgmOnStart = true;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeSources();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (Instance != this) return;  // 중복 인스턴스는 무시
        if (playMainBgmOnStart && mainBGM != null)
            PlayBGM(mainBGM, bgmVolume);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void InitializeSources()
    {
        if (bgmSource == null)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
        }

        if (clockSource == null)
        {
            clockSource = gameObject.AddComponent<AudioSource>();
            clockSource.loop = true;
            clockSource.playOnAwake = false;
        }
    }

    public void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, volume);
    }

    // 시계 소리 루프 시작 (내 턴 동안). 이미 재생 중이면 무시
    public void StartClock(float volume = 1f)
    {
        if (clockTick == null || clockSource == null) return;
        if (clockSource.isPlaying && clockSource.clip == clockTick) return;
        clockSource.clip = clockTick;
        clockSource.volume = volume;
        clockSource.loop = true;
        clockSource.Play();
    }

    // 시계 소리 정지 (턴 종료 / 상대 턴)
    public void StopClock()
    {
        if (clockSource != null && clockSource.isPlaying)
            clockSource.Stop();
    }

    public void PlayBGM(AudioClip clip, float volume = 0.5f)
    {
        if (clip == null || bgmSource == null || bgmSource.clip == clip) return;
        bgmSource.clip = clip;
        bgmSource.volume = volume;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource.isPlaying)
            bgmSource.Stop();
    }

    public void SetBGMVolume(float volume)
    {
        if (bgmSource != null)
            bgmSource.volume = volume;
    }

    public void SetSFXVolume(float volume)
    {
        if (sfxSource != null)
            sfxSource.volume = volume;
    }
}
