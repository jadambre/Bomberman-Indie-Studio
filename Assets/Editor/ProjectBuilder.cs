using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BombermanIndieStudio.Editor
{
    public static class ProjectBuilder
    {
        [MenuItem("Bomberman Indie Studio/Configure project and create launch scene")]
        public static void Configure()
        {
            PlayerSettings.companyName="EmberGrid";
            PlayerSettings.productName="Bomberman Indie Studio";
            PlayerSettings.bundleVersion="1.0.0";
            PlayerSettings.defaultScreenWidth=1600;
            PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow=true;
            PlayerSettings.runInBackground=false;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.usePlayerLog=true;
            PlayerSettings.SplashScreen.show=false;
            var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=settings.FindProperty("activeInputHandler"); if(input!=null) input.intValue=0;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var inputAssets=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if(inputAssets.Length>0)
            {
                var inputSettings=new SerializedObject(inputAssets[0]);
                var physical=inputSettings.FindProperty("m_UsePhysicalKeys"); if(physical!=null) physical.boolValue=true;
                inputSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length-1,true);
            QualitySettings.antiAliasing=4;
            QualitySettings.vSyncCount=1;
            QualitySettings.shadows=ShadowQuality.All;
            QualitySettings.shadowResolution=ShadowResolution.High;
            QualitySettings.shadowDistance=70;
            QualitySettings.pixelLightCount=4;
            QualitySettings.softParticles=false;
            QualitySettings.realtimeReflectionProbes=false;
            GraphicsSettings.defaultRenderPipeline=null;
            QualitySettings.renderPipeline=null;
            // Runtime-created materials must have their shader included in a standalone build.
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders=graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach(var name in new[]{"Standard","Unlit/Color","Sprites/Default","UI/Default","UI/Default Font"})
            {
                var shader=Shader.Find(name); if(!shader)continue;
                bool found=false;
                for(int i=0;i<shaders.arraySize;i++) if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader) found=true;
                if(!found) { shaders.InsertArrayElementAtIndex(shaders.arraySize); shaders.GetArrayElementAtIndex(shaders.arraySize-1).objectReferenceValue=shader; }
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Bomberman Indie Studio · Game Director").AddComponent<GameApp>();
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.43f,.51f,.58f);
            RenderSettings.ambientEquatorColor=new Color(.22f,.29f,.35f);
            RenderSettings.ambientGroundColor=new Color(.12f,.15f,.20f);
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/BombermanIndieStudio.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/BombermanIndieStudio.unity",true)};
            ConfigureAudio();
            AssetDatabase.SaveAssets();
            Debug.Log("Bomberman Indie Studio project configured, launch scene saved.");
        }

        static void ConfigureAudio()
        {
            foreach(var guid in AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Resources/Audio"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                var importer=(AudioImporter)AssetImporter.GetAtPath(path);
                var music=Path.GetFileName(path).StartsWith("music_");
                var sample=importer.defaultSampleSettings;
                sample.loadType=music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat=AudioCompressionFormat.Vorbis;
                sample.quality=music?.7f:.85f;
                sample.sampleRateSetting=AudioSampleRateSetting.OptimizeSampleRate;
                importer.defaultSampleSettings=sample;
                importer.forceToMono=!music;
                importer.loadInBackground=music;
                importer.SaveAndReimport();
            }
        }
        [MenuItem("Bomberman Indie Studio/Build Windows release")]
        public static void BuildWindows()
        {
            if(!File.Exists("Assets/Scenes/BombermanIndieStudio.unity")) Configure();
            PlayerSettings.productName="Bomberman Indie Studio";
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds/Windows");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/BombermanIndieStudio.unity"}, locationPathName="Builds/Windows/BombermanIndieStudio.exe",
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.None
            });
            var report="Result: "+result.summary.result+"\nSize: "+result.summary.totalSize+" bytes\nTime: "+result.summary.totalTime+"\nErrors: "+result.summary.totalErrors;
            Directory.CreateDirectory("QA"); File.WriteAllText("QA/build-report.txt",report);
            Debug.Log(report);
            if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Windows build failed.");
        }
    }
}
