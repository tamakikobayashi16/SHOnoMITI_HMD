using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
public class PlayerInkProbe:MonoBehaviour {
 [Serializable] class Replay { public RemoteTrialRecorder.Entry[] points; }
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install(){var c=FindObjectOfType<PreGameControl>();if(c==null)return;c.useLocalTestTemplate=true;c.skipBalanceBoardInLocalTest=true;new GameObject("PlayerInkProbe").AddComponent<PlayerInkProbe>();}
 static RemoteTrialRecorder.Entry[] CreateDenseStroke(){
 var points=new RemoteTrialRecorder.Entry[1800];
 for(int i=0;i<points.Length;i++){float t=i/(float)(points.Length-1);points[i]=new RemoteTrialRecorder.Entry{character=0,stroke=0,x=-2+4*t+0.025f*Mathf.Sin(i*2f),y=2*Mathf.Sin(t*8)+0.025f*Mathf.Cos(i*2f),pressure=80};}
 return points;
 }
 IEnumerator Start(){
 yield return new WaitForSeconds(6);
 var c=FindObjectOfType<PreGameControl>();var f=typeof(PreGameControl).GetField("status",Flags);f.SetValue(c,Enum.Parse(f.FieldType,"READY_TO_WRITE"));
 var draw=FindObjectOfType<DrawTrail>();
 for(int n=0;n<3;n++){
 var data=Resources.Load<TextAsset>("InkReplay");
 var replay=data!=null?JsonUtility.FromJson<Replay>(data.text):new Replay{points=CreateDenseStroke()};int lastStroke=-1;
 foreach(var p in replay.points){if(p.character!=0)continue;if(p.stroke!=lastStroke){draw.EndStroke();draw.StartStroke();lastStroke=p.stroke;}draw.UpdateStroke(new Vector3(p.x,p.y,p.z),p.pressure);}draw.EndStroke();
 typeof(PreGameControl).GetField("charTimerSec",Flags).SetValue(c,0f);
 yield return new WaitForSeconds(3);
 foreach(var l in GameObject.Find("SavedGroup_"+n).GetComponentsInChildren<LineRenderer>())Debug.Log("PLAYER_INK "+n+" alignment="+l.alignment+" scale="+l.transform.lossyScale+" material="+l.sharedMaterial.name+" shader="+l.sharedMaterial.shader.name);
 }
 yield return new WaitForEndOfFrame();
 var rt=new RenderTexture(1920,1080,24);Camera.main.targetTexture=rt;Camera.main.Render();RenderTexture.active=rt;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(Application.dataPath,"../player-ink.png"),tex.EncodeToPNG());Camera.main.targetTexture=null;RenderTexture.active=null;
 yield return new WaitForSeconds(2);Debug.Log("PLAYER_INK_CAPTURE_PASS");Application.Quit();
 }
}
