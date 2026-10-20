using System;
using System.Reflection;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class GalleryCapture {
 static double started; static int stage; static float resultAt;
 public static void Run(){
 if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets")) throw new Exception("Run only in the isolated verification project");
 EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/RemoteGame.unity",true),new EditorBuildSettingsScene("Assets/Scenes/GoldResult.unity",true)};
 EditorSceneManager.OpenScene("Assets/Scenes/RemoteGame.unity");
 var c=UnityEngine.Object.FindObjectOfType<PreGameControl>(); c.useLocalTestTemplate=true; c.skipBalanceBoardInLocalTest=true;
 EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
 EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
 started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
 }
 static void Tick(){
 if(EditorApplication.timeSinceStartup-started>70){EditorApplication.Exit(1);return;}
 if(!EditorApplication.isPlaying || Time.time<6)return;
 if(stage==0){
 var c=UnityEngine.Object.FindObjectOfType<PreGameControl>();c.enabled=false;
 var d=UnityEngine.Object.FindObjectOfType<DrawTrail>();
 var paper=GameObject.Find("MoveArea").GetComponent<Renderer>();var b=paper.bounds;
 Debug.Log("PAPER "+b+" scale="+paper.transform.lossyScale);
 d.StartStroke();for(int i=0;i<20;i++)d.UpdateStroke(new Vector3(b.center.x-2+i*.2f,b.center.y-2+i*.2f,0),15f);d.EndStroke();
 typeof(PreGameControl).GetMethod("SaveGlyphObjectToGallery",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{d.PopLastGlyph()});
 stage=1;return;
 }
 if(stage==1){
 foreach(var r in UnityEngine.Object.FindObjectsOfType<Renderer>())if(r.name.StartsWith("Saved_")||r.name.Contains("Line"))Debug.Log("GALLERY "+r.name+" bounds="+r.bounds+" scale="+r.transform.lossyScale);
 ScreenCapture.CaptureScreenshot(Path.GetFullPath("gallery.png"));stage=2;return;}
 if(stage==2&&File.Exists("gallery.png")){
 var c=UnityEngine.Object.FindObjectOfType<PreGameControl>();
 typeof(PreGameControl).GetMethod("TriggerCompletionAnimations",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,null);
 resultAt=Time.time;stage=5;return;}
 if(stage==5&&Time.time-resultAt>9){
 var c=UnityEngine.Object.FindObjectOfType<PreGameControl>();
 typeof(PreGameControl).GetField("_readyForResult",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);
 typeof(PreGameControl).GetMethod("LoadResultScene",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{"GoldResult"});resultAt=Time.time;stage=3;return;}
 if(stage==3&&Time.time-resultAt>12){
 Debug.Log("RESULT_CAMERA "+Camera.main.transform.position+" rot="+Camera.main.transform.eulerAngles);
 foreach(var r in UnityEngine.Object.FindObjectsOfType<Renderer>())Debug.Log("RESULT_RENDER "+r.name+" "+r.bounds+" enabled="+r.enabled);
ScreenCapture.CaptureScreenshot(Path.GetFullPath("gallery-result.png"));stage=4;return;}
 if(stage==4&&File.Exists("gallery-result.png")){Debug.Log("GALLERY_RESULT_CAPTURE_PASS");EditorApplication.Exit(0);}
 }
}
