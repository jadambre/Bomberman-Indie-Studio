using System;
using System.Collections.Generic;
using UnityEngine;

namespace BombermanIndieStudio
{
    public sealed class GameAudio : MonoBehaviour
    {
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly AudioSource[] music = new AudioSource[2];
        readonly AudioSource[] voices = new AudioSource[16];
        int activeMusic, voice;
        float musicVolume = .45f, sfxVolume = .8f, duck = 1;
        string musicKey;
        AudioClip stepClip, tickClip;
        readonly string[] stages = { "music_moss", "music_sun", "music_frost", "music_neon", "music_core" };

        void Awake()
        {
            for(int i=0;i<2;i++) { music[i] = gameObject.AddComponent<AudioSource>(); music[i].loop = true; music[i].volume=0; music[i].priority=32; }
            for(int i=0;i<voices.Length;i++) { voices[i]=gameObject.AddComponent<AudioSource>(); voices[i].playOnAwake=false; voices[i].priority=100; }
            stepClip = MakeTone("Footstep", .07f, false);
            tickClip = MakeTone("Fuse warning", .08f, true);
        }

        AudioClip Clip(string key)
        {
            if(!clips.TryGetValue(key,out var clip)) { clip=Resources.Load<AudioClip>("Audio/"+key); clips[key]=clip; }
            return clip;
        }
        public void SetVolumes(float musicLevel,float effectsLevel) { musicVolume=musicLevel; sfxVolume=effectsLevel; }
        public void SetPaused(bool paused) { duck=paused ? .4f : 1; }
        public void PlayMusic(int stage = -1)
        {
            var key = stage < 0 ? "music_menu" : stages[Mathf.Clamp(stage,0,4)];
            if(key == musicKey) return;
            musicKey=key; activeMusic=1-activeMusic;
            music[activeMusic].clip=Clip(key); music[activeMusic].volume=0;
            if(music[activeMusic].clip) music[activeMusic].Play();
        }
        void Update()
        {
            for(int i=0;i<2;i++)
            {
                music[i].volume=Mathf.MoveTowards(music[i].volume,i==activeMusic ? musicVolume*.62f*duck : 0,Time.unscaledDeltaTime*.35f);
                if(i!=activeMusic && music[i].volume==0 && music[i].isPlaying) music[i].Stop();
            }
        }
        void Play(AudioClip clip,float volume=1,float pitch=1,float pan=0)
        {
            if(!clip || sfxVolume<=0) return;
            var source=voices[voice++%voices.Length];
            source.Stop(); source.clip=clip; source.volume=volume*sfxVolume; source.pitch=pitch; source.panStereo=pan; source.Play();
        }
        public void UI(bool confirm) => Play(Clip(confirm?"confirm":"cursor"),confirm?.42f:.20f);
        public void Cue(string key,float level=.65f) => Play(Clip(key),level);
        public void Warning() => Play(tickClip,.15f);
        public void Events(IReadOnlyList<GameEvent> events,SessionState state)
        {
            int explosions=0;
            foreach(var e in events)
            {
                float pan=(e.X/(float)(state.Width-1)-.5f)*.65f;
                switch(e.Kind)
                {
                    case GameEventKind.BombPlaced: Play(Clip("place"),.58f,1,pan); break;
                    case GameEventKind.Exploded: if(explosions++<3) Play(Clip("explode"),.65f/(1+explosions*.25f),.94f+(e.X%3)*.04f,pan); break;
                    case GameEventKind.PickupTaken: Play(Clip("pickup"),.7f,1,pan); break;
                    case GameEventKind.ActorDied: Play(Clip(e.ActorId < state.PlayerCount ? "player_down":"bot_down"),.7f,1,pan); break;
                    case GameEventKind.Footstep:
                        var a=state.Actors.Find(x=>x.Id==e.ActorId);
                        if(a!=null && a.HumanIndex>=0) Play(stepClip,.18f,1+(e.ActorId%2)*.16f,pan);
                        break;
                }
            }
        }
        static AudioClip MakeTone(string name,float length,bool tonal)
        {
            int count=Mathf.RoundToInt(22050*length);
            var data=new float[count]; var rng=new System.Random(71);
            for(int i=0;i<count;i++)
            {
                float t=i/22050f, envelope=Mathf.Pow(1-i/(float)count,3);
                data[i]=envelope*(tonal ? Mathf.Sin(t*850*2*Mathf.PI)*.22f : ((float)rng.NextDouble()-.5f)*.20f+Mathf.Sin(t*145*2*Mathf.PI)*.22f);
            }
            var clip=AudioClip.Create(name,count,1,22050,false); clip.SetData(data,0); return clip;
        }
        void OnDestroy() { if(stepClip) Destroy(stepClip); if(tickClip) Destroy(tickClip); }
    }
}
