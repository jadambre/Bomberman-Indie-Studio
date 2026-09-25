using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EmberGrid.Editor
{
    /// <summary>Editor-only integration checks and capture helpers, callable through Unity Pipeline.</summary>
    public static class QATools
    {
        public static void SetGameView(int width=1600,int height=900)
        {
            var assembly=typeof(UnityEditor.Editor).Assembly;
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes=singleton.GetProperty("instance").GetValue(null);
            var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new object[]{1});
            var groupType=group.GetType();
            var sizeType=assembly.GetType("UnityEditor.GameViewSize");
            var kindType=assembly.GetType("UnityEditor.GameViewSizeType");
            var size=Activator.CreateInstance(sizeType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{Enum.ToObject(kindType,1),width,height,"EMBERGRID QA"},null);
            groupType.GetMethod("AddCustomSize").Invoke(group,new[]{size});
            int count=(int)groupType.GetMethod("GetTotalCount").Invoke(group,null);
            var window=EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            var selected=window.GetType().GetProperty("selectedSizeIndex",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            selected.SetValue(window,count-1); window.Repaint();
        }

        public static string Smoke()
        {
            var app=GameApp.Instance;
            if(!Application.isPlaying || app==null) throw new Exception("Enter play mode first.");
            SaveRepository.TestDirectory=Path.GetFullPath("QA/Saves");
            app.NewCampaign(2);
            Require(app.Sim.State.PlayerCount==2,"two-player setup");
            Require(app.Sim.State.Actors.Count(a=>a.HumanIndex>=0)==2,"two human actors");
            app.Pause();
            Require(app.Screen==GameScreen.Paused,"pause screen");
            var state=app.Sim.State;
            var human=state.Actors.First(a=>a.HumanIndex==0);
            human.Shield=0; human.BombCapacity=3; human.FireRange=4; human.Speed=4.2f; human.WallPass=true;
            Require(app.Sim.PlaceBomb(human),"bomb placement");
            app.Sim.Tick(.13f,new[]{new PlayerCommand(Vector2Int.zero,false),new PlayerCommand(Vector2Int.zero,false)});
            string original=JsonUtility.ToJson(app.Sim.State);
            Require(app.SaveNow(false),"save write");
            app.GoToMenu();
            app.ContinueCampaign();
            Require(JsonUtility.ToJson(app.Sim.State)==original,"exact state restored, including active fuse");
            app.Pause();
            app.RestartStage();
            Require(app.Sim.State.Bombs.Count==0,"retry checkpoint");
            for(int stage=0;stage<5;stage++)
            {
                app.LoadPreviewStage(stage,2);
                Require(app.Sim.State.Stage==stage,"stage "+stage);
                Require(app.View.GameCamera!=null,"camera stage "+stage);
                Require(app.Sim.State.Actors.Count(a=>a.HumanIndex>=0)==2,"co-op stage "+stage);
            }
            app.LoadPreviewStage(0,2);
            app.Sim.State.Outcome=MatchOutcome.Won;
            app.SendMessage("EndRound");
            Require(app.Screen==GameScreen.StageClear,"stage clear screen");
            app.NextStage();
            Require(app.Sim.State.Stage==1 && app.Screen==GameScreen.Playing,"continue to next arena");
            app.LoadPreviewStage(4,2);
            app.Sim.State.Outcome=MatchOutcome.Won;
            app.SendMessage("EndRound");
            Require(app.Screen==GameScreen.Victory,"campaign completion screen");
            Require(!app.HasSave,"completed campaign does not offer stale Continue");
            app.LoadPreviewStage(2,1);
            app.Sim.State.Outcome=MatchOutcome.Lost;
            app.SendMessage("EndRound");
            Require(app.Screen==GameScreen.GameOver,"defeat screen");
            app.RestartStage();
            Require(app.Sim.State.Stage==2 && app.Sim.State.Outcome==MatchOutcome.None,"retry restores stage");
            app.GoToMenu();
            app.UI.Refresh();
            Require(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Length>0,"interactive menu buttons");
            const string report="PASS: co-op creation; pause; exact save/load with active bomb and upgrades; retry checkpoint; all five stage renderers; stage clear and next stage; campaign completion; defeat and retry; interactive menu.\n";
            Directory.CreateDirectory("QA"); File.WriteAllText("QA/integration-smoke.txt",report);
            return report;
        }
        static void Require(bool condition,string label) { if(!condition) throw new Exception("QA failed: "+label); }

        public static string LayoutAudit()
        {
            var texts=UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None);
            var problematic=texts.Where(t=>t.gameObject.activeInHierarchy && t.rectTransform.rect.width>1 && t.rectTransform.rect.height>1 && t.preferredHeight>t.rectTransform.rect.height+3 && t.verticalOverflow==VerticalWrapMode.Truncate)
                .Select(t=>t.name+": "+t.text.Replace("\n"," | ")+" (needs "+t.preferredHeight+", has "+t.rectTransform.rect.height+")").ToArray();
            return "Active texts: "+texts.Length+"; potential clipped labels: "+problematic.Length+"\n"+string.Join("\n",problematic);
        }
    }
}
