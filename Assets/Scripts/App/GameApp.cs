using System;
using UnityEngine;

namespace BombermanIndieStudio
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "EmberGrid", "EmberGrid.Runtime", "GameApp")]
    [DefaultExecutionOrder(-100)]
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance {get; private set;}
        public Simulation Sim {get; private set;}
        public GameScreen Screen {get; private set;} = GameScreen.MainMenu;
        public bool HasSave => cachedSave != null && !(cachedSave.Session.Stage==4 && cachedSave.Session.Outcome==MatchOutcome.Won);
        public string SaveSummary => !HasSave ? GameText.T("No saved campaign") : GameText.Format("STAGE {0:00} / 05  ·  {1}  ·  {2}", cachedSave.Session.Stage+1, GameText.StageName(cachedSave.Session.Stage), GameText.T(cachedSave.Session.PlayerCount==2 ? "CO-OP" : "SOLO"));
        public string LastNotice => GameText.T(noticeSource);
        public GameLanguage Language => GameText.Current;
        string noticeSource;
        public float MusicVolume {get; private set;}
        public float SfxVolume {get; private set;}
        public bool ShakeEnabled {get; private set;}
        public float IntroRemaining {get; private set;}
        public ArenaView View {get; private set;}
        public GameUI UI {get; private set;}
        SessionState checkpoint;
        CampaignSave cachedSave;
        GameAudio audioSystem;
        float accumulator, outcomeDelay, noticeTimer, warningTimer;
        bool bomb1, bomb2;
        readonly PlayerCommand[] commands=new PlayerCommand[2];
        const float Step=1f/60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if(FindFirstObjectByType<GameApp>()==null) new GameObject("Bomberman Indie Studio").AddComponent<GameApp>();
        }
        void Awake()
        {
            if(Instance && Instance!=this) { Destroy(gameObject); return; }
            Instance=this;
            GameText.LoadPreference();
            Application.targetFrameRate=120;
            Application.runInBackground=false;
            MusicVolume=PlayerPrefs.GetFloat("music",.42f);
            SfxVolume=PlayerPrefs.GetFloat("sfx",.8f);
            ShakeEnabled=PlayerPrefs.GetInt("shake",1)==1;
            audioSystem=gameObject.AddComponent<GameAudio>(); audioSystem.SetVolumes(MusicVolume,SfxVolume);
            cachedSave=SaveRepository.Read(out var notice); Notice(notice);
            Sim=Simulation.CreateCampaign(2,812764);
            View=gameObject.AddComponent<ArenaView>(); View.ShakeEnabled=ShakeEnabled; View.Initialize(Sim.State);
            UI=gameObject.AddComponent<GameUI>(); UI.Initialize(this);
            audioSystem.PlayMusic();
        }

        void Update()
        {
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            if(noticeTimer>0) { noticeTimer-=dt; if(noticeTimer<=0) noticeSource=null; }
            if(Input.GetKeyDown(KeyCode.Escape))
            {
                if(!UI.CloseSubpage()) { if(Screen==GameScreen.Playing) Pause(); else if(Screen==GameScreen.Paused) Resume(); }
            }
            if((Screen==GameScreen.Playing || Screen==GameScreen.Paused) && Input.GetKeyDown(KeyCode.F5)) SaveNow();
            if(Screen==GameScreen.Playing)
            {
                if(IntroRemaining>0) IntroRemaining=Mathf.Max(0,IntroRemaining-dt);
                else if(Sim.State.Outcome==MatchOutcome.None)
                {
                    bomb1|=Input.GetKeyDown(KeyCode.Space);
                    bomb2|=Input.GetKeyDown(KeyCode.F);
                    accumulator+=dt;
                    int guard=0;
                    while(accumulator>=Step && guard++<6 && Sim.State.Outcome==MatchOutcome.None)
                    {
                        commands[0]=new PlayerCommand(ReadDirection(0),bomb1);
                        commands[1]=new PlayerCommand(ReadDirection(1),bomb2);
                        bomb1=bomb2=false;
                        Sim.Tick(Step,commands);
                        View.HandleEvents(Sim.Events); audioSystem.Events(Sim.Events,Sim.State);
                        accumulator-=Step;
                    }
                    if(Sim.State.TimeRemaining<30) { warningTimer-=dt; if(warningTimer<=0) { warningTimer=1; audioSystem.Warning(); } }
                    if(Sim.State.Outcome!=MatchOutcome.None) outcomeDelay=1.25f;
                }
                else
                {
                    outcomeDelay-=dt;
                    if(outcomeDelay<=0) EndRound();
                }
            }
            View.Render(Sim.State,dt,Screen==GameScreen.MainMenu);
            UI.Refresh();
        }
        static Vector2Int ReadDirection(int player)
        {
            // Physical W/A/S/D positions are labelled Z/Q/S/D on AZERTY.
            if(player==0)
            {
                if(Input.GetKey(KeyCode.UpArrow)) return Vector2Int.up;
                if(Input.GetKey(KeyCode.DownArrow)) return Vector2Int.down;
                if(Input.GetKey(KeyCode.LeftArrow)) return Vector2Int.left;
                if(Input.GetKey(KeyCode.RightArrow)) return Vector2Int.right;
            }
            else
            {
                if(Input.GetKey(KeyCode.W)) return Vector2Int.up;
                if(Input.GetKey(KeyCode.S)) return Vector2Int.down;
                if(Input.GetKey(KeyCode.A)) return Vector2Int.left;
                if(Input.GetKey(KeyCode.D)) return Vector2Int.right;
            }
            return Vector2Int.zero;
        }

        public void NewCampaign(int players)
        {
            PlayUISound();
            int seed=unchecked((int)DateTime.UtcNow.Ticks ^ Environment.TickCount);
            Sim=Simulation.CreateCampaign(Mathf.Clamp(players,1,2),seed);
            checkpoint=SaveRepository.Clone(Sim.State);
            BeginStage(true); SaveNow(false);
        }
        public void ContinueCampaign()
        {
            PlayUISound(); cachedSave=SaveRepository.Read(out var notice); Notice(notice);
            if(!HasSave) { Notice("No valid campaign to continue."); return; }
            Sim=Simulation.FromState(SaveRepository.Clone(cachedSave.Session));
            checkpoint=SaveRepository.Clone(cachedSave.Checkpoint);
            BeginStage(false);
            if(Sim.State.Outcome!=MatchOutcome.None) { outcomeDelay=0; EndRound(); }
        }
        void BeginStage(bool intro)
        {
            Screen=GameScreen.Playing; IntroRemaining=intro?3:1.5f; accumulator=0; bomb1=bomb2=false; outcomeDelay=0;
            View.Rebuild(Sim.State); audioSystem.SetPaused(false); audioSystem.PlayMusic(Sim.State.Stage);
            if(intro) audioSystem.Cue("ready",.48f);
        }
        void EndRound()
        {
            if(Sim.State.Outcome==MatchOutcome.Won)
            {
                Screen=Sim.State.Stage==4 ? GameScreen.Victory : GameScreen.StageClear;
                audioSystem.Cue(Screen==GameScreen.Victory?"victory":"clear",.62f);
                SaveNow(false);
            }
            else { Screen=GameScreen.GameOver; audioSystem.Cue("defeat",.5f); }
            audioSystem.SetPaused(true);
        }
        public void Pause()
        {
            if(Screen!=GameScreen.Playing) return;
            Screen=GameScreen.Paused; bomb1=bomb2=false; accumulator=0; audioSystem.SetPaused(true); audioSystem.Cue("pause",.35f);
        }
        public void Resume() { if(Screen!=GameScreen.Paused)return; PlayUISound(); Screen=GameScreen.Playing; audioSystem.SetPaused(false); }
        public void SaveAndMenu()
        {
            if(!SaveNow()) return;
            GoToMenu();
        }
        public bool SaveNow(bool showNotice=true)
        {
            if(SaveRepository.Write(Sim.State,checkpoint,out var error))
            {
                cachedSave=new CampaignSave { Session=SaveRepository.Clone(Sim.State), Checkpoint=SaveRepository.Clone(checkpoint??Sim.State), SavedAt=DateTime.UtcNow.ToString("o") };
                if(showNotice) { Notice("Campaign saved."); PlayUISound(); } return true;
            }
            Notice(error); return false;
        }
        public void GoToMenu()
        {
            PlayUISound(); Screen=GameScreen.MainMenu; IntroRemaining=0; accumulator=0;
            Sim=Simulation.CreateCampaign(2,812764); View.Rebuild(Sim.State);
            audioSystem.SetPaused(false); audioSystem.PlayMusic();
        }
        public void RestartStage()
        {
            if(checkpoint==null) return;
            PlayUISound(); Sim=Simulation.FromState(SaveRepository.Clone(checkpoint)); BeginStage(true); SaveNow(false);
        }
        public void NextStage()
        {
            if(Screen!=GameScreen.StageClear || Sim.State.Stage>=4)return;
            PlayUISound(); Sim.StartNextStage(); checkpoint=SaveRepository.Clone(Sim.State); BeginStage(true); SaveNow(false);
        }
        public void Quit()
        {
            PlayerPrefs.Save();
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
            #else
            Application.Quit();
            #endif
        }
        public void SetMusicVolume(float value) { MusicVolume=Mathf.Clamp01(value); PlayerPrefs.SetFloat("music",MusicVolume); audioSystem.SetVolumes(MusicVolume,SfxVolume); }
        public void SetSfxVolume(float value) { SfxVolume=Mathf.Clamp01(value); PlayerPrefs.SetFloat("sfx",SfxVolume); audioSystem.SetVolumes(MusicVolume,SfxVolume); }
        public void SetShake(bool value) { ShakeEnabled=value; View.ShakeEnabled=value; PlayerPrefs.SetInt("shake",value?1:0); }
        public void SetLanguage(GameLanguage language) { GameText.SetLanguage(language); }
        public void PlayUISound(bool confirm=true) => audioSystem.UI(confirm);
        public void Notice(string message) { if(string.IsNullOrEmpty(message)) return; noticeSource=message; noticeTimer=5; }
        void OnApplicationFocus(bool focus) { if(!focus && Screen==GameScreen.Playing) Pause(); }
        void OnApplicationQuit() { PlayerPrefs.Save(); if(Screen==GameScreen.Playing || Screen==GameScreen.Paused) SaveNow(false); }
        void OnDestroy() { if(Instance==this) Instance=null; }

        // Useful for deterministic integration checks via Pipeline. Never bound to player controls.
        public void LoadPreviewStage(int stage,int players=2)
        {
            Sim=Simulation.CreateCampaign(players,812764);
            for(int i=0;i<Mathf.Clamp(stage,0,4);i++) { Sim.State.Outcome=MatchOutcome.Won; Sim.StartNextStage(); }
            checkpoint=SaveRepository.Clone(Sim.State); BeginStage(false); IntroRemaining=0; Screen=GameScreen.Paused;
        }
    }
}
